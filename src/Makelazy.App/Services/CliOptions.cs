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
    public bool Silent { get; set; } // --silent: không chờ phím (cho installer gọi ngầm)

    public bool IsCliVerb => ListTargets || Register || Unregister || Help || Version;

    /// <summary>
    /// Path file/thư mục truyền vào nhưng bị từ chối vì tên file không phải
    /// "makefile"/"Makefile". App dùng để báo lỗi rồi thoát (không mở GUI).
    /// </summary>
    public string? RejectedPath { get; set; }

    /// <summary>Tên file phải chính xác là "makefile" hoặc "Makefile" (so khớp tuyệt đối).</summary>
    public static bool IsAcceptedMakefileName(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        var name = Path.GetFileName(path.Trim().Trim('"'));
        return name.Equals("makefile", StringComparison.Ordinal)
            || name.Equals("Makefile", StringComparison.Ordinal);
    }

    public static string UnsupportedFileMessage(string? rawPath)
    {
        var name = string.IsNullOrWhiteSpace(rawPath) ? "?" : Path.GetFileName(rawPath.Trim().Trim('"'));
        return $"File \"{name}\" không được hỗ trợ.\nMakelazy chỉ mở file có tên chính xác là \"Makefile\" hoặc \"makefile\".";
    }

    public const string HelpText =
@"Makelazy — Makefile Runner
Dùng: Makelazy.exe [Makefile | thư-mục] [tùy-chọn]

  [Makefile]              File tên chính xác 'Makefile' hoặc 'makefile' (không đuôi).
                          Kéo-thả, Open-With, double-click sau khi --register
                          đều truyền path vào đây. File khác tên sẽ bị từ chối.
                          Truyền thư-mục cũng được (tự tìm Makefile bên trong).
  -t, --target <name>     Tự chạy target ngay sau khi mở file (mở GUI).
  -l, --list              Chỉ in danh sách target ra console rồi thoát (không mở GUI).
  --register              Đăng ký Open-With cho file Makefile (HKCU, không cần admin):
                          double-click file không-đuôi + menu chuột phải thư-mục.
  --unregister            Gỡ đăng ký trên.
  --silent                Dùng kèm verb trên: không chờ bấm phím (cho installer gọi ngầm).
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
        string? positionalRaw = null;

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
            if (a.Equals("--silent", StringComparison.OrdinalIgnoreCase))
            { o.Silent = true; continue; }
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
            if (positionalRaw is null)
                positionalRaw = a;
            if (o.MakefilePath is null)
                o.MakefilePath = ResolveMakefilePath(a);
        }

        o.AutoTarget = string.IsNullOrWhiteSpace(pendingTargetOpt) ? null : pendingTargetOpt;

        // Chốt kiểm tra tên file: chỉ chấp nhận đúng "makefile"/"Makefile".
        // - Truyền file sai tên (kể cả thư-mục resolve ra file sai tên) -> từ chối.
        if (o.MakefilePath is not null && !IsAcceptedMakefileName(o.MakefilePath))
        {
            o.RejectedPath = o.MakefilePath;
            o.MakefilePath = null;
        }
        else if (o.MakefilePath is null && positionalRaw is not null
                 && !IsAcceptedMakefileName(positionalRaw) && !IsDirectoryPath(positionalRaw))
        {
            o.RejectedPath = positionalRaw;
        }
        return o;
    }

    private static bool IsDirectoryPath(string raw)
    {
        var p = raw.Trim().Trim('"');
        if (Directory.Exists(p)) return true;
        return Directory.Exists(Path.Combine(Environment.CurrentDirectory, p));
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
