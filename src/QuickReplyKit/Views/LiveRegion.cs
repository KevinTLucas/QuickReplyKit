using System.Windows;
using System.Windows.Automation.Peers;

namespace QuickReplyKit.Views;

public static class LiveRegion
{
    public static readonly DependencyProperty ContentProperty = DependencyProperty.RegisterAttached(
        "Content", typeof(object), typeof(LiveRegion), new PropertyMetadata(null, ContentChanged));

    public static object? GetContent(DependencyObject element) => element.GetValue(ContentProperty);
    public static void SetContent(DependencyObject element, object? value) => element.SetValue(ContentProperty, value);

    private static void ContentChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not FrameworkElement element || !element.IsLoaded ||
            !AutomationPeer.ListenerExists(AutomationEvents.LiveRegionChanged)) return;
        (UIElementAutomationPeer.FromElement(element) ?? UIElementAutomationPeer.CreatePeerForElement(element))
            ?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }
}
