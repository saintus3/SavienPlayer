using SavienPlayer.Core;
using LibVLCSharp.Shared;

namespace SavienPlayer.Infrastructure;

public sealed class VlcMediaPlayerService : IMediaPlayerService, IDisposable
{
    private readonly LibVLC _libVlc;
    private readonly MediaPlayer _mediaPlayer;
    private Media? _currentMedia;

    public event EventHandler<double>? PositionChanged;
    public event EventHandler<TimeSpan>? TimeChanged;
    public event EventHandler<PlaybackState>? PlaybackStateChanged;

    public bool IsLoaded { get; private set; }
    public PlaybackState State { get; private set; } = PlaybackState.None;

    public MediaPlayer NativePlayer => _mediaPlayer;

    public VlcMediaPlayerService()
    {
        Core.Initialize();
        _libVlc = new LibVLC();
        _mediaPlayer = new MediaPlayer(_libVlc);

        _mediaPlayer.TimeChanged += (_, e) => TimeChanged?.Invoke(this, TimeSpan.FromMilliseconds(e.Time));
        _mediaPlayer.PositionChanged += (_, e) => PositionChanged?.Invoke(this, e.Position * 100d);

        _mediaPlayer.Playing += (_, _) => SetState(PlaybackState.Playing);
        _mediaPlayer.Paused += (_, _) => SetState(PlaybackState.Paused);
        _mediaPlayer.Stopped += (_, _) => SetState(PlaybackState.Stopped);
        _mediaPlayer.Buffering += (_, _) => SetState(PlaybackState.Buffering);
        _mediaPlayer.EndReached += (_, _) => SetState(PlaybackState.Ended);
        _mediaPlayer.EncounteredError += (_, _) => SetState(PlaybackState.Error);
    }

    public void Open(string mediaPath)
    {
        _currentMedia?.Dispose();
        _currentMedia = new Media(_libVlc, new Uri(mediaPath));
        _mediaPlayer.Media = _currentMedia;
        IsLoaded = true;
        SetState(PlaybackState.Opening);
    }

    public void Play()
    {
        if (!IsLoaded) return;
        _mediaPlayer.Play();
    }

    public void Pause()
    {
        if (!IsLoaded) return;
        _mediaPlayer.Pause();
    }

    public void Stop()
    {
        if (!IsLoaded) return;
        _mediaPlayer.Stop();
    }

    public void Seek(double positionPercent)
    {
        if (!IsLoaded) return;
        var normalized = Math.Clamp(positionPercent / 100d, 0d, 1d);
        _mediaPlayer.Position = (float)normalized;
    }

    public void SetVolume(int volume)
    {
        _mediaPlayer.Volume = Math.Clamp(volume, 0, 100);
    }

    private void SetState(PlaybackState state)
    {
        State = state;
        PlaybackStateChanged?.Invoke(this, state);
    }

    public void Dispose()
    {
        _currentMedia?.Dispose();
        _mediaPlayer.Dispose();
        _libVlc.Dispose();
    }
}
