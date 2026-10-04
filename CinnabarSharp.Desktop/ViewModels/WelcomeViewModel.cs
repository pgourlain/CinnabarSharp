using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace CinnabarSharp.Desktop.ViewModels;

/// <summary>A file of the welcome screen's "Recent" list.</summary>
public sealed class RecentFileItem(string path) : ViewModelBase
{
    private Avalonia.Media.Imaging.Bitmap? _thumbnail;
    private bool _thumbnailRequested;

    public string Path { get; } = path;

    public string Name => System.IO.Path.GetFileName(Path);

    /// <summary>The folder, shortened to its last two parts; the user's own folders are what they recognize.</summary>
    public string Folder
    {
        get
        {
            var parts = (System.IO.Path.GetDirectoryName(Path) ?? "")
                .Split([System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
            return string.Join(System.IO.Path.DirectorySeparatorChar, parts.TakeLast(2));
        }
    }

    /// <summary>"JPG", "HEIC"…: the badge on the tile until the thumbnail is read (or when it can't be).</summary>
    public string Extension => System.IO.Path.GetExtension(Path).TrimStart('.').ToUpperInvariant();

    public bool Exists => File.Exists(Path);

    /// <summary>The picture; null until it has been read in the background (the tile then updates).</summary>
    public Avalonia.Media.Imaging.Bitmap? Thumbnail
    {
        get
        {
            if (!_thumbnailRequested)
            {
                _thumbnailRequested = true;
                _ = LoadThumbnailAsync();
            }
            return _thumbnail;
        }
    }

    public bool HasThumbnail => _thumbnail is not null;

    private async Task LoadThumbnailAsync()
    {
        _thumbnail = await Services.RecentThumbnails.LoadAsync(Path);
        OnPropertyChanged(nameof(Thumbnail));
        OnPropertyChanged(nameof(HasThumbnail));
    }
}

/// <summary>One line of the welcome screen's shortcut list.</summary>
public sealed record WelcomeShortcut(string What, string Keys);

public partial class MainViewModel
{
    private static string Cmd => OperatingSystem.IsMacOS() ? "⌘" : "Ctrl";

    public static IReadOnlyList<WelcomeShortcut> WelcomeShortcuts { get; } =
    [
        new("Open", $"{Cmd} O"),
        new("New image", $"{Cmd} N"),
        new("Paste as a new image", $"{Cmd} Alt V"),
        new("Undo", $"{Cmd} Z"),
        new("Paintbrush · Eraser · Pencil", "B · E · P"),
        new("Select tools (cycle)", "S"),
        new("Crop", "C"),
        new("Text", "T"),
        new("Zoom around the mouse", $"{Cmd} + wheel"),
        new("Pan", "Space + drag"),
    ];

    public string VersionText => $"Version {typeof(MainViewModel).Assembly.GetName().Version?.ToString(3)}";

    /// <summary>The recent files that still exist, newest first, for the welcome screen.</summary>
    public IReadOnlyList<RecentFileItem> RecentItems =>
        RecentFiles.Files.Select(p => _recentItems.TryGetValue(p, out var item) ? item : _recentItems[p] = new RecentFileItem(p))
            .Where(i => i.Exists).Take(8).ToList();

    // The same item (and its thumbnail) while the file stays in the list: the welcome screen reads this on every refresh.
    private readonly Dictionary<string, RecentFileItem> _recentItems = [];

    public bool HasRecentItems => RecentItems.Count > 0;

    /// <summary>Whether the welcome screen is shown while no image is open (a setting; a short hint otherwise).</summary>
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    public partial bool ShowWelcomeScreen { get; set; } = true;

    public bool IsWelcomeVisible => !HasDocument && ShowWelcomeScreen;

    public bool IsEmptyHintVisible => !HasDocument && !ShowWelcomeScreen;

    partial void OnShowWelcomeScreenChanged(bool value)
    {
        OnPropertyChanged(nameof(IsWelcomeVisible));
        OnPropertyChanged(nameof(IsEmptyHintVisible));
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private Task OpenRecentItem(RecentFileItem item) => OpenFileAsync(item.Path);

    private void RefreshWelcome()
    {
        OnPropertyChanged(nameof(RecentItems));
        OnPropertyChanged(nameof(HasRecentItems));
        OnPropertyChanged(nameof(IsWelcomeVisible));
        OnPropertyChanged(nameof(IsEmptyHintVisible));
    }
}
