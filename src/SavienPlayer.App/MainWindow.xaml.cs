using System.IO;
using System.Windows;
using System.ComponentModel;
using System.Windows.Threading;
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
    private readonly PlayerSettingsStore _settingsStore;
    private readonly PlayerSettings _settings;
    private readonly DispatcherTimer _saveTimer;
    private bool _restoringSettings = true;
    private bool _restorePending = true;
    private bool _windowedMaximized;
    private bool _closing;

    public MainWindow(IMediaPlayerService playerService, PlayerSettingsStore settingsStore, PlayerSettings settings)
    {
        _playerService = playerService;
        _settingsStore = settingsStore;
        _settings = settings;
        InitializeComponent();

        _playerService.PositionChanged += PlayerServiceOnPositionChanged;
        _playerService.PlaybackFailed += PlayerServiceOnPlaybackFailed;

        if (playerService is not VlcMediaPlayerService vlc)
        {
            throw new InvalidOperationException("현재 구현은 VlcMediaPlayerService를 필요로 합니다.");
        }

        _subtitleService = vlc;
        VideoView.MediaPlayer = vlc.NativePlayer;

        Width = Math.Min(settings.WindowWidth, Math.Max(MinWidth, SystemParameters.WorkArea.Width));
        Height = Math.Min(settings.WindowHeight, Math.Max(MinHeight, SystemParameters.WorkArea.Height));
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        if (settings.IsMaximized) WindowState = WindowState.Maximized;
        VolumeBar.Value = settings.Volume;
        PlaybackRateBar.Value = settings.PlaybackRate;
        _playerService.SetVolume(settings.Volume);
        _playerService.SetPlaybackRate(settings.PlaybackRate);
        _restoringSettings = false;
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _saveTimer.Tick += (_, _) => SaveSettings();
        _saveTimer.Start();
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        if (!_restorePending) return;
        _restorePending = false;
        if (_settings.IsFullscreen) SetFullscreen(true);
        var path = _settings.LastMediaPath;
        if (path == null) return;
        if (!File.Exists(path))
        {
            _settings.LastMediaPath = null;
            _settings.LastPositionMilliseconds = 0;
            SaveSettings();
            MessageBox.Show(this, "마지막 동영상 파일이 없거나 접근할 수 없어 이어재생하지 않았습니다. '열기'로 파일을 선택해주세요.",
                "SavienPlayer - 이어재생", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        _playerService.Open(path, TimeSpan.FromTicks(
            _settings.LastPositionMilliseconds * TimeSpan.TicksPerMillisecond));
        if (_playerService.IsLoaded) _playerService.Play();
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
            {
                _settings.LastMediaPath = dialog.FileName;
                _settings.LastPositionMilliseconds = 0;
                _playerService.Play();
            }
            SubtitleStatusText.Text = "자막: 없음";
            SubtitleOffsetBox.Text = "0";
            SaveSettings();
        }
    }

    private void PlayButton_OnClick(object sender, RoutedEventArgs e) => _playerService.Play();
    private void PauseButton_OnClick(object sender, RoutedEventArgs e) => _playerService.Pause();
    private void StopButton_OnClick(object sender, RoutedEventArgs e)
    {
        Interlocked.Increment(ref _positionVersion);
        _playerService.Stop();
        SaveSettings();
    }

    private void SeekBar_OnPreviewMouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        Interlocked.Increment(ref _positionVersion);
        _playerService.Seek(SeekBar.Value);
        SaveSettings();
    }

    private void VolumeBar_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _playerService.SetVolume((int)e.NewValue);
        if (!_restoringSettings) SaveSettings();
    }

    private void PlaybackRateBar_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _playerService.SetPlaybackRate(e.NewValue);
        if (!_restoringSettings) SaveSettings();
    }

    private void FullscreenButton_OnClick(object sender, RoutedEventArgs e)
    {
        SetFullscreen(!_isFullscreen);
        SaveSettings();
    }

    private void SetFullscreen(bool fullscreen)
    {
        if (_isFullscreen == fullscreen) return;
        if (fullscreen)
        {
            CaptureWindowSettings();
            _windowedMaximized = WindowState == WindowState.Maximized;
        }
        _isFullscreen = fullscreen;

        if (_isFullscreen)
        {
            WindowStyle = WindowStyle.None;
            WindowState = WindowState.Maximized;
            ResizeMode = ResizeMode.NoResize;
        }
        else
        {
            WindowStyle = WindowStyle.SingleBorderWindow;
            WindowState = _windowedMaximized ? WindowState.Maximized : WindowState.Normal;
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
        _closing = true;
        _saveTimer.Stop();
        _playerService.PositionChanged -= PlayerServiceOnPositionChanged;
        _playerService.PlaybackFailed -= PlayerServiceOnPlaybackFailed;
        VideoView.MediaPlayer = null;
        base.OnClosed(e);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (!e.Cancel) SaveSettings();
    }

    private void CaptureWindowSettings()
    {
        if (_isFullscreen) return;
        var bounds = WindowState == WindowState.Normal
            ? new Rect(Left, Top, ActualWidth, ActualHeight) : RestoreBounds;
        if (bounds.Width > 0 && bounds.Height > 0)
        {
            _settings.WindowWidth = bounds.Width;
            _settings.WindowHeight = bounds.Height;
        }
        if (WindowState != WindowState.Minimized)
            _settings.IsMaximized = WindowState == WindowState.Maximized;
    }

    private void SaveSettings()
    {
        if (_restoringSettings || _restorePending || _closing) return;
        _settings.Volume = (int)VolumeBar.Value;
        _settings.PlaybackRate = PlaybackRateBar.Value;
        _settings.IsFullscreen = _isFullscreen;
        CaptureWindowSettings();
        if (_playerService.GetResumePoint() is { } resumePoint)
        {
            _settings.LastMediaPath = resumePoint.MediaPath;
            _settings.LastPositionMilliseconds = resumePoint.Position.Ticks / TimeSpan.TicksPerMillisecond;
        }
        _settingsStore.Save(_settings);
    }
}
