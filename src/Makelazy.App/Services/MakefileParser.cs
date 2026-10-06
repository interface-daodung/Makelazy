using System.IO;
using System.Text.RegularExpressions;
using Makelazy.App.Models;

namespace Makelazy.App.Services;

/// <summary>
/// Parse Makefile (file tên "Makefile", không đuôi) thành danh sách targets.
/// Hỗ trợ: multi-target "a b c: deps", deps nối dòng "\", lọc deps hiển thị
/// (bỏ biến $(...), pattern %, order-only |, file path), recipe dòng tab,
/// comment ## mô tả (trên hoặc inline), .PHONY, khối ifeq/ifdef/else/endif,
/// bỏ qua khối define/endef.
/// </summary>
public static partial class MakefileParser
{
    private static readonly char[] Whitespace = { ' ', '\t' };

    private static readonly HashSet<string> DirectiveWords = new(StringComparer.Ordinal)
    {
        "ifeq", "ifneq", "ifdef", "ifndef", "else", "endif",
        "include", "-include", "sinclude",
        "export", "unexport", "override", "define", "endef", "private",
    };

    private static bool IsDirective(string trimmed)
    {
        var word = trimmed.Split(Whitespace, 2, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault() ?? string.Empty;
        word = word.TrimStart('-'); // "-include"
        if (word.EndsWith(':')) word = word.Substring(0, word.Length - 1);
        return DirectiveWords.Contains(word);
    }

    private static bool IsVariableAssignment(string raw)
    {
        // VAR = ..., VAR := ..., VAR ?= ..., VAR += ..., VAR != ..., export VAR = ...
        return VariableAssignRegex().IsMatch(raw);
    }

    private static string FirstWord(string trimmed)
    {
        var idx = trimmed.IndexOfAny(Whitespace);
        return idx < 0 ? trimmed : trimmed.Substring(0, idx);
    }

    private static bool IsDefineStart(string trimmed)
    {
        // "define FOO", "define FOO =", "override define FOO".
        // Dòng define thật không bao giờ chứa ':' (guard tránh nhận nhầm
        // target kiểu "foo: define").
        if (trimmed.Contains(':')) return false;
        var words = trimmed.Split(Whitespace, 3, StringSplitOptions.RemoveEmptyEntries);
        return (words.Length > 0 && words[0] == "define")
            || (words.Length > 1 && words[1] == "define");
    }

    /// <summary>
    /// Lọc deps để hiển thị: bỏ order-only ("| ..."), biến make ($(X)/$X),
    /// pattern (%), file path — chỉ giữ tên target thật.
    /// </summary>
    private static string[] ParseDependencies(string rest)
    {
        var deps = new List<string>();
        foreach (var tok in rest.Split(Whitespace, StringSplitOptions.RemoveEmptyEntries))
        {
            if (tok == "|") break;             // order-only: bỏ hết phần sau
            if (tok.StartsWith('$')) continue; // $(OBJS), $VAR: make tự expand
            if (tok.Contains('%')) continue;  // pattern %.o
            if (tok.Contains('/'))
            {
                // "src/a.c", "a/b/c": path file, không phải target
                if (tok.Count(c => c == '/') > 1 || tok.Contains('.')) continue;
            }
            deps.Add(tok);
        }
        return deps.ToArray();
    }

    public static List<MakefileTarget> Parse(string makefilePath)
    {
        var lines = File.ReadAllLines(makefilePath);
        var result = new List<MakefileTarget>();
        var phony = new HashSet<string>(StringComparer.Ordinal);

        // Pass 1: gom .PHONY
        foreach (var raw in lines)
        {
            var t = raw.Trim();
            if (t.StartsWith(".PHONY", StringComparison.Ordinal))
            {
                var idx = t.IndexOf(':');
                if (idx >= 0)
                {
                    var rest = t.Substring(idx + 1);
                    // bỏ comment Inline
                    var h = rest.IndexOf('#');
                    if (h >= 0) rest = rest.Substring(0, h);
                    foreach (var p in rest.Split(Whitespace, StringSplitOptions.RemoveEmptyEntries))
                        phony.Add(p.Trim());
                }
            }
        }

        string? pendingDesc = null;
        var inDefine = false;

        for (int i = 0; i < lines.Length; i++)
        {
            var raw = lines[i];
            if (string.IsNullOrWhiteSpace(raw)) continue;

            var trimmed = raw.Trim();

            // Trong khối define ... endef: bỏ toàn bộ nội dung,
            // tránh parse nhầm dòng chứa ":" thành target giả.
            if (inDefine)
            {
                if (FirstWord(trimmed) == "endef") inDefine = false;
                continue;
            }

            // comment mô tả: ## xxx  (giữ lại cho target kế tiếp)
            if (trimmed.StartsWith("##"))
            {
                pendingDesc = trimmed.Substring(2).Trim();
                continue;
            }
            if (trimmed.StartsWith('#'))
                continue;

            // Dòng recipe (tab / 8 spaces) -> bỏ qua ở pass này
            if (raw.StartsWith('\t') || raw.StartsWith("        "))
                continue;

            // Mở khối define
            if (IsDefineStart(trimmed)) { inDefine = true; pendingDesc = null; continue; }

            // Bỏ directive điều kiện / include / export...
            if (IsDirective(trimmed))
                continue;

            // Bỏ variable assignment (foo = bar, :=, ?=, +=, !=)
            if (IsVariableAssignment(raw)) continue;
            if (!raw.Contains(':')) continue;
            // Bỏ các rule đặc biệt bắt đầu bằng '.' như .PHONY, .DEFAULT_GOAL
            if (trimmed.StartsWith('.')) continue;

            // Target line continuation: "foo: a \" + nối các dòng kế tiếp.
            // Match trên raw (giữ nguyên đầu dòng) để giữ hành vi cũ: dòng
            // recipe thụt đầu dòng bằng space không bị nhận nhầm thành target.
            var ruleStart = i;
            var ruleLine = raw;
            string stripped;
            while ((stripped = ruleLine.TrimEnd()).EndsWith("\\", StringComparison.Ordinal)
                   && i + 1 < lines.Length)
            {
                ruleLine = stripped.Substring(0, stripped.Length - 1) + " " + lines[i + 1].Trim();
                i++;
            }

            var m = TargetLineRegex().Match(ruleLine);
            if (!m.Success) continue;

            // Multi-target: "build test: deps" -> mỗi tên một target
            var names = m.Groups["names"].Value.Split(Whitespace, StringSplitOptions.RemoveEmptyEntries);
            var valid = new List<string>(names.Length);
            foreach (var n in names)
            {
                // Bỏ special target (.PHONY...), biến $(...)/$X, pattern %
                if (n.StartsWith('.') || n.Contains('$') || n.Contains('%')) continue;
                valid.Add(n);
            }
            if (valid.Count == 0) continue;

            var rest = m.Groups["rest"].Value ?? string.Empty;
            // Rule "::" kép -> rest thừa dấu ":" ở đầu
            if (rest.StartsWith(":")) rest = rest.Substring(1);
            // Tách mô tả Inline "## ..." (ưu tiên), còn lại "#" là comment thường
            string inlineDesc = string.Empty;
            var ddIdx = rest.IndexOf("##", StringComparison.Ordinal);
            if (ddIdx >= 0)
            {
                inlineDesc = rest.Substring(ddIdx + 2).Trim();
                rest = rest.Substring(0, ddIdx);
            }
            else
            {
                var hIdx = rest.IndexOf('#');
                if (hIdx >= 0) rest = rest.Substring(0, hIdx);
            }
            // Hỗ trợ "target: deps ; recipe Inline"
            var semiIdx = rest.IndexOf(';');
            string? inlineRecipe = null;
            if (semiIdx >= 0)
            {
                inlineRecipe = rest.Substring(semiIdx + 1).Trim();
                rest = rest.Substring(0, semiIdx);
            }
            var deps = ParseDependencies(rest);

            // Gom recipe lines kế tiếp (bắt đầu bằng tab), xuyên qua
            // các directive ifeq/ifdef/else/endif (vd: target có 2 nhánh recipe).
            var commands = new List<string>();
            if (!string.IsNullOrWhiteSpace(inlineRecipe))
            {
                var cmd0 = inlineRecipe.TrimStart('@', '-', '+').TrimStart();
                if (!string.IsNullOrWhiteSpace(cmd0)) commands.Add(cmd0);
            }
            int j = i + 1;
            while (j < lines.Length)
            {
                var nxt = lines[j];
                var nTrim = nxt.Trim();
                if (string.IsNullOrWhiteSpace(nxt)) { j++; continue; }
                if (nTrim.StartsWith('#')) { j++; continue; }
                if (IsDirective(nTrim)) { j++; continue; }
                if (nxt.StartsWith('\t') || nxt.StartsWith("        "))
                {
                    var cmd = nxt.TrimStart('\t', ' ');
                    // bỏ prefix make: @ - +
                    cmd = cmd.TrimStart('@', '-', '+').TrimStart();
                    if (!string.IsNullOrWhiteSpace(cmd))
                        commands.Add(cmd);
                    j++;
                    continue;
                }
                break;
            }

            var desc = !string.IsNullOrWhiteSpace(inlineDesc) ? inlineDesc : (pendingDesc ?? string.Empty);
            foreach (var n in valid)
            {
                result.Add(new MakefileTarget(
                    Name: n,
                    Dependencies: deps,
                    Commands: commands.ToArray(),
                    Line: ruleStart + 1,
                    Description: desc,
                    IsPhony: phony.Contains(n)));
            }
            pendingDesc = null;
        }

        return result;
    }

    [GeneratedRegex(@"^(?<names>[A-Za-z0-9][^:#=]*)\s*:(?<rest>.*)$")]
    private static partial Regex TargetLineRegex();

    [GeneratedRegex(@"^\s*(export\s+|override\s+)?[A-Za-z0-9_.\-]+\s*(\?|::?|\+|!)?=\s*")]
    private static partial Regex VariableAssignRegex();
}
