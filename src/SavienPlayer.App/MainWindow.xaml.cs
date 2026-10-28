using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using SavienPlayer.Core;
using SavienPlayer.Infrastructure;

namespace SavienPlayer.App;

public partial class MainWindow : Window
{
    private readonly IMediaPlayerService _playerService;
    private readonly ISubtitleService _subtitleService;
    private readonly Playlist _playlist = new();
    private bool _isFullscreen;
    private Point _dragStart;
    private PlaylistItem? _dragItem;
    private long _playbackVersion;
    private bool _playbackRequested;
    private bool _isClosing;

    public MainWindow(IMediaPlayerService playerService)
    {
        if (playerService is not VlcMediaPlayerService vlc)
        {
            throw new InvalidOperationException("현재 구현은 VlcMediaPlayerService를 필요로 합니다.");
        }

        _playerService = playerService;
        _subtitleService = vlc;

        InitializeComponent();

        PlaylistList.ItemsSource = _playlist.Items;
        try
        {
            PlaylistStore.Load(_playlist);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException)
        {
            MessageBox.Show("플레이리스트를 복원하지 못했습니다.", "SavienPlayer", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        AllowDuplicatesBox.IsChecked = _playlist.AllowDuplicates;

        _playerService.PositionChanged += PlayerServiceOnPositionChanged;
        _playerService.PlaybackStateChanged += PlayerServiceOnPlaybackStateChanged;
        VideoView.MediaPlayer = vlc.NativePlayer;

        _playerService.SetVolume((int)VolumeBar.Value);
    }

    private void OpenButton_OnClick(object sender, RoutedEventArgs e)
    {
        AddFiles(true);
    }

    private void AddFilesButton_OnClick(object sender, RoutedEventArgs e)
    {
        AddFiles(false);
    }

    private void AddFiles(bool play)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Video Files|*.mp4;*.mkv;*.avi;*.mov;*.wmv;*.flv;*.ts;*.m4v|All Files|*.*",
            Multiselect = true
        };

        if (dialog.ShowDialog() == true)
        {
            PlaylistItem? first = null;
            foreach (var file in dialog.FileNames)
            {
                var item = _playlist.Add(file);
                first ??= item;
            }
            SavePlaylist();
            if (play && first is not null) OpenPlaylistItem(first);
        }
    }

    private void OpenPlaylistItem(PlaylistItem item, bool play = true)
    {
        _playerService.Stop();
        Interlocked.Increment(ref _playbackVersion);
        _playbackRequested = play;
        _playlist.SetCurrent(item);
        _playerService.Open(item.FilePath);
        SeekBar.Value = 0;
        SubtitleStatusText.Text = "자막: 없음";
        SubtitleOffsetBox.Text = "0";
        PlaylistList.ScrollIntoView(item);
        if (play) _playerService.Play();
    }

    private void PlayButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_playlist.Current is null)
        {
            var item = PlaylistList.SelectedItem as PlaylistItem ?? _playlist.GetNext();
            if (item is not null) OpenPlaylistItem(item);
            return;
        }
        _playbackRequested = true;
        Interlocked.Increment(ref _playbackVersion);
        _playerService.Play();
    }

    private void PauseButton_OnClick(object sender, RoutedEventArgs e)
    {
        _playbackRequested = false;
        _playerService.Pause();
        Interlocked.Increment(ref _playbackVersion);
    }

    private void StopButton_OnClick(object sender, RoutedEventArgs e)
    {
        _playbackRequested = false;
        _playerService.Stop();
        Interlocked.Increment(ref _playbackVersion);
    }

    private void NextButton_OnClick(object sender, RoutedEventArgs e)
    {
        var next = _playlist.GetNext();
        if (next is not null) OpenPlaylistItem(next);
    }

    private void RemoveFilesButton_OnClick(object sender, RoutedEventArgs e)
    {
        var current = _playlist.Current;
        var wasPlaying = _playbackRequested;
        _playlist.Remove(PlaylistList.SelectedItems.Cast<PlaylistItem>().ToArray());
        if (current != _playlist.Current)
        {
            if (_playlist.Current is not null)
            {
                OpenPlaylistItem(_playlist.Current, wasPlaying);
            }
            else
            {
                _playbackRequested = false;
                _playerService.Stop();
                Interlocked.Increment(ref _playbackVersion);
                SeekBar.Value = 0;
                SubtitleStatusText.Text = "자막: 없음";
                SubtitleOffsetBox.Text = "0";
            }
        }
        SavePlaylist();
    }

    private void AllowDuplicatesBox_OnChanged(object sender, RoutedEventArgs e)
    {
        _playlist.AllowDuplicates = AllowDuplicatesBox.IsChecked == true;
        SavePlaylist();
    }

    private void PlaylistList_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source &&
            ItemsControl.ContainerFromElement(PlaylistList, source) is ListBoxItem { DataContext: PlaylistItem item })
        {
            OpenPlaylistItem(item);
        }
    }

    private void PlaylistList_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(PlaylistList);
        _dragItem = e.OriginalSource is DependencyObject source &&
            ItemsControl.ContainerFromElement(PlaylistList, source) is ListBoxItem container
            ? container.DataContext as PlaylistItem : null;
    }

    private void PlaylistList_OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragItem is null) return;
        var position = e.GetPosition(PlaylistList);
        if (Math.Abs(position.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(position.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        var item = _dragItem;
        _dragItem = null;
        DragDrop.DoDragDrop(PlaylistList, new DataObject(typeof(PlaylistItem), item), DragDropEffects.Move);
    }

    private void PlaylistList_OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetData(typeof(PlaylistItem)) is PlaylistItem item && _playlist.Items.Contains(item)
            ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void PlaylistList_OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(PlaylistItem)) is not PlaylistItem item || !_playlist.Items.Contains(item)) return;
        var target = e.OriginalSource is DependencyObject source
            ? ItemsControl.ContainerFromElement(PlaylistList, source) as ListBoxItem : null;
        var index = target?.DataContext is PlaylistItem targetItem
            ? _playlist.Items.IndexOf(targetItem) : _playlist.Items.Count - 1;
        _playlist.Move(item, index);
        SavePlaylist();
        e.Handled = true;
    }

    private void SavePlaylist()
    {
        try
        {
            PlaylistStore.Save(_playlist);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show("플레이리스트를 저장하지 못했습니다.", "SavienPlayer", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void SeekBar_OnPreviewMouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _playerService.Seek(SeekBar.Value);
    }

    private void VolumeBar_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _playerService?.SetVolume((int)e.NewValue);
    }

    private void FullscreenButton_OnClick(object sender, RoutedEventArgs e)
    {
        _isFullscreen = !_isFullscreen;

        if (_isFullscreen)
        {
            WindowStyle = WindowStyle.None;
            WindowState = WindowState.Maximized;
            ResizeMode = ResizeMode.NoResize;
        }
        else
        {
            WindowStyle = WindowStyle.SingleBorderWindow;
            WindowState = WindowState.Normal;
            ResizeMode = ResizeMode.CanResize;
        }
    }

    private void OpenSubtitleButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Subtitle Files|*.srt;*.ass;*.ssa;*.vtt|All Files|*.*"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        if (_subtitleService.TryLoad(dialog.FileName, out var error))
        {
            SubtitleStatusText.Text = $"자막: {Path.GetFileName(dialog.FileName)}";
            return;
        }

        MessageBox.Show(error ?? "자막 로드에 실패했습니다.", "SavienPlayer", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void ClearSubtitleButton_OnClick(object sender, RoutedEventArgs e)
    {
        _subtitleService.Clear();
        SubtitleStatusText.Text = "자막: 없음";
        SubtitleOffsetBox.Text = "0";
    }

    private void ApplySubtitleOffsetButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(SubtitleOffsetBox.Text, out var offset))
        {
            MessageBox.Show("자막 싱크 값은 정수(ms)로 입력해주세요.", "SavienPlayer", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _subtitleService.SetOffset(offset);
        SubtitleStatusText.Text = _subtitleService.IsLoaded
            ? $"자막: 적용됨 (싱크 {offset}ms)"
            : $"자막: 없음 (기본 싱크 {offset}ms 저장)";
    }

    private void PlayerServiceOnPositionChanged(object? sender, double position)
    {
        var version = Interlocked.Read(ref _playbackVersion);
        Dispatcher.BeginInvoke(() =>
        {
            if (!_isClosing && version == Interlocked.Read(ref _playbackVersion)) SeekBar.Value = position;
        });
    }

    private void PlayerServiceOnPlaybackStateChanged(object? sender, PlaybackState state)
    {
        if (state is not (PlaybackState.Ended or PlaybackState.Error)) return;
        var version = Interlocked.Read(ref _playbackVersion);
        // Never open/stop VLC synchronously from its native event callback.
        Dispatcher.BeginInvoke(() =>
        {
            if (_isClosing || version != Interlocked.Read(ref _playbackVersion) || _playlist.Current is null) return;
            if (state == PlaybackState.Error)
            {
                _playbackRequested = false;
                return;
            }
            if (!_playbackRequested) return;
            var next = _playlist.GetNext();
            if (next is not null) OpenPlaylistItem(next);
            else _playbackRequested = false;
        });
    }

    protected override void OnClosed(EventArgs e)
    {
        _isClosing = true;
        _playerService.PositionChanged -= PlayerServiceOnPositionChanged;
        _playerService.PlaybackStateChanged -= PlayerServiceOnPlaybackStateChanged;
        base.OnClosed(e);
    }
}
