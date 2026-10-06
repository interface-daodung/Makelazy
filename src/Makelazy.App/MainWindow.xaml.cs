using System.IO;
using System.Text.Json;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Microsoft.Win32;
using Makelazy.App.Models;
using Makelazy.App.Services;

namespace Makelazy.App;

public partial class MainWindow : Window
{
    private readonly Dictionary<string, MakeSession> _sessions = new();
    private string? _currentMakefile;
    private List<MakefileTarget> _lastTargets = new();

    /// <summary>File Makefile truyền qua CLI / Open-With / single-instance. App.xaml gán trước khi Show().</summary>
    public string? PendingMakefile { get; set; }
    /// <summary>Target tự chạy sau khi mở file (CLI --target).</summary>
    public string? PendingTarget { get; set; }

    public MainWindow()
    {
        InitializeComponent();
        Drop += OnDrop;
        Loaded += async (_, _) => await InitWebAsync();
        Closed += (_, _) =>
        {
            foreach (var s in _sessions.Values) { try { s.Dispose(); } catch { } }
        };
    }

    // ---------- WebView2 init ----------
    private async Task InitWebAsync()
    {
        try
        {
            await Web.EnsureCoreWebView2Async();
        }
        catch (Exception ex)
        {
            // WebView2 Runtime chưa cài / lỗi init: hiện lỗi rõ ràng thay vì treo splash
            SplashMsg.Text = "Không khởi động được WebView2: " + ex.Message
                + "\nHãy cài WebView2 Runtime (miễn phí) rồi mở lại app.";
            MessageBox.Show(this,
                "Không khởi động được WebView2.\n\n" + ex.Message
                + "\n\nHãy cài đặt WebView2 Runtime từ Microsoft rồi mở lại Makelazy.",
                "Makelazy — thiếu WebView2 Runtime",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        Web.CoreWebView2.Settings.AreDevToolsEnabled = true;
        Web.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
        Web.CoreWebView2.WebMessageReceived += OnWebMessage;
        Web.CoreWebView2.NavigationCompleted += (_, _) => { Splash.Visibility = Visibility.Collapsed; };

        var wwwroot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
        var indexHtml = Path.Combine(wwwroot, "index.html");

        // Dev mode: nếu chưa build frontend mà đang chạy vite (npm run dev) thì load localhost
        var devUrl = Environment.GetEnvironmentVariable("MAKELAZY_DEV_URL");
        if (!string.IsNullOrWhiteSpace(devUrl))
        {
            Web.CoreWebView2.Navigate(devUrl);
            return;
        }

        if (File.Exists(indexHtml))
        {
            Web.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "appassets", wwwroot, CoreWebView2HostResourceAccessKind.Allow);
            Web.CoreWebView2.Navigate("https://appassets/index.html");
        }
        else
        {
            // Chưa có wwwroot (chưa npm run build): thử vite dev server
            Web.CoreWebView2.Navigate("http://localhost:5173");
        }
    }

    // ---------- Bridge JS -> C# ----------
    private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var json = e.TryGetWebMessageAsString();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var type = root.GetProperty("type").GetString();

            switch (type)
            {
                case "ready":
                    AutoLoadFromArgs();
                    break;
                case "openMakefile":
                    Dispatcher.Invoke(OpenMakefileDialog);
                    break;
                case "run":
                    RunTarget(root.GetProperty("target").GetString() ?? "");
                    break;
                case "kill":
                    KillSession(root.GetProperty("sessionId").GetString() ?? "");
                    break;
                case "input":
                    WriteInput(root.GetProperty("sessionId").GetString() ?? "",
                               root.GetProperty("data").GetString() ?? "");
                    break;
                case "assocStatus":
                    Post(new { type = "assoc", registered = FileAssociation.IsRegistered() });
                    break;
                case "registerAssoc":
                    try
                    {
                        FileAssociation.Register();
                        Post(new { type = "assoc", registered = true });
                    }
                    catch (Exception ex) { Post(new { type = "error", message = $"Không đăng ký được: {ex.Message}" }); }
                    break;
                case "unregisterAssoc":
                    try
                    {
                        FileAssociation.Unregister();
                        Post(new { type = "assoc", registered = false });
                    }
                    catch (Exception ex) { Post(new { type = "error", message = $"Không gỡ được: {ex.Message}" }); }
                    break;
            }
        }
        catch { /* ignore bad message */ }
    }

    private void AutoLoadFromArgs()
    {
        // 1. File từ CLI / Open-With / kéo file thả vào exe (App.xaml đã parse sẵn)
        if (!string.IsNullOrEmpty(PendingMakefile) && File.Exists(PendingMakefile))
        {
            LoadMakefile(PendingMakefile);
            return;
        }
        // 2. Fallback: quét arg thô (phòng khi window dựng bằng đường khác)
        var args = Environment.GetCommandLineArgs();
        foreach (var a in args.Skip(1))
        {
            var p = CliOptions.ResolveMakefilePath(a);
            if (p is not null) { LoadMakefile(p); return; }
        }
        // 3. Makefile trong folder hiện tại
        var local = Path.Combine(Environment.CurrentDirectory, "Makefile");
        if (File.Exists(local)) LoadMakefile(local);
    }

    /// <summary>Instance 2 (double-click file khi app đang mở) chuyển file sang qua named pipe.</summary>
    public void OpenExternalFile(string? path, string? target)
    {
        var resolved = CliOptions.ResolveMakefilePath(path);
        if (resolved is null)
        {
            Post(new { type = "error", message = "Không mở được file (không phải Makefile hợp lệ)." });
            Activate();
            return;
        }
        PendingTarget = string.IsNullOrWhiteSpace(target) ? null : target;
        LoadMakefile(resolved);
        Activate();
    }

    // ---------- Makefile ----------
    private void OnDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        var files = (string[])e.Data.GetData(DataFormats.FileDrop);
        if (files.Length > 0 && File.Exists(files[0]))
            LoadMakefile(files[0]);
    }

    private void OpenMakefileDialog()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Mở Makefile (file tên Makefile, không đuôi)",
            Filter = "Makefile|Makefile*;Makefile|All files|*.*",
            FileName = "Makefile",
        };
        if (dlg.ShowDialog(this) == true)
            LoadMakefile(dlg.FileName);
    }

    public void LoadMakefile(string path)
    {
        try
        {
            _currentMakefile = Path.GetFullPath(path);
            var targets = MakefileParser.Parse(_currentMakefile);
            _lastTargets = targets;
            Post(new
            {
                type = "targets",
                file = _currentMakefile,
                targets = targets.Select(t => new
                {
                    name = t.Name,
                    deps = t.Dependencies,
                    commands = t.Commands,
                    line = t.Line,
                    desc = t.Description,
                    phony = t.IsPhony,
                }),
            });
            if (targets.Count == 0)
            {
                Post(new { type = "error", message = "Không tìm thấy target nào trong file này. Kiểm tra cú pháp 'ten_target: deps' + recipe thụt tab." });
            }
            // CLI --target: tự chạy ngay sau khi mở file
            if (!string.IsNullOrWhiteSpace(PendingTarget))
            {
                var auto = PendingTarget;
                PendingTarget = null;
                RunTarget(auto);
            }
        }
        catch (Exception ex)
        {
            Post(new { type = "error", message = $"Không đọc được Makefile: {ex.Message}" });
        }
    }

    // ---------- Process ----------
    private void RunTarget(string target)
    {
        if (string.IsNullOrWhiteSpace(_currentMakefile))
        {
            Post(new { type = "error", message = "Chưa mở Makefile nào." });
            return;
        }
        if (string.IsNullOrWhiteSpace(target)) return;

        var recipe = _lastTargets.FirstOrDefault(t => t.Name == target)?.Commands ?? Array.Empty<string>();
        var session = new MakeSession(_currentMakefile, target, recipe);
        session.OnOutput += (s, data) =>
            Dispatcher.InvokeAsync(() => Post(new { type = "output", sessionId = s.SessionId, data }));
        session.OnExit += (s, code) =>
            Dispatcher.InvokeAsync(() => Post(new { type = "exit", sessionId = s.SessionId, code }));
        _sessions[session.SessionId] = session;

        try
        {
            session.Start();
            Post(new { type = "session-started", sessionId = session.SessionId, target });
        }
        catch (Exception ex)
        {
            _sessions.Remove(session.SessionId);
            session.Dispose();
            Post(new { type = "error", message = $"Không chạy được: {ex.Message}" });
        }
    }

    private void KillSession(string sessionId)
    {
        if (_sessions.TryGetValue(sessionId, out var s))
        {
            s.Kill();
        }
    }

    private void WriteInput(string sessionId, string data)
    {
        if (_sessions.TryGetValue(sessionId, out var s))
            s.WriteInput(data);
    }

    private void Post(object payload) =>
        Dispatcher.InvokeAsync(() =>
        {
            try { Web.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(payload)); }
            catch { /* web chưa sẵn sàng */ }
        });
}
