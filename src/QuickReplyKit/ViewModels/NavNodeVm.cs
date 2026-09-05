using System.Collections.ObjectModel;

namespace QuickReplyKit.ViewModels;

public enum NavScope { Favorites, Recent, All, Category }

/// <summary>A node in the nav tree: the fixed Favorites/Recent/All items plus the category folders.</summary>
public sealed class NavNodeVm : ViewModelBase
{
    public string Title { get; }
    public string Icon { get; }
    public NavScope Scope { get; }

    /// <summary>Library-relative folder path for <see cref="NavScope.Category"/> nodes; "" otherwise.</summary>
    public string CategoryPath { get; }

    public ObservableCollection<NavNodeVm> Children { get; } = new();

    private bool _isExpanded;
    public bool IsExpanded { get => _isExpanded; set => Set(ref _isExpanded, value); }

    private bool _isSelected;
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }

    public NavNodeVm(string title, string icon, NavScope scope, string categoryPath = "")
    {
        Title = title;
        Icon = icon;
        Scope = scope;
        CategoryPath = categoryPath;
    }
}
