using SavienPlayer.Core;

namespace SavienPlayer.Infrastructure;

public sealed class PerformanceLogService(string directory, Func<PlaybackPerformanceSnapshot> snapshot)
    : IPerformanceLogService
{
    public IPerformanceLogSession Start(Action<Exception> onError) =>
        new PerformanceLogSession(directory, snapshot, onError);
}
