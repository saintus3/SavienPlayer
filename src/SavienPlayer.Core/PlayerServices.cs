namespace SavienPlayer.Core;

public sealed record PlayerSettings
{
    public string[] Files { get; init; } = Array.Empty<string>();
    public bool AllowDuplicates { get; init; }
    public int Volume { get; init; } = 70;
}

public interface ISettingsService
{
    PlayerSettings Load();
    void Save(PlayerSettings settings);
}

public interface IUiDispatcher
{
    void Post(Action action);
}

public interface IUserNotifications
{
    void Show(string message);
}

public interface IPerformanceLogSession : IDisposable
{
    string LogPath { get; }
}

public interface IPerformanceLogService
{
    IPerformanceLogSession Start(Action<Exception> onError);
}
