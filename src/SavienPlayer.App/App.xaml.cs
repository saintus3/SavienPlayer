using System.Windows;
using System.Diagnostics;
using System.IO;
using SavienPlayer.Infrastructure;
using SavienPlayer.Core;

namespace SavienPlayer.App;

public partial class App : Application
{
    private VlcMediaPlayerService? _playerService;
    private PlaybackLogListener? _logListener;
    internal static string? DiagnosticLogPath { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SavienPlayer", "Logs", $"playback-{DateTime.UtcNow:yyyyMMdd}-{Environment.ProcessId}.log");
            _logListener = new PlaybackLogListener(path);
            Trace.Listeners.Add(_logListener);
            DiagnosticLogPath = path;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            Trace.TraceWarning($"Cannot create playback log: {exception}");
        }

        try
        {
            _playerService = new VlcMediaPlayerService();
            var settingsStore = new PlayerSettingsStore(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SavienPlayer", "settings.json"));
            PlayerSettings settings = settingsStore.Load();
            var window = new MainWindow(_playerService, settingsStore, settings);
            window.Show();
        }
        catch (Exception exception)
        {
            Trace.TraceError($"Player startup failure: HRESULT={exception.HResult:X8}, {exception}");
            MessageBox.Show("플레이어를 시작하지 못했습니다. 64비트 VLC 런타임 설치 여부를 확인한 뒤 다시 실행해주세요." +
                LogLocationMessage, "SavienPlayer", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    internal static string LogLocationMessage => DiagnosticLogPath is { } path &&
        ((App)Current)._logListener?.IsAvailable == true
        ? $"\n\n진단 로그: {path}"
        : "\n\n진단 로그를 저장할 수 없습니다. 사용자 폴더의 쓰기 권한과 디스크 여유 공간을 확인해주세요.";

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _playerService?.Dispose();
        }
        catch (Exception exception)
        {
            Trace.TraceError($"Player shutdown failure: {exception}");
        }
        if (_logListener != null)
        {
            Trace.Listeners.Remove(_logListener);
            _logListener.Dispose();
        }
        base.OnExit(e);
    }
}
