using SavienPlayer.Core;

namespace SavienPlayer.Infrastructure;

public sealed class StubMediaPlayerService : IMediaPlayerService
{
    public event EventHandler<double>? PositionChanged;
    public event EventHandler<TimeSpan>? TimeChanged;
    public event EventHandler<PlaybackState>? PlaybackStateChanged;
    public event EventHandler<PlaybackError>? PlaybackFailed;

    public bool IsLoaded { get; private set; }
    public PlaybackState State { get; private set; } = PlaybackState.None;
    public PlaybackError? LastError => null;
    public TimeSpan CurrentTime { get; private set; }
    public string? CurrentMediaPath { get; private set; }
    public bool IsMuted { get; private set; }
    public PlaybackResumePoint? GetResumePoint() =>
        IsLoaded && CurrentMediaPath != null ? new(CurrentMediaPath, CurrentTime) : null;

    public void Open(string mediaPath, TimeSpan? startPosition = null)
    {
        CurrentTime = startPosition ?? TimeSpan.Zero;
        CurrentMediaPath = mediaPath;
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
        CurrentTime = TimeSpan.Zero;
        SetState(PlaybackState.Stopped);
    }

    public void TogglePlayback()
    {
        if (State == PlaybackState.Playing) Pause();
        else Play();
    }

    public void SeekRelative(TimeSpan offset)
    {
        if (!IsLoaded || State is not (PlaybackState.Playing or PlaybackState.Paused)) return;
        var ticks = Math.Clamp((decimal)CurrentTime.Ticks + offset.Ticks, 0, TimeSpan.MaxValue.Ticks);
        CurrentTime = TimeSpan.FromTicks((long)ticks);
        TimeChanged?.Invoke(this, CurrentTime);
    }

    public void SetMuted(bool muted) => IsMuted = muted;

    public void Seek(double positionPercent)
    {
        PositionChanged?.Invoke(this, positionPercent);
    }

    public void SetVolume(int volume)
    {
        _ = volume;
    }

    public void SetPlaybackRate(double rate) => _ = rate;

    private void SetState(PlaybackState state)
    {
        State = state;
        PlaybackStateChanged?.Invoke(this, state);
    }
}
