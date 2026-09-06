using System.Windows.Input;
using QuickReplyKit.Core.Model;
using QuickReplyKit.Core.Search;

namespace QuickReplyKit.ViewModels;

/// <summary>One run of a response name, flagged when it is part of a search match (for highlighting).</summary>
public readonly record struct NameSegment(string Text, bool Hit);

/// <summary>One row in the results list, wrapping a <see cref="ResponseItem"/>.</summary>
public sealed class ResponseRowVm : ViewModelBase
{
    public ResponseItem Item { get; }

    private readonly string? _snippet;

    public ResponseRowVm(ResponseItem item, Action<ResponseRowVm> toggleFavorite,
        IReadOnlyList<MatchSpan>? nameSpans = null, string? snippet = null)
    {
        Item = item;
        _snippet = string.IsNullOrEmpty(snippet) ? null : snippet;
        NameSegments = BuildSegments(item.Name, nameSpans);
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

    /// <summary>The name split into highlighted / plain runs for the results template.</summary>
    public IReadOnlyList<NameSegment> NameSegments { get; }

    /// <summary>Filled star when favorited, hollow otherwise.</summary>
    public string FavoriteGlyph => Item.Favorite ? "★" : "☆";

    /// <summary>True for invalid files, which show a warning glyph and cannot be pasted.</summary>
    public bool ShowStatusGlyph => !Item.IsValid;

    /// <summary>
    /// Context line: the matching body snippet when the hit was body-only, otherwise where the
    /// response lives (invalid marker, App and/or category path).
    /// </summary>
    public string Subtitle
    {
        get
        {
            if (_snippet != null) return _snippet;
            var parts = new List<string>();
            if (!Item.IsValid) parts.Add("Invalid file — can't be read");
            if (!string.IsNullOrEmpty(Item.App)) parts.Add(Item.App);
            if (!string.IsNullOrEmpty(Item.CategoryPath)) parts.Add(Item.CategoryPath);
            return string.Join("  ·  ", parts);
        }
    }

    public ICommand ToggleFavoriteCommand { get; }

    private static IReadOnlyList<NameSegment> BuildSegments(string name, IReadOnlyList<MatchSpan>? spans)
    {
        if (spans == null || spans.Count == 0) return new[] { new NameSegment(name, false) };

        var segments = new List<NameSegment>();
        int pos = 0;
        foreach (var s in spans)
        {
            int start = Math.Clamp(s.Start, 0, name.Length);
            int end = Math.Clamp(s.Start + s.Length, start, name.Length);
            if (start > pos) segments.Add(new NameSegment(name[pos..start], false));
            if (end > start) segments.Add(new NameSegment(name[start..end], true));
            pos = end;
        }
        if (pos < name.Length) segments.Add(new NameSegment(name[pos..], false));
        return segments;
    }
}
