using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace QuickReplyKit.Views;

internal static class WindowSizing
{
    public static void LimitHeightToCurrentScreen(Window window)
    {
        void Apply() => window.MaxHeight = Math.Max(window.MinHeight,
            System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(window).Handle).WorkingArea.Height /
            VisualTreeHelper.GetDpi(window).DpiScaleY);

        window.Loaded += (_, _) =>
        {
            Apply();
            window.LocationChanged += (_, _) => Apply();
            window.DpiChanged += (_, _) => Apply();
        };
    }
}
