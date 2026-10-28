using System.Windows;
using LibVLCSharp.Shared;
using Microsoft.Win32;
using SavienPlayer.Core;
using SavienPlayer.Infrastructure;

namespace SavienPlayer.App;

public partial class MainWindow : Window
{
    private readonly IMediaPlayerService _playerService;
    private bool _isFullscreen;

    public MainWindow(IMediaPlayerService playerService)
    {
        InitializeComponent();

        _playerService = playerService;
        _playerService.PositionChanged += PlayerServiceOnPositionChanged;

        if (_playerService is VlcMediaPlayerService vlc)
        {
            VideoView.MediaPlayer = vlc.NativePlayer;
        }

        _playerService.SetVolume((int)VolumeBar.Value);
    }

    private void OpenButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Video Files|*.mp4;*.mkv;*.avi;*.mov;*.wmv;*.flv;*.ts;*.m4v|All Files|*.*"
        };

        if (dialog.ShowDialog() == true)
        {
            _playerService.Open(dialog.FileName);
            _playerService.Play();
        }
    }

    private void PlayButton_OnClick(object sender, RoutedEventArgs e) => _playerService.Play();
    private void PauseButton_OnClick(object sender, RoutedEventArgs e) => _playerService.Pause();
    private void StopButton_OnClick(object sender, RoutedEventArgs e) => _playerService.Stop();

    private void SeekBar_OnPreviewMouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _playerService.Seek(SeekBar.Value);
    }

    private void VolumeBar_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _playerService.SetVolume((int)e.NewValue);
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

    private void PlayerServiceOnPositionChanged(object? sender, double position)
    {
        Dispatcher.Invoke(() => SeekBar.Value = position);
    }
}
