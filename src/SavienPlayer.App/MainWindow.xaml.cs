using System.IO;
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
    private bool _isFullscreen;
    private WindowState _previousWindowState = WindowState.Normal;

    public MainWindow(IMediaPlayerService playerService)
    {
        if (playerService is not VlcMediaPlayerService vlc)
        {
            throw new InvalidOperationException("현재 구현은 VlcMediaPlayerService를 필요로 합니다.");
        }

        _playerService = playerService;
        _subtitleService = vlc;

        InitializeComponent();

        _playerService.PositionChanged += PlayerServiceOnPositionChanged;
        VideoView.MediaPlayer = vlc.NativePlayer;

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
            SubtitleStatusText.Text = "자막: 없음";
            SubtitleOffsetBox.Text = "0";
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
        _playerService?.SetVolume((int)e.NewValue);
    }

    private void VideoOverlay_OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is DependencyObject element)
        {
            var overlayWindow = GetWindow(element);
            if (overlayWindow is not null && !ReferenceEquals(overlayWindow, this))
            {
                overlayWindow.PreviewKeyDown -= Window_OnPreviewKeyDown;
                overlayWindow.PreviewKeyDown += Window_OnPreviewKeyDown;
            }
        }
    }

    private void Window_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !e.IsRepeat && e.OriginalSource is not TextBox)
        {
            e.Handled = true;
            ToggleFullscreen();
        }
    }

    private void FullscreenButton_OnClick(object sender, RoutedEventArgs e) => ToggleFullscreen();

    private void ToggleFullscreen()
    {
        _isFullscreen = !_isFullscreen;

        if (_isFullscreen)
        {
            _previousWindowState = WindowState;
            ControlsPanel.Visibility = Visibility.Collapsed;
            RootGrid.Margin = new Thickness(0);
            VideoBorder.CornerRadius = new CornerRadius(0);
            VideoBorder.BorderThickness = new Thickness(0);

            if (WindowState == WindowState.Maximized)
            {
                WindowState = WindowState.Normal;
            }

            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Maximized;
        }
        else
        {
            ControlsPanel.Visibility = Visibility.Visible;
            RootGrid.Margin = new Thickness(12);
            VideoBorder.CornerRadius = new CornerRadius(8);
            VideoBorder.BorderThickness = new Thickness(1);

            WindowStyle = WindowStyle.SingleBorderWindow;
            ResizeMode = ResizeMode.CanResize;
            WindowState = _previousWindowState;
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
        Dispatcher.Invoke(() => SeekBar.Value = position);
    }
}
