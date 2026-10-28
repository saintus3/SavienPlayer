using SavienPlayer.Core;
using LibVLCSharp.Shared;

namespace SavienPlayer.Infrastructure;

public sealed class VlcMediaPlayerService : IMediaPlayerService, ISubtitleService, IDisposable
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

    public bool IsSubtitleLoaded { get; private set; }
    public string? CurrentSubtitlePath { get; private set; }
    public int OffsetMilliseconds { get; private set; }
    bool ISubtitleService.IsLoaded => IsSubtitleLoaded;

    public VlcMediaPlayerService()
    {
        LibVLCSharp.Shared.Core.Initialize();
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

        IsSubtitleLoaded = false;
        CurrentSubtitlePath = null;
        OffsetMilliseconds = 0;
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

    public bool TryLoad(string subtitlePath, out string? errorMessage)
    {
        errorMessage = null;

        if (!IsLoaded)
        {
            errorMessage = "먼저 동영상을 열어주세요.";
            return false;
        }

        if (!File.Exists(subtitlePath))
        {
            errorMessage = "자막 파일을 찾을 수 없습니다.";
            return false;
        }

        var subtitleUri = new Uri(Path.GetFullPath(subtitlePath)).AbsoluteUri;
        var ok = _mediaPlayer.AddSlave(MediaSlaveType.Subtitle, subtitleUri, true);
        if (!ok)
        {
            errorMessage = "자막 로드에 실패했습니다.";
            return false;
        }

        IsSubtitleLoaded = true;
        CurrentSubtitlePath = subtitlePath;

        if (OffsetMilliseconds != 0)
        {
            _mediaPlayer.SetSpuDelay(OffsetMilliseconds * 1000L);
        }

        return true;
    }

    public void Clear()
    {
        IsSubtitleLoaded = false;
        CurrentSubtitlePath = null;
        OffsetMilliseconds = 0;
        _mediaPlayer.SetSpuDelay(0);
    }

    public void SetOffset(int milliseconds)
    {
        OffsetMilliseconds = milliseconds;
        _mediaPlayer.SetSpuDelay(milliseconds * 1000L);
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
