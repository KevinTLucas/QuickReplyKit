using Microsoft.Win32;

namespace QuickReplyKit.Services;

/// <summary>
/// Toggles "launch at Windows startup" via the per-user HKCU Run key (no elevation needed).
/// The value stores the current exe path so a moved/updated build stays correct on next save.
/// </summary>
public static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "QuickReplyKit";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string;
        }
        catch { return false; }
    }

    public static void SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (key is null) return;
            if (enabled) key.SetValue(ValueName, $"\"{Environment.ProcessPath}\"");
            else key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch { /* best effort — startup toggle is non-critical */ }
    }
}
