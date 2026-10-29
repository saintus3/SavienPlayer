using System.Windows;
using System.Windows.Threading;
using SavienPlayer.Core;

namespace SavienPlayer.App;

internal sealed class UiDispatcher(Dispatcher dispatcher) : IUiDispatcher
{
    public void Post(Action action) => dispatcher.BeginInvoke(action);
}

internal sealed class UserNotifications : IUserNotifications
{
    public void Show(string message) =>
        MessageBox.Show(message, "SavienPlayer", MessageBoxButton.OK, MessageBoxImage.Warning);
}
