namespace SavienPlayer.Core;

public interface IMediaPlayerService
{
    event EventHandler<double>? PositionChanged;
    event EventHandler<TimeSpan>? TimeChanged;
    event EventHandler<PlaybackState>? PlaybackStateChanged;
    event EventHandler<PlaybackError>? PlaybackFailed;

    bool IsLoaded { get; }
    PlaybackState State { get; }
    PlaybackError? LastError { get; }
    PlaybackResumePoint? GetResumePoint();

    void Open(string mediaPath, TimeSpan? startPosition = null);
    void Play();
    void Pause();
    void Stop();
    void Seek(double positionPercent);
    void SetVolume(int volume);
    void SetPlaybackRate(double rate);
}
