using System.IO;
using System.Windows;
using Microsoft.Win32;
using SavienPlayer.Core;
using SavienPlayer.Infrastructure;

namespace SavienPlayer.App;

public partial class MainWindow : Window
{
    private readonly IMediaPlayerService _playerService;
    private readonly ISubtitleService _subtitleService;
    private bool _isFullscreen;
    private int _positionVersion;

    public MainWindow(IMediaPlayerService playerService)
    {
        _playerService = playerService;
        InitializeComponent();

        _playerService.PositionChanged += PlayerServiceOnPositionChanged;
        _playerService.PlaybackFailed += PlayerServiceOnPlaybackFailed;

        if (playerService is not VlcMediaPlayerService vlc)
        {
            throw new InvalidOperationException("현재 구현은 VlcMediaPlayerService를 필요로 합니다.");
        }

        _subtitleService = vlc;
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
            Interlocked.Increment(ref _positionVersion);
            _playerService.Open(dialog.FileName);
            if (_playerService.IsLoaded)
                _playerService.Play();
            SubtitleStatusText.Text = "자막: 없음";
            SubtitleOffsetBox.Text = "0";
        }
    }

    private void PlayButton_OnClick(object sender, RoutedEventArgs e) => _playerService.Play();
    private void PauseButton_OnClick(object sender, RoutedEventArgs e) => _playerService.Pause();
    private void StopButton_OnClick(object sender, RoutedEventArgs e)
    {
        Interlocked.Increment(ref _positionVersion);
        _playerService.Stop();
    }

    private void SeekBar_OnPreviewMouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        Interlocked.Increment(ref _positionVersion);
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
        var version = Volatile.Read(ref _positionVersion);
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (version == _positionVersion && !SeekBar.IsMouseCaptureWithin)
                SeekBar.Value = position;
        }));
    }

    private void PlayerServiceOnPlaybackFailed(object? sender, PlaybackError error)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!ReferenceEquals(_playerService.LastError, error) || !IsVisible) return;
            Interlocked.Increment(ref _positionVersion);
            SeekBar.Value = 0;
            SubtitleStatusText.Text = "자막: 없음";
            SubtitleOffsetBox.Text = "0";
            MessageBox.Show(this, error.Message +
                "\n\n문제를 해결한 후 '재생'을 누르면 다시 시도합니다. '열기'로 다른 파일을 선택할 수도 있습니다." +
                App.LogLocationMessage, "SavienPlayer - 재생 오류", MessageBoxButton.OK, MessageBoxImage.Warning);
        }));
    }

    protected override void OnClosed(EventArgs e)
    {
        _playerService.PositionChanged -= PlayerServiceOnPositionChanged;
        _playerService.PlaybackFailed -= PlayerServiceOnPlaybackFailed;
        VideoView.MediaPlayer = null;
        base.OnClosed(e);
    }
}
