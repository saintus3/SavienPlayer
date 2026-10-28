using SavienPlayer.Core;

namespace SavienPlayer.Infrastructure;

public sealed class StubMediaPlayerService : IMediaPlayerService
{
    public event EventHandler<double>? PositionChanged;
    public event EventHandler<TimeSpan>? TimeChanged;
    public event EventHandler<PlaybackState>? PlaybackStateChanged;

    public bool IsLoaded { get; private set; }
    public PlaybackState State { get; private set; } = PlaybackState.None;

    public void Open(string mediaPath)
    {
        IsLoaded = true;
        SetState(PlaybackState.Opening);
    }

    public void Play()
    {
        if (!IsLoaded) return;
        SetState(PlaybackState.Playing);
    }

    public void Pause()
    {
        if (!IsLoaded) return;
        SetState(PlaybackState.Paused);
    }

    public void Stop()
    {
        if (!IsLoaded) return;
        SetState(PlaybackState.Stopped);
        TimeChanged?.Invoke(this, TimeSpan.Zero);
    }

    public void Seek(double positionPercent)
    {
        PositionChanged?.Invoke(this, positionPercent);
    }

    public void SetVolume(int volume)
    {
        _ = volume;
    }

    private void SetState(PlaybackState state)
    {
        State = state;
        PlaybackStateChanged?.Invoke(this, state);
    }
}
