using System.Windows.Input;
using QuickReplyKit.Core.Model;

namespace QuickReplyKit.ViewModels;

/// <summary>One row in the results list, wrapping a <see cref="ResponseItem"/>.</summary>
public sealed class ResponseRowVm : ViewModelBase
{
    public ResponseItem Item { get; }

    public ResponseRowVm(ResponseItem item, Action<ResponseRowVm> toggleFavorite)
    {
        Item = item;
        ToggleFavoriteCommand = new RelayCommand(_ =>
        {
            if (!Item.IsValid) return;
            toggleFavorite(this);
            OnPropertyChanged(nameof(IsFavorite));
            OnPropertyChanged(nameof(FavoriteGlyph));
        });
    }

    public string Name => Item.Name;
    public bool IsValid => Item.IsValid;
    public bool IsFavorite => Item.Favorite;

    /// <summary>Filled star when favorited, hollow otherwise.</summary>
    public string FavoriteGlyph => Item.Favorite ? "★" : "☆";

    /// <summary>True for invalid files, which show a warning glyph and cannot be pasted.</summary>
    public bool ShowStatusGlyph => !Item.IsValid;

    /// <summary>Where the response lives: App and/or category path, for context.</summary>
    public string Subtitle
    {
        get
        {
            var parts = new List<string>();
            if (!Item.IsValid) parts.Add("Invalid file — can't be read");
            if (!string.IsNullOrEmpty(Item.App)) parts.Add(Item.App);
            if (!string.IsNullOrEmpty(Item.CategoryPath)) parts.Add(Item.CategoryPath);
            return string.Join("  ·  ", parts);
        }
    }

    public ICommand ToggleFavoriteCommand { get; }
}
