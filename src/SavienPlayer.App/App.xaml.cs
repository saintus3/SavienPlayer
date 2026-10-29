using System.Windows;
using System.IO;
using SavienPlayer.Core;
using SavienPlayer.Infrastructure;

namespace SavienPlayer.App;

public partial class App : Application
{
    private VlcMediaPlayerService? _playerService;
    private PlayerViewModel? _viewModel;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _playerService = new VlcMediaPlayerService();
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SavienPlayer");
        _viewModel = new PlayerViewModel(_playerService,
            new VlcSubtitleService(_playerService.NativePlayer, _playerService),
            new JsonSettingsService(Path.Combine(directory, "playlist.json")),
            new UiDispatcher(Dispatcher), new UserNotifications(),
            new PerformanceLogService(Path.Combine(directory, "Performance"), _playerService.GetPerformanceSnapshot));
        var window = new MainWindow(_viewModel, _playerService.NativePlayer);
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _viewModel?.Dispose();
        _playerService?.Dispose();
        base.OnExit(e);
    }
}
