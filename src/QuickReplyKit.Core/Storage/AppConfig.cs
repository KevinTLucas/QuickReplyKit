using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace QuickReplyKit.Core.Storage;

public enum PasteMode { RichText, PlainText }

/// <summary>UI theme preference. <see cref="System"/> follows the Windows apps-theme setting.</summary>
public enum AppTheme { System, Light, Dark }

/// <summary>Persisted launcher window placement.</summary>
public sealed class WindowBounds
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }

    /// <summary>Device name of the monitor the window was last on, for on-screen validation.</summary>
    public string? MonitorDeviceName { get; set; }
}

/// <summary>
/// App settings at <see cref="AppPaths.ConfigPath"/>. <see cref="Load"/> is tolerant — a
/// missing file or bad JSON falls back to defaults.
/// </summary>
public sealed class AppConfig
{
    public string HotkeyGesture { get; set; } = "Ctrl+Alt+D";
    public string LibraryPath { get; set; } = AppPaths.DefaultLibraryDir;
    public WindowBounds Window { get; set; } = new();
    public PasteMode DefaultPasteMode { get; set; } = PasteMode.RichText;

    /// <summary>Light/Dark, or System to follow Windows. Default follows the OS.</summary>
    public AppTheme Theme { get; set; } = AppTheme.System;

    /// <summary>Restore the launcher's last size/position on show (validated on-screen first).</summary>
    public bool RememberWindow { get; set; } = true;

    /// <summary>Verbose (Debug/Verbose) diagnostic logging. Off by default → Info+ only.</summary>
    public bool VerboseLogging { get; set; } = false;

    /// <summary>Days a deleted response stays in the app Trash before auto-purge (0 = keep forever).</summary>
    public int TrashRetentionDays { get; set; } = 30;

    /// <summary>Set once the first-run onboarding has been shown, so it never pops again.</summary>
    public bool OnboardingSeen { get; set; } = false;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static AppConfig Load() => Load(AppPaths.ConfigPath);

    internal static AppConfig Load(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var cfg = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(path), Options);
                if (cfg is not null)
                {
                    // Guard against explicit nulls in the file.
                    if (string.IsNullOrEmpty(cfg.HotkeyGesture)) cfg.HotkeyGesture = "Ctrl+Alt+D";
                    if (string.IsNullOrEmpty(cfg.LibraryPath)) cfg.LibraryPath = AppPaths.DefaultLibraryDir;
                    cfg.Window ??= new WindowBounds();
                    return cfg;
                }
            }
        }
        catch { /* fall through to defaults */ }
        return new AppConfig();
    }

    public void Save()
    {
        AppPaths.EnsureAppData();
        Save(AppPaths.ConfigPath);
    }

    internal void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, Options));
    }
}
