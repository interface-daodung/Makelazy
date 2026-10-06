using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using Makelazy.App.Services;

namespace Makelazy.App;

public partial class App : Application
{
    private CancellationTokenSource? _pipeCts;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var opts = CliOptions.Parse(e.Args);

        // Verb CLI: chạy console rồi thoát, không mở GUI
        if (opts.IsCliVerb)
        {
            RunCliVerb(opts);
            Shutdown();
            return;
        }

        // Single-instance: instance 2 chuyển file sang instance 1 rồi thoát
        if (!SingleInstance.TryAcquire())
        {
            SingleInstance.SendToRunning(opts.MakefilePath, opts.AutoTarget);
            Shutdown();
            return;
        }

        var win = new MainWindow
        {
            PendingMakefile = opts.MakefilePath,
            PendingTarget = opts.AutoTarget,
        };
        MainWindow = win;
        win.Show();

        _pipeCts = new CancellationTokenSource();
        SingleInstance.StartServer(
            async (f, t) => await win.Dispatcher.InvokeAsync(() => win.OpenExternalFile(f, t)),
            _pipeCts.Token);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _pipeCts?.Cancel(); } catch { }
        base.OnExit(e);
    }

    // ---------- CLI verbs (WinExe nên phải cấp console riêng) ----------
    private static void RunCliVerb(CliOptions opts)
    {
        AllocConsole();
        Console.OutputEncoding = Encoding.UTF8;
        try
        {
            if (opts.Help) { Console.WriteLine(CliOptions.HelpText); }
            else if (opts.Version)
            {
                var v = Assembly.GetExecutingAssembly().GetName().Version;
                Console.WriteLine($"Makelazy {v}");
            }
            else if (opts.Register)
            {
                FileAssociation.Register();
                Console.WriteLine("Đã đăng ký Open-With cho file Makefile (double-click + menu thư-mục).");
            }
            else if (opts.Unregister)
            {
                FileAssociation.Unregister();
                Console.WriteLine("Đã gỡ đăng ký Open-With.");
            }
            else if (opts.ListTargets)
            {
                if (string.IsNullOrEmpty(opts.MakefilePath))
                {
                    Console.WriteLine("Không tìm thấy Makefile. Dùng: Makelazy.exe <path/Makefile> --list");
                }
                else
                {
                    var targets = MakefileParser.Parse(opts.MakefilePath);
                    Console.WriteLine($"# {opts.MakefilePath} — {targets.Count} targets");
                    foreach (var t in targets)
                    {
                        var extra = string.IsNullOrEmpty(t.Description) ? "" : $" — {t.Description}";
                        Console.WriteLine($"  {t.Name}{extra}");
                    }
                }
            }
            Console.WriteLine();
            Console.WriteLine("Nhấn phím bất kỳ để đóng...");
            Console.ReadKey(intercept: true);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Lỗi: {ex.Message}");
            Console.ReadKey(intercept: true);
        }
    }

    [DllImport("kernel32.dll")]
    private static extern bool AllocConsole();
}
