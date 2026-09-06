namespace QuickReplyKit.Core.Storage;

/// <summary>Pure theme resolution: maps a preference plus the OS light/dark flag to an effective theme.</summary>
public static class ThemeResolver
{
    /// <summary>
    /// Resolve the effective theme (always <see cref="AppTheme.Light"/> or <see cref="AppTheme.Dark"/>).
    /// For <see cref="AppTheme.System"/> the OS flag decides; Light/Dark are honoured as-is.
    /// </summary>
    public static AppTheme Effective(AppTheme preference, bool systemUsesLight) => preference switch
    {
        AppTheme.Light => AppTheme.Light,
        AppTheme.Dark => AppTheme.Dark,
        _ => systemUsesLight ? AppTheme.Light : AppTheme.Dark,
    };
}
