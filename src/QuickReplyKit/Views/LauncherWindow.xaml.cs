using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using QuickReplyKit.Core.Storage;
using QuickReplyKit.ViewModels;

namespace QuickReplyKit;

public partial class LauncherWindow : Window
{
    private readonly LauncherViewModel _vm;
    private readonly AppConfig _config;

    private const double NavAutoCollapseWidth = 620;
    private double _navWidth = 180;   // remembered so collapse/expand restores the user's size
    private bool _navVisible = true;
    private bool _navUserOverride;    // once toggled by hand, stop auto-managing on resize

    public LauncherWindow(LauncherViewModel vm, AppConfig config)
    {
        _vm = vm;
        _config = config;
        InitializeComponent();
        DataContext = _vm;
        _vm.RequestHide += Hide;
    }

    /// <summary>Bring the launcher up, cleared and focused on the search box, ready to type.</summary>
    public void ShowLauncher()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
        _vm.Query = "";
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    // --- Keyboard contract --------------------------------------------------

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down:
                _vm.MoveSelection(1);
                ScrollSelectedIntoView();
                e.Handled = true;
                break;
            case Key.Up:
                _vm.MoveSelection(-1);
                ScrollSelectedIntoView();
                e.Handled = true;
                break;
            case Key.Enter:
                if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) _vm.TriggerPlainInsert();
                else _vm.TriggerDefaultInsert();
                e.Handled = true;
                break;
            case Key.C when (Keyboard.Modifiers & ModifierKeys.Control) != 0 && SearchBox.SelectionLength == 0:
                // No text selected in the box → Ctrl+C copies the selected response instead.
                if (_vm.CopyCommand.CanExecute(null)) _vm.CopyCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Hide(); e.Handled = true; return; }

        // Ctrl+L / Ctrl+F refocus the search box (nice-to-have).
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0 && (e.Key == Key.L || e.Key == Key.F))
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
            return;
        }
        base.OnKeyDown(e);
    }

    private void ScrollSelectedIntoView()
    {
        if (_vm.SelectedResult != null) ResultsList.ScrollIntoView(_vm.SelectedResult);
    }

    private void ResultsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // Only a double-click on an actual row inserts (not empty space or the scrollbar).
        if (ItemsControl.ContainerFromElement(ResultsList, e.OriginalSource as DependencyObject) is ListBoxItem)
            _vm.TriggerDefaultInsert();
    }

    private void NavTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is NavNodeVm node) _vm.SelectedNav = node;
    }

    // --- Nav collapse -------------------------------------------------------

    private void NavToggle_Click(object sender, RoutedEventArgs e)
    {
        _navUserOverride = true;
        SetNavVisible(!_navVisible);
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_navUserOverride) return;
        bool shouldShow = ActualWidth >= NavAutoCollapseWidth;
        if (shouldShow != _navVisible) SetNavVisible(shouldShow);
    }

    private void SetNavVisible(bool visible)
    {
        if (!visible && NavColumn.ActualWidth > 0) _navWidth = NavColumn.ActualWidth;
        _navVisible = visible;
        NavColumn.MinWidth = visible ? 140 : 0;
        NavColumn.Width = visible ? new GridLength(_navWidth) : new GridLength(0);
        NavBorder.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        NavSplitter.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    // --- Settings gear (stub: opens the library folder for now) -------------

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(_config.LibraryPath);
            Process.Start(new ProcessStartInfo(_config.LibraryPath) { UseShellExecute = true });
        }
        catch { /* best effort */ }
    }

    // Closing hides to tray instead of exiting; the app exits explicitly via the tray menu.
    protected override void OnClosing(CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
    }
}
