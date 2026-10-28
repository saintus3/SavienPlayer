using SavienPlayer.Core;

namespace SavienPlayer.Tests;

internal sealed class FakePlayer : IMediaPlayerService
{
    public event EventHandler<double>? PositionChanged;
    public event EventHandler<TimeSpan>? TimeChanged { add { } remove { } }
    public event EventHandler<PlaybackState>? PlaybackStateChanged;
    public int PositionSubscribers => PositionChanged?.GetInvocationList().Length ?? 0;
    public int StateSubscribers => PlaybackStateChanged?.GetInvocationList().Length ?? 0;
    public bool IsLoaded { get; private set; }
    public PlaybackState State { get; private set; }
    public List<string> Opened { get; } = new();
    public int Plays { get; private set; }
    public int Stops { get; private set; }
    public int Volume { get; private set; }
    public double Position { get; private set; }
    public bool FailOpen { get; set; }
    public bool EndOnStop { get; set; }

    public void Open(string mediaPath)
    {
        if (FailOpen) throw new InvalidOperationException("open failed");
        IsLoaded = true;
        Opened.Add(mediaPath);
        Emit(PlaybackState.Opening);
    }
    public void Play() { ++Plays; Emit(PlaybackState.Playing); }
    public void Pause()
    {
        if (State == PlaybackState.Playing) Emit(PlaybackState.Paused);
        else if (State == PlaybackState.Paused) Emit(PlaybackState.Playing);
    }
    public void Stop()
    {
        ++Stops;
        if (EndOnStop) Emit(PlaybackState.Ended);
        Emit(PlaybackState.Stopped);
    }
    public void Seek(double positionPercent) => Position = positionPercent;
    public void SetVolume(int volume) => Volume = volume;
    public void EmitPosition(double position) => PositionChanged?.Invoke(this, position);
    public void Emit(PlaybackState state)
    {
        State = state;
        PlaybackStateChanged?.Invoke(this, state);
    }
}

internal sealed class FakeSubtitles : ISubtitleService
{
    public bool IsLoaded { get; private set; }
    public string? CurrentSubtitlePath { get; private set; }
    public int OffsetMilliseconds { get; private set; }
    public int Clears { get; private set; }
    public string? Error { get; set; }
    public bool ThrowOnLoad { get; set; }
    public bool TryLoad(string subtitlePath, out string? errorMessage)
    {
        if (ThrowOnLoad) throw new IOException("subtitle failed");
        errorMessage = Error;
        if (Error is not null) return false;
        IsLoaded = true;
        CurrentSubtitlePath = subtitlePath;
        return true;
    }
    public void Clear()
    {
        ++Clears;
        IsLoaded = false;
        CurrentSubtitlePath = null;
        OffsetMilliseconds = 0;
    }
    public void SetOffset(int milliseconds) => OffsetMilliseconds = milliseconds;
}

internal sealed class FakeSettings : ISettingsService
{
    public PlayerSettings Saved { get; set; } = new();
    public int Saves { get; private set; }
    public Exception? LoadError { get; set; }
    public bool FailSave { get; set; }
    public PlayerSettings Load() => LoadError is null ? Saved : throw LoadError;
    public void Save(PlayerSettings settings)
    {
        if (FailSave) throw new IOException("save failed");
        Saved = settings;
        ++Saves;
    }
}

internal sealed class QueuedDispatcher : IUiDispatcher
{
    public Queue<Action> Pending { get; } = new();
    public void Post(Action action) => Pending.Enqueue(action);
    public void Drain()
    {
        while (Pending.TryDequeue(out var action)) action();
    }
}

internal sealed class FakeNotifications : IUserNotifications
{
    public List<string> Messages { get; } = new();
    public void Show(string message) => Messages.Add(message);
}

internal sealed class FakePerformance : IPerformanceLogService
{
    public sealed class Session(Action<Exception> onError) : IPerformanceLogSession
    {
        public string LogPath => "performance.jsonl";
        public int Disposals { get; private set; }
        public void Fail() => onError(new IOException("log failed"));
        public void Dispose() => ++Disposals;
    }
    public List<Session> Sessions { get; } = new();
    public bool FailStart { get; set; }
    public IPerformanceLogSession Start(Action<Exception> onError)
    {
        if (FailStart) throw new IOException("start failed");
        var session = new Session(onError);
        Sessions.Add(session);
        return session;
    }
}

internal sealed class PlayerFixture : IDisposable
{
    public FakePlayer Player { get; } = new();
    public FakeSubtitles Subtitles { get; } = new();
    public FakeSettings Settings { get; } = new();
    public QueuedDispatcher Dispatcher { get; } = new();
    public FakeNotifications Notifications { get; } = new();
    public FakePerformance Performance { get; } = new();
    private PlayerViewModel? _viewModel;
    public PlayerViewModel ViewModel => _viewModel ??= new(Player, Subtitles, Settings, Dispatcher, Notifications, Performance);
    public void AddThree(bool play = true) => ViewModel.AddFiles(new[] { "a.mp4", "b.mp4", "c.mp4" }, play);
    public void Dispose() => _viewModel?.Dispose();
}
