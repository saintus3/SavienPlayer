using System.Windows;
using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using LibVLCSharp.Shared;
using Microsoft.Win32;
using SavienPlayer.Core;

namespace SavienPlayer.App;

public partial class MainWindow : Window
{
    private readonly PlayerViewModel _viewModel;
    private readonly DispatcherTimer _positionTimer;
    private WindowDisplayController? _displayController;
    private Point _dragStart;
    private PlaylistItem? _dragItem;

    public MainWindow(PlayerViewModel viewModel, MediaPlayer nativePlayer)
    {
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
        _viewModel.PropertyChanged += ViewModel_OnPropertyChanged;
        VideoView.MediaPlayer = nativePlayer;
        _positionTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _positionTimer.Tick += PositionTimer_OnTick;
        _positionTimer.Start();
    }

    private void PositionTimer_OnTick(object? sender, EventArgs e) => _viewModel.DrainPosition();
    private void ViewModel_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlayerViewModel.CurrentItem) && _viewModel.CurrentItem is { } item)
            PlaylistList.ScrollIntoView(item);
    }
    private void OpenButton_OnClick(object sender, RoutedEventArgs e) => AddFiles(true);
    private void AddFilesButton_OnClick(object sender, RoutedEventArgs e) => AddFiles(false);

    private void AddFiles(bool play)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Video Files|*.mp4;*.mkv;*.avi;*.mov;*.wmv;*.flv;*.ts;*.m4v|All Files|*.*",
            Multiselect = true
        };
        if (dialog.ShowDialog() == true) _viewModel.AddFiles(dialog.FileNames, play);
    }

    private void RemoveFilesButton_OnClick(object sender, RoutedEventArgs e) =>
        _viewModel.Remove(PlaylistList.SelectedItems.Cast<PlaylistItem>().ToArray());

    private void PlaylistList_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source &&
            ItemsControl.ContainerFromElement(PlaylistList, source) is ListBoxItem { DataContext: PlaylistItem item })
            _viewModel.Open(item);
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
        e.Effects = e.Data.GetData(typeof(PlaylistItem)) is PlaylistItem item && _viewModel.Playlist.Items.Contains(item)
            ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void PlaylistList_OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(PlaylistItem)) is not PlaylistItem item || !_viewModel.Playlist.Items.Contains(item)) return;
        var target = e.OriginalSource is DependencyObject source
            ? ItemsControl.ContainerFromElement(PlaylistList, source) as ListBoxItem : null;
        var index = target?.DataContext is PlaylistItem targetItem
            ? _viewModel.Playlist.Items.IndexOf(targetItem) : _viewModel.Playlist.Items.Count - 1;
        _viewModel.Move(item, index);
        e.Handled = true;
    }

    private void SeekBar_OnPreviewMouseUp(object sender, MouseButtonEventArgs e) => _viewModel.Seek(SeekBar.Value);
    private void FullscreenButton_OnClick(object sender, RoutedEventArgs e) => _displayController?.ToggleFullscreen();

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _displayController = new WindowDisplayController(this);
    }

    private void Window_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (PlaybackControls is null || PlaylistColumn is null) return;
        PlaybackControls.MaxHeight = Math.Max(100, ActualHeight * 0.55);
        PlaylistColumn.Width = new GridLength(Math.Min(280, Math.Max(0, ActualWidth - 24) * 0.4));
    }

    private void OpenSubtitleButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Subtitle Files|*.srt;*.ass;*.ssa;*.vtt|All Files|*.*" };
        if (dialog.ShowDialog() == true) _viewModel.LoadSubtitle(dialog.FileName);
    }

    protected override void OnClosed(EventArgs e)
    {
        _displayController?.Dispose();
        _positionTimer.Stop();
        _positionTimer.Tick -= PositionTimer_OnTick;
        _viewModel.PropertyChanged -= ViewModel_OnPropertyChanged;
        _viewModel.Dispose();
        VideoView.MediaPlayer = null;
        base.OnClosed(e);
    }
}
