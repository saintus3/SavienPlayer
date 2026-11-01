using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using SavienPlayer.Core;

namespace SavienPlayer.App;

public partial class PlaylistWindow : Window
{
    private static readonly string[] SupportedVideoExtensions =
    [
        ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".ts", ".m4v", ".webm"
    ];

    private readonly IMediaPlayerService _playerService;
    private readonly Action<PlaylistItem> _playItemCallback;

    public ObservableCollection<PlaylistItem> Items { get; } = [];

    public PlaylistWindow(IMediaPlayerService playerService, Action<PlaylistItem> playItemCallback)
    {
        _playerService = playerService;
        _playItemCallback = playItemCallback;

        InitializeComponent();

        CustomTitleBar.Height = Math.Max(14, SystemParameters.WindowCaptionHeight / 2d);
        PlaylistListBox.ItemsSource = Items;
    }

    public async Task AddFilesAsync(IEnumerable<string> filePaths)
    {
        foreach (var path in filePaths)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            var trimmed = path.Trim();
            if (!File.Exists(trimmed))
            {
                continue;
            }

            var item = new PlaylistItem
            {
                FilePath = trimmed,
                FileName = Path.GetFileName(trimmed),
                DurationText = "--:--"
            };

            Items.Add(item);
            var duration = await _playerService.GetMediaDurationAsync(trimmed);
            item.DurationText = PlaylistItem.FormatDuration(duration);
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (Owner is not null && Owner.IsLoaded)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnClosing(e);
    }

    private void CustomTitleBar_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source)
        {
            for (var current = source; current is not null; current = VisualTreeHelper.GetParent(current))
            {
                if (current is Button)
                {
                    return;
                }
            }
        }

        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void CloseWindowButton_OnClick(object sender, RoutedEventArgs e)
    {
        Hide();
    }

    private void PlaylistListBox_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (PlaylistListBox.SelectedItem is PlaylistItem selectedItem)
        {
            _playItemCallback(selectedItem);
        }
    }

    private async void AddItemButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Multiselect = true,
            Filter = "Video Files|*.mp4;*.mkv;*.avi;*.mov;*.wmv;*.flv;*.ts;*.m4v;*.webm|All Files|*.*"
        };

        if (dialog.ShowDialog(this) == true)
        {
            await AddFilesAsync(dialog.FileNames);
        }
    }

    private void RemoveItemButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (PlaylistListBox.SelectedItem is PlaylistItem selectedItem)
        {
            var index = Items.IndexOf(selectedItem);
            Items.Remove(selectedItem);
            if (Items.Count > 0)
            {
                PlaylistListBox.SelectedIndex = Math.Min(index, Items.Count - 1);
            }
        }
    }

    private async void LoadPlaylistButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Multiselect = true,
            Filter = "Playlist & Video Files|*.m3u;*.m3u8;*.pls;*.txt;*.mp4;*.mkv;*.avi;*.mov;*.wmv;*.flv;*.ts;*.m4v;*.webm|Playlist Files|*.m3u;*.m3u8;*.pls;*.txt|Video Files|*.mp4;*.mkv;*.avi;*.mov;*.wmv;*.flv;*.ts;*.m4v;*.webm|All Files|*.*"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        Items.Clear();
        var resolvedFiles = new List<string>();

        foreach (var fileName in dialog.FileNames)
        {
            var ext = Path.GetExtension(fileName);
            if (string.Equals(ext, ".m3u", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(ext, ".m3u8", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(ext, ".pls", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(ext, ".txt", StringComparison.OrdinalIgnoreCase))
            {
                var baseDir = Path.GetDirectoryName(fileName) ?? string.Empty;
                foreach (var rawLine in await File.ReadAllLinesAsync(fileName))
                {
                    var line = rawLine.Trim();
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#') || line.StartsWith('['))
                    {
                        continue;
                    }

                    if (line.StartsWith("File", StringComparison.OrdinalIgnoreCase) && line.Contains('='))
                    {
                        line = line[(line.IndexOf('=') + 1)..].Trim();
                    }

                    var candidatePath = Path.IsPathRooted(line) ? line : Path.Combine(baseDir, line);
                    if (File.Exists(candidatePath))
                    {
                        resolvedFiles.Add(candidatePath);
                    }
                }
            }
            else if (SupportedVideoExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase) || File.Exists(fileName))
            {
                resolvedFiles.Add(fileName);
            }
        }

        await AddFilesAsync(resolvedFiles);
    }
}
