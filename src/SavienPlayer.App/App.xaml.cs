using System.Windows;
using SavienPlayer.Infrastructure;

namespace SavienPlayer.App;

public partial class App : Application
{
    private VlcMediaPlayerService? _playerService;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _playerService = new VlcMediaPlayerService();
        var window = new MainWindow(_playerService);
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _playerService?.Dispose();
        base.OnExit(e);
    }
}
