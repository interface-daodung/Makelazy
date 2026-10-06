using System.IO;

namespace Makelazy.App.Services;

/// <summary>
/// Parse CLI. Hỗ trợ:
///   Makelazy.exe ["path/Makefile" | "path/folder"] [-t target] [-l] [--register] [--unregister] [-h] [-v]
/// Kéo-thả / Open-With / double-click đều đi qua đây (Windows truyền path file dạng arg đầu tiên).
/// </summary>
public sealed class CliOptions
{
    public string? MakefilePath { get; set; }
    public string? AutoTarget { get; set; }
    public bool ListTargets { get; set; }
    public bool Register { get; set; }
    public bool Unregister { get; set; }
    public bool Help { get; set; }
    public bool Version { get; set; }

    public bool IsCliVerb => ListTargets || Register || Unregister || Help || Version;

    public const string HelpText =
@"Makelazy — Makefile Runner
Dùng: Makelazy.exe [Makefile | thư-mục] [tùy-chọn]

  [Makefile]              File 'Makefile' (không đuôi). Kéo-thả, Open-With,
                          double-click sau khi --register đều truyền path vào đây.
                          Truyền thư-mục cũng được (tự tìm Makefile bên trong).
  -t, --target <name>     Tự chạy target ngay sau khi mở file (mở GUI).
  -l, --list              Chỉ in danh sách target ra console rồi thoát (không mở GUI).
  --register              Đăng ký Open-With cho file Makefile (HKCU, không cần admin):
                          double-click file không-đuôi + menu chuột phải thư-mục.
  --unregister            Gỡ đăng ký trên.
  -h, --help              Bản trợ giúp này.
  -v, --version           In phiên bản.

Ví dụ:
  Makelazy.exe C:\proj\Makefile
  Makelazy.exe C:\proj --target build
  Makelazy.exe C:\proj\Makefile --list
  Makelazy.exe --register";

    public static CliOptions Parse(IEnumerable<string> argsNoExe)
    {
        var o = new CliOptions();
        string? pendingTargetOpt = null;
        bool expectTarget = false;

        foreach (var raw in argsNoExe)
        {
            var a = raw.Trim().Trim('"');
            if (expectTarget) { pendingTargetOpt = a; expectTarget = false; continue; }

            if (a.Equals("-t", StringComparison.OrdinalIgnoreCase) || a.Equals("--target", StringComparison.OrdinalIgnoreCase))
            { expectTarget = true; continue; }
            if (a.StartsWith("--target=", StringComparison.OrdinalIgnoreCase))
            { pendingTargetOpt = a.Substring("--target=".Length).Trim('"'); continue; }
            if (a.Equals("-l", StringComparison.OrdinalIgnoreCase) || a.Equals("--list", StringComparison.OrdinalIgnoreCase))
            { o.ListTargets = true; continue; }
            if (a.Equals("--register", StringComparison.OrdinalIgnoreCase))
            { o.Register = true; continue; }
            if (a.Equals("--unregister", StringComparison.OrdinalIgnoreCase))
            { o.Unregister = true; continue; }
            if (a.Equals("-h", StringComparison.OrdinalIgnoreCase) || a.Equals("--help", StringComparison.OrdinalIgnoreCase)
                || a.Equals("/?", StringComparison.OrdinalIgnoreCase) || a.Equals("/h", StringComparison.OrdinalIgnoreCase))
            { o.Help = true; continue; }
            if (a.Equals("-v", StringComparison.OrdinalIgnoreCase) || a.Equals("--version", StringComparison.OrdinalIgnoreCase))
            { o.Version = true; continue; }
            if (a.StartsWith('-') || a.StartsWith('/'))
                continue; // flag lạ: bỏ qua cho nhẹ

            // positional đầu tiên = path file/thư mục
            if (o.MakefilePath is null)
                o.MakefilePath = ResolveMakefilePath(a);
        }

        o.AutoTarget = string.IsNullOrWhiteSpace(pendingTargetOpt) ? null : pendingTargetOpt;
        return o;
    }

    /// <summary>Chuẩn hoá arg thành path Makefile: file trực tiếp, relative, hoặc thư-mục chứa Makefile.</summary>
    public static string? ResolveMakefilePath(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var p = raw.Trim().Trim('"');
        if (File.Exists(p)) return Path.GetFullPath(p);
        var combined = Path.Combine(Environment.CurrentDirectory, p);
        if (File.Exists(combined)) return Path.GetFullPath(combined);
        var dir = Directory.Exists(p) ? p
            : Directory.Exists(combined) ? combined : null;
        if (dir is not null)
        {
            foreach (var name in new[] { "Makefile", "GNUmakefile", "makefile" })
            {
                var f = Path.Combine(dir, name);
                if (File.Exists(f)) return Path.GetFullPath(f);
            }
        }
        return null;
    }
}
