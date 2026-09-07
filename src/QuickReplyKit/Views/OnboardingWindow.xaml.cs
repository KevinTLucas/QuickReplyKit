using System.Linq;
using System.Windows;

namespace QuickReplyKit.Views;

/// <summary>
/// Very short, skippable first-run intro (three cards) shown once. Teaches the hotkey, search/insert,
/// and where the library lives. The caller persists the "seen" flag; this window just walks the cards.
/// </summary>
public partial class OnboardingWindow : Window
{
    private readonly (string Emoji, string Heading, string Body)[] _steps;
    private int _i;

    public OnboardingWindow(string hotkey)
    {
        InitializeComponent();
        _steps = new[]
        {
            ("👋", "Press " + hotkey + " anywhere",
                "From any app, press " + hotkey + " to pop QuickReplyKit, find a prewritten response, and insert it where your cursor is."),
            ("🔍", "Search, then Enter",
                "Type a few letters to search by name, folder, tag, or text. ↑/↓ to choose, Enter to paste — Shift+Enter for plain text. Your clipboard is left untouched."),
            ("📁", "Your responses, your files",
                "Responses are just files in a folder tree. Add or edit with ＋, tag them, and share by copying folders. The ⚙ Settings change the hotkey, theme, and more."),
        };
        Show(0);
    }

    private void Show(int i)
    {
        _i = i;
        var (emoji, heading, body) = _steps[i];
        Emoji.Text = emoji;
        Heading.Text = heading;
        Body.Text = body;
        Dots.Text = string.Concat(Enumerable.Range(0, _steps.Length).Select(n => n == i ? "● " : "○ ")).TrimEnd();
        BackButton.IsEnabled = i > 0;
        NextButton.Content = i == _steps.Length - 1 ? "Get started" : "Next";
    }

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        if (_i < _steps.Length - 1) Show(_i + 1);
        else Close();
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (_i > 0) Show(_i - 1);
    }

    private void Skip_Click(object sender, RoutedEventArgs e) => Close();
}
