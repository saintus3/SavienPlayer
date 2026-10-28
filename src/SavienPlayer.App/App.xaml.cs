using System.Windows;
using SavienPlayer.Infrastructure;

namespace SavienPlayer.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var playerService = new VlcMediaPlayerService();
        var window = new MainWindow(playerService);
        window.Show();
    }
}
