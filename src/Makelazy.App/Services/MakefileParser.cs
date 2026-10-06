using System.IO;
using System.Text.RegularExpressions;
using Makelazy.App.Models;

namespace Makelazy.App.Services;

/// <summary>
/// Parse Makefile (file tên "Makefile", không đuôi) thành danh sách targets.
/// Hỗ trợ: target: deps, recipe dòng tab, comment ## mô tả, .PHONY.
/// </summary>
public static partial class MakefileParser
{
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

            // Bỏ variable assignment (foo = bar, :=, ?=, +=, !=) — chứa '=' trước ':'
            // Target line: có ':' và KHÔNG có '='.
            if (!raw.Contains(':') || raw.Contains('=')) continue;
            // Bỏ các directive bắt đầu bằng '.' như .PHONY, .DEFAULT_GOAL
            if (trimmed.StartsWith('.')) continue;

            var m = TargetLineRegex().Match(raw);
            if (!m.Success) continue;

            var name = m.Groups["name"].Value.Trim();
            if (string.IsNullOrWhiteSpace(name) || name.Contains(' ') || name.Contains('$'))
                continue;
            // Bỏ pattern rule chứa '%'
            if (name.Contains('%')) continue;

            var depsRaw = m.Groups["deps"].Value.Trim();
            // deps có thể chứa comment sau '#'
            var hashIdx = depsRaw.IndexOf('#');
            if (hashIdx >= 0) depsRaw = depsRaw.Substring(0, hashIdx).Trim();
            var deps = depsRaw.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

            // Gom recipe lines kế tiếp (bắt đầu bằng tab)
            var commands = new List<string>();
            int j = i + 1;
            while (j < lines.Length && (lines[j].StartsWith('\t') || lines[j].StartsWith("        ")))
            {
                var cmd = lines[j].TrimStart('\t', ' ');
                // bỏ prefix make: @ - +
                cmd = cmd.TrimStart('@', '-', '+').TrimStart();
                if (!string.IsNullOrWhiteSpace(cmd))
                    commands.Add(cmd);
                j++;
            }

            result.Add(new MakefileTarget(
                Name: name,
                Dependencies: deps,
                Commands: commands.ToArray(),
                Line: i + 1,
                Description: pendingDesc ?? string.Empty,
                IsPhony: phony.Contains(name)));
            pendingDesc = null;
        }

        return result;
    }

    [GeneratedRegex(@"^(?<name>[A-Za-z0-9][A-Za-z0-9_\-./]*)\s*:(?:\s*(?<deps>[^#]*))?$")]
    private static partial Regex TargetLineRegex();
}
