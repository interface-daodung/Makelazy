using System.Diagnostics;
using System.IO;
using System.Text;

namespace Makelazy.App.Services;

/// <summary>
/// Chạy target trong process ẩn (CreateNoWindow), stream stdout/stderr về UI, cho phép gõ stdin.
/// - Nếu có GNU Make: `make &lt;target&gt;`.
/// - Nếu KHÔNG có make (máy Windows thường chưa cài): chạy thẳng từng dòng recipe bằng cmd.
///   Nhờ vậy nút ▶ luôn bấm-chạy được, không bắt buộc cài make.
/// Mỗi lần run = 1 session (1 tab terminal bên phải).
/// </summary>
public sealed class MakeSession : IDisposable
{
    private readonly Process _proc;
    private readonly string _banner;
    private bool _disposed;

    public string SessionId { get; } = Guid.NewGuid().ToString("N").Substring(0, 8);
    public string Target { get; }
    public string MakefilePath { get; }
    public bool DirectMode { get; }
    public int? ExitCode { get; private set; }
    public bool IsRunning => !_proc.HasExited;

    public event Action<MakeSession, string>? OnOutput;
    public event Action<MakeSession, int>? OnExit;

    public MakeSession(string makefilePath, string target, string[] recipe)
    {
        MakefilePath = makefilePath;
        Target = target;
        var workDir = Path.GetDirectoryName(Path.GetFullPath(makefilePath)) ?? Environment.CurrentDirectory;

        string arguments;
        if (HasCommand("make"))
        {
            DirectMode = false;
            arguments = "/d /s /c \"chcp 65001 >nul & make " + EscapeArg(target) + "\"";
            _banner = "\x1b[90m$ make " + target + "\x1b[0m\r\n";
        }
        else if (recipe.Length > 0)
        {
            DirectMode = true;
            // Chạy thẳng recipe (đã strip @/-/+ khi parse). Nối bằng & — tương thích cả && / || bên trong.
            var joined = string.Join(" & ", recipe);
            arguments = "/d /s /c \"chcp 65001 >nul & " + joined + "\"";
            _banner = "\x1b[33m(!) Không tìm thấy 'make' trong PATH — chạy trực tiếp recipe của target '"
                + target + "'.\x1b[0m\r\n"
                + "\x1b[33mCài GNU Make (choco install make) để chạy đúng chuẩn.\x1b[0m\r\n";
        }
        else
        {
            throw new InvalidOperationException(
                "Không tìm thấy 'make' trong PATH và target không có recipe. Cài GNU Make: choco install make");
        }

        _proc = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = arguments,
                WorkingDirectory = workDir,
                UseShellExecute = false,
                CreateNoWindow = true,          // terminal ẩn
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            },
            EnableRaisingEvents = true,
        };
        _proc.OutputDataReceived += (_, e) => { if (e.Data is not null) OnOutput?.Invoke(this, e.Data + "\n"); };
        _proc.ErrorDataReceived += (_, e) => { if (e.Data is not null) OnOutput?.Invoke(this, e.Data + "\n"); };
        _proc.Exited += (_, _) =>
        {
            try { ExitCode = _proc.ExitCode; } catch { ExitCode = -1; }
            OnExit?.Invoke(this, ExitCode ?? -1);
        };
    }

    public void Start()
    {
        _proc.Start();
        _proc.BeginOutputReadLine();
        _proc.BeginErrorReadLine();
        OnOutput?.Invoke(this, _banner);
    }

    public void WriteInput(string data)
    {
        if (_disposed || _proc.HasExited) return;
        try { _proc.StandardInput.Write(data); _proc.StandardInput.Flush(); }
        catch { /* process đã chết */ }
    }

    public void Kill()
    {
        try
        {
            if (!_proc.HasExited) _proc.Kill(entireProcessTree: true);
        }
        catch { /* ignore */ }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { if (!_proc.HasExited) _proc.Kill(entireProcessTree: true); } catch { }
        _proc.Dispose();
    }

    private static bool HasCommand(string cmd)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/d /s /c \"where " + cmd + " >nul 2>&1\"",
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (p is null) return false;
            return p.WaitForExit(5000) && p.ExitCode == 0;
        }
        catch { return false; }
    }

    private static string EscapeArg(string s) =>
        "\"" + s.Replace("\"", "\\\"") + "\"";
}
