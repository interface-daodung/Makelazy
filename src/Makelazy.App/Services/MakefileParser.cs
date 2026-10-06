using System.IO;
using System.Text.RegularExpressions;
using Makelazy.App.Models;

namespace Makelazy.App.Services;

/// <summary>
/// Parse Makefile (file tên "Makefile", không đuôi) thành danh sách targets.
/// Hỗ trợ: target: deps [# mô tả], recipe dòng tab, comment ## mô tả (trên hoặc inline),
/// .PHONY, khối điều kiện ifeq/ifdef/else/endif.
/// </summary>
public static partial class MakefileParser
{
    private static readonly HashSet<string> DirectiveWords = new(StringComparer.Ordinal)
    {
        "ifeq", "ifneq", "ifdef", "ifndef", "else", "endif",
        "include", "-include", "sinclude",
        "export", "unexport", "override", "define", "endef", "private",
    };

    private static bool IsDirective(string trimmed)
    {
        var word = trimmed.Split(new char[] { ' ', '\t' }, 2, StringSplitOptions.RemoveEmptyEntries)
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
                    foreach (var p in rest.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
                        phony.Add(p.Trim());
                }
            }
        }

        string? pendingDesc = null;

        for (int i = 0; i < lines.Length; i++)
        {
            var raw = lines[i];
            if (string.IsNullOrWhiteSpace(raw)) continue;

            var trimmed = raw.Trim();

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

            // Bỏ directive điều kiện / include / export...
            if (IsDirective(trimmed))
                continue;

            // Bỏ variable assignment (foo = bar, :=, ?=, +=, !=)
            if (IsVariableAssignment(raw)) continue;
            if (!raw.Contains(':')) continue;
            // Bỏ các rule đặc biệt bắt đầu bằng '.' như .PHONY, .DEFAULT_GOAL
            if (trimmed.StartsWith('.')) continue;

            var m = TargetLineRegex().Match(raw);
            if (!m.Success) continue;

            var name = m.Groups["name"].Value.Trim();
            if (string.IsNullOrWhiteSpace(name) || name.Contains(' ') || name.Contains('$'))
                continue;
            // Bỏ pattern rule chứa '%'
            if (name.Contains('%')) continue;

            var rest = m.Groups["rest"].Value ?? string.Empty;
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
            var deps = rest.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

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
            result.Add(new MakefileTarget(
                Name: name,
                Dependencies: deps,
                Commands: commands.ToArray(),
                Line: i + 1,
                Description: desc,
                IsPhony: phony.Contains(name)));
            pendingDesc = null;
        }

        return result;
    }

    [GeneratedRegex(@"^(?<name>[A-Za-z0-9][A-Za-z0-9_\-./]*)\s*:(?<rest>.*)$")]
    private static partial Regex TargetLineRegex();

    [GeneratedRegex(@"^\s*(export\s+|override\s+)?[A-Za-z0-9_.\-]+\s*(\?|::?|\+|!)?=\s*")]
    private static partial Regex VariableAssignRegex();
}
