using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Makelazy.App.Services;

/// <summary>
/// Đăng ký Open-With cho file tên "Makefile" (không đuôi).
/// File không-đuôi không gắn association theo extension được, nên dùng 2 cơ chế HKCU (không cần admin):
///  1. Khóa "." (HKCU\Software\Classes\.) trỏ về ProgId riêng -> double-click file không-đuôi mở bằng app.
///  2. Menu chuột phải trên thư-mục -> mở Makefile trong đó.
///  3. Applications\Makelazy.exe -> app hiện trong danh sách Open-With.
/// Chạy: Makelazy.exe --register / --unregister (hoặc nút 📌 trong app).
/// </summary>
public static class FileAssociation
{
    private const string ProgId = "Makelazy.Makefile";
    private const string AppKeyName = "Makelazy.exe";
    private const string BackupSubKey = @"Software\Makelazy";
    private const string BackupValue = "DotDefaultBackup";

    public static string ExePath =>
        Environment.ProcessPath
        ?? Process.GetCurrentProcess().MainModule?.FileName
        ?? string.Empty;

    public static bool IsRegistered()
    {
        try
        {
            using var dot = Registry.CurrentUser.OpenSubKey(@"Software\Classes\.");
            if ((dot?.GetValue(null) as string) != ProgId) return false;
            using var cmd = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{ProgId}\shell\open\command");
            var c = cmd?.GetValue(null) as string;
            return c is not null && c.StartsWith("\"" + ExePath + "\"", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    public static void Register()
    {
        var exe = ExePath;
        if (string.IsNullOrEmpty(exe)) throw new InvalidOperationException("Không xác định được đường dẫn exe.");

        // 1. ProgId
        using (var prog = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProgId}"))
            prog.SetValue(null, "Makelazy Makefile");
        using (var icon = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProgId}\DefaultIcon"))
            icon.SetValue(null, $"\"{exe}\",0");
        using (var cmd = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProgId}\shell\open\command"))
            cmd.SetValue(null, $"\"{exe}\" \"%1\"");

        // 2. Backup + gán khóa "." (file không đuôi)
        using (var dot = Registry.CurrentUser.CreateSubKey(@"Software\Classes\."))
        {
            var cur = dot.GetValue(null) as string;
            if (cur != ProgId)
            {
                using var bak = Registry.CurrentUser.CreateSubKey(BackupSubKey);
                if (bak.GetValue(BackupValue) is null && cur is not null)
                    bak.SetValue(BackupValue, cur);
                dot.SetValue(null, ProgId);
            }
        }

        // 3. Hiện trong danh sách Open-With
        using (var app = Registry.CurrentUser.CreateSubKey($@"Software\Classes\Applications\{AppKeyName}\shell\open\command"))
            app.SetValue(null, $"\"{exe}\" \"%1\"");
        using (var appInfo = Registry.CurrentUser.CreateSubKey($@"Software\Classes\Applications\{AppKeyName}"))
            appInfo.SetValue("FriendlyAppName", "Makelazy");

        // 4. Menu chuột phải trên thư-mục: mở Makefile trong đó
        using (var dir = Registry.CurrentUser.CreateSubKey(@"Software\Classes\Directory\shell\Makelazy"))
        {
            dir.SetValue(null, "Mở Makefile bằng Makelazy");
            dir.SetValue("Icon", $"\"{exe}\",0");
        }
        using (var dirCmd = Registry.CurrentUser.CreateSubKey(@"Software\Classes\Directory\shell\Makelazy\command"))
            dirCmd.SetValue(null, $"\"{exe}\" \"%V\"");

        RefreshShell();
    }

    public static void Unregister()
    {
        try
        {
            // Trả lại khóa "." nếu đang trỏ về app
            using (var dot = Registry.CurrentUser.CreateSubKey(@"Software\Classes\."))
            {
                if ((dot.GetValue(null) as string) == ProgId)
                {
                    using var bak = Registry.CurrentUser.OpenSubKey(BackupSubKey);
                    var old = bak?.GetValue(BackupValue) as string;
                    if (old is not null) dot.SetValue(null, old);
                    else dot.DeleteValue(null!, throwOnMissingValue: false);
                }
            }
            Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\{ProgId}", throwOnMissingSubKey: false);
            Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\Applications\{AppKeyName}", throwOnMissingSubKey: false);
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\Directory\shell\Makelazy", throwOnMissingSubKey: false);
            Registry.CurrentUser.DeleteSubKey(BackupSubKey, throwOnMissingSubKey: false);
        }
        catch { /* best effort */ }
        RefreshShell();
    }

    private static void RefreshShell()
    {
        try { SHChangeNotify(0x08000000 /* SHCNE_ASSOCCHANGED */, 0, IntPtr.Zero, IntPtr.Zero); }
        catch { /* ignore */ }
    }

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);
}
