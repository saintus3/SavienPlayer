namespace SavienPlayer.Core;

public interface IMediaPlayerService
{
    event EventHandler<double>? PositionChanged;
    event EventHandler<TimeSpan>? TimeChanged;
    event EventHandler<PlaybackState>? PlaybackStateChanged;

    bool IsLoaded { get; }
    PlaybackState State { get; }

    void Open(string mediaPath);
    Task OpenUrlAsync(string url, CancellationToken cancellationToken = default);
    Task<TimeSpan?> GetMediaDurationAsync(string mediaPath, CancellationToken cancellationToken = default);
    void Play();
    void Pause();
    void Stop();
    void Seek(double positionPercent);
    void SeekRelative(TimeSpan offset);
    void SetVolume(int volume);
}
