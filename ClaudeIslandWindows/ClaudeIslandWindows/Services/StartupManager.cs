using Microsoft.Win32;

namespace ClaudeIslandWindows.Services;

public static class StartupManager
{
    private const string AppName = "ClaudeIsland";
    private const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
            return key?.GetValue(AppName) != null;
        }
        catch { return false; }
    }

    public static void Enable()
    {
        try
        {
            var exePath = Environment.ProcessPath;
            if (exePath == null) return;
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
            key?.SetValue(AppName, $"\"{exePath}\"");
        }
        catch { }
    }

    public static void Disable()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
            key?.DeleteValue(AppName, false);
        }
        catch { }
    }

    /// <summary>
    /// If launch-at-login is enabled but the stored path no longer matches the
    /// running executable, rewrite it. The path goes stale whenever the build
    /// output folder changes (e.g. a TFM bump moves bin\Debug\net10.0-windows
    /// to a versioned folder), which would otherwise launch a dead/old exe at
    /// login. Skips when running under a host (dotnet.exe) to avoid persisting
    /// a path that isn't our app.
    /// </summary>
    public static void SyncIfEnabled()
    {
        try
        {
            var exePath = Environment.ProcessPath;
            if (exePath == null ||
                !exePath.EndsWith("ClaudeIslandWindows.exe", StringComparison.OrdinalIgnoreCase))
                return;

            using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
            if (key?.GetValue(AppName) is not string current) return;

            var desired = $"\"{exePath}\"";
            if (!string.Equals(current, desired, StringComparison.OrdinalIgnoreCase))
                key.SetValue(AppName, desired);
        }
        catch { }
    }
}
