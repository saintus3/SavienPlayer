namespace SavienPlayer.Core;

public interface IMediaPlayerService
{
    event EventHandler<double>? PositionChanged;
    event EventHandler<TimeSpan>? TimeChanged;
    event EventHandler<PlaybackState>? PlaybackStateChanged;

    bool IsLoaded { get; }
    PlaybackState State { get; }

    void Open(string mediaPath);
    void Play();
    void Pause();
    void Stop();
    void Seek(double positionPercent);
    void SetVolume(int volume);
}
