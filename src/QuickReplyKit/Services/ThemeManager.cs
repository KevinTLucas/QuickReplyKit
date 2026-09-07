using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;
using QuickReplyKit.Core.Storage;
using Brushes = System.Windows.Media.Brushes;
using SystemColors = System.Windows.SystemColors;

namespace QuickReplyKit.Services;

/// <summary>
/// Applies a light/dark <see cref="ResourceDictionary"/> to the app's merged resources and swaps it
/// live. Views bind colours with <c>DynamicResource</c>, so a swap re-themes every open window's
/// content; the window title bar (non-client area) is themed separately via DWM. When the preference
/// is <see cref="AppTheme.System"/> it follows the Windows apps-theme flag and reacts to OS theme
/// changes; Light/Dark force the choice.
/// </summary>
public static class ThemeManager
{
    private const int DwmwaUseImmersiveDarkMode = 20;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    private static AppTheme _preference = AppTheme.System;
    private static AppTheme _effective = AppTheme.Light;
    private static ResourceDictionary? _current;
    private static bool _hooked;

    /// <summary>Set the preference and apply the resulting effective theme immediately.</summary>
    public static void Apply(AppTheme preference)
    {
        _preference = preference;
        HookEvents();
        Swap(ThemeResolver.Effective(preference, SystemUsesLight()));
    }

    private static void Swap(AppTheme effective)
    {
        var app = Application.Current;
        if (app is null) return;
        _effective = effective;

        var dict = new ResourceDictionary
        {
            Source = new Uri($"/Themes/{effective}.xaml", UriKind.Relative),
        };
        if (SystemParameters.HighContrast) ApplyHighContrast(dict);

        if (_current is not null) app.Resources.MergedDictionaries.Remove(_current);
        app.Resources.MergedDictionaries.Add(dict);
        _current = dict;

        // Re-theme the title bar of every already-open window (covers live preview).
        foreach (Window w in app.Windows) ThemeTitleBar(w);
    }

    /// <summary>Dark/light the window's title bar (non-client frame) to match the current theme.</summary>
    private static void ThemeTitleBar(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;   // not yet sourced; the Loaded handler will catch it
        int dark = !SystemParameters.HighContrast && _effective == AppTheme.Dark ? 1 : 0;
        try { DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref dark, sizeof(int)); }
        catch { /* older Windows without the attribute: leave the default frame */ }
    }

    /// <summary>Reads HKCU AppsUseLightTheme (1 = light). Missing/unreadable → light.</summary>
    private static bool SystemUsesLight()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is not int v || v != 0;
        }
        catch { return true; }
    }

    private static void HookEvents()
    {
        if (_hooked) return;
        _hooked = true;

        // Theme each window's title bar once its HWND exists (fires for every current/future window).
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
            new RoutedEventHandler((s, _) => ThemeTitleBar((Window)s)));

        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            bool accessibilityChange = e.Category is UserPreferenceCategory.Accessibility or UserPreferenceCategory.Color;
            if (!accessibilityChange && (e.Category != UserPreferenceCategory.General || _preference != AppTheme.System))
                return;
            // SystemEvents fires off the UI thread; marshal the resource swap.
            Application.Current?.Dispatcher.BeginInvoke(
                new Action(() => Swap(ThemeResolver.Effective(_preference, SystemUsesLight()))));
        };
    }

    private static void ApplyHighContrast(ResourceDictionary dict)
    {
        dict["WindowBg"] = SystemColors.WindowBrush;
        dict["CardBg"] = SystemColors.ControlBrush;
        dict["CardBorder"] = SystemColors.ControlTextBrush;
        dict["Accent"] = SystemColors.HighlightBrush;
        dict["PrimaryButtonBg"] = SystemColors.HighlightBrush;
        dict["OnAccent"] = SystemColors.HighlightTextBrush;
        dict["TextPrimary"] = SystemColors.WindowTextBrush;
        dict["TextMuted"] = SystemColors.GrayTextBrush;
        dict["Selection"] = SystemColors.HighlightBrush;
        dict["Disabled"] = SystemColors.GrayTextBrush;
        dict["Warning"] = SystemColors.WindowTextBrush;
        dict["Error"] = SystemColors.WindowTextBrush;
        dict["Danger"] = SystemColors.HighlightBrush;
        dict["Favorite"] = SystemColors.WindowTextBrush;
        dict["Hyperlink"] = SystemColors.HotTrackBrush;
        dict["ButtonHoverOverlay"] = Brushes.Transparent;
        dict["ButtonPressedOverlay"] = Brushes.Transparent;
        dict["ButtonOutlineOuter"] = SystemColors.WindowTextBrush;
        dict["ButtonOutlineInner"] = SystemColors.WindowBrush;
        dict[SystemColors.HighlightBrushKey] = SystemColors.HighlightBrush;
        dict[SystemColors.HighlightTextBrushKey] = SystemColors.HighlightTextBrush;
        dict[SystemColors.InactiveSelectionHighlightBrushKey] = SystemColors.HighlightBrush;
        dict[SystemColors.InactiveSelectionHighlightTextBrushKey] = SystemColors.HighlightTextBrush;
        dict[SystemColors.WindowBrushKey] = SystemColors.WindowBrush;
        dict[SystemColors.WindowTextBrushKey] = SystemColors.WindowTextBrush;
        dict[SystemColors.ControlTextBrushKey] = SystemColors.ControlTextBrush;
        dict[SystemColors.GrayTextBrushKey] = SystemColors.GrayTextBrush;
    }

}
