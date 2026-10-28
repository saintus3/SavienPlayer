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

    void Open(string mediaPath);
    void Play();
    void Pause();
    void Stop();
    void Seek(double positionPercent);
    void SetVolume(int volume);
}
