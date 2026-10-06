using System.IO;

namespace Makelazy.App.Services;

/// <summary>Lưu theme sáng/tối vào %AppData%\Makelazy\theme.txt.</summary>
public static class ThemeStore
{
    private static string FilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Makelazy", "theme.txt");

    public static bool LoadDark()
    {
        try
        {
            if (!File.Exists(FilePath)) return false; // mặc định sáng
            return File.ReadAllText(FilePath).Trim().Equals("dark", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    public static void SaveDark(bool dark)
    {
        try
        {
            var dir = Path.GetDirectoryName(FilePath);
            if (dir is not null) Directory.CreateDirectory(dir);
            File.WriteAllText(FilePath, dark ? "dark" : "light");
        }
        catch { /* best effort */ }
    }
}
