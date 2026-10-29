using System.Windows.Input;

namespace SavienPlayer.Core;

public sealed class PlayerCommand(Action<object?> execute, Predicate<object?>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => canExecute?.Invoke(parameter) ?? true;
    public void Execute(object? parameter)
    {
        if (CanExecute(parameter)) execute(parameter);
    }
    internal void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
