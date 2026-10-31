using SavienPlayer.Core;
using LibVLCSharp.Shared;
using YoutubeExplode;
using YoutubeExplode.Videos;
using YoutubeExplode.Videos.Streams;

namespace SavienPlayer.Infrastructure;

public sealed class VlcMediaPlayerService : IMediaPlayerService, ISubtitleService, IDisposable
{
    private readonly LibVLC _libVlc;
    private readonly MediaPlayer _mediaPlayer;
    private readonly YoutubeClient _youtubeClient = new();
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
        _mediaPlayer = new MediaPlayer(_libVlc)
        {
            EnableMouseInput = false,
            EnableKeyInput = false
        };

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
        var mediaUri = Uri.TryCreate(mediaPath, UriKind.Absolute, out var parsedUri) &&
                       (parsedUri.Scheme == Uri.UriSchemeHttp || parsedUri.Scheme == Uri.UriSchemeHttps || parsedUri.Scheme == Uri.UriSchemeFile)
            ? parsedUri
            : new Uri(Path.GetFullPath(mediaPath));

        OpenMediaUri(mediaUri);
    }

    public async Task OpenUrlAsync(string url, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new ArgumentException("URL이 비어 있습니다.", nameof(url));
        }

        var trimmedUrl = url.Trim();
        var videoId = VideoId.TryParse(trimmedUrl);
        if (videoId is not null)
        {
            var manifest = await _youtubeClient.Videos.Streams.GetManifestAsync(videoId.Value, cancellationToken);
            var muxedStream = manifest
                .GetMuxedStreams()
                .OrderByDescending(s => s.VideoQuality.MaxHeight)
                .ThenByDescending(s => s.Bitrate)
                .FirstOrDefault();

            if (muxedStream is not null)
            {
                OpenMediaUri(new Uri(muxedStream.Url));
                return;
            }

            var videoOnlyStream = manifest
                .GetVideoOnlyStreams()
                .OrderByDescending(s => s.VideoQuality.MaxHeight)
                .ThenByDescending(s => s.Bitrate)
                .FirstOrDefault();

            var audioOnlyStream = manifest
                .GetAudioOnlyStreams()
                .OrderByDescending(s => s.Bitrate)
                .FirstOrDefault();

            if (videoOnlyStream is null)
            {
                throw new InvalidOperationException("재생 가능한 유튜브 스트림을 찾을 수 없습니다.");
            }

            OpenMediaUri(new Uri(videoOnlyStream.Url), audioOnlyStream?.Url);
            return;
        }

        if (!Uri.TryCreate(trimmedUrl, UriKind.Absolute, out var directUri))
        {
            throw new ArgumentException("올바른 URL 형식이 아닙니다.", nameof(url));
        }

        OpenMediaUri(directUri);
    }

    public async Task<TimeSpan?> GetMediaDurationAsync(string mediaPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(mediaPath))
        {
            return null;
        }

        try
        {
            var trimmed = mediaPath.Trim();
            var videoId = VideoId.TryParse(trimmed);
            if (videoId is not null)
            {
                var video = await _youtubeClient.Videos.GetAsync(videoId.Value, cancellationToken);
                return video.Duration;
            }

            var mediaUri = Uri.TryCreate(trimmed, UriKind.Absolute, out var parsedUri) &&
                           (parsedUri.Scheme == Uri.UriSchemeHttp || parsedUri.Scheme == Uri.UriSchemeHttps || parsedUri.Scheme == Uri.UriSchemeFile)
                ? parsedUri
                : new Uri(Path.GetFullPath(trimmed));

            using var media = new Media(_libVlc, mediaUri);
            var parseOptions = mediaUri.IsFile ? MediaParseOptions.ParseLocal : MediaParseOptions.ParseNetwork;
            await media.Parse(parseOptions, 5000, cancellationToken);
            if (media.Duration > 0)
            {
                return TimeSpan.FromMilliseconds(media.Duration);
            }
        }
        catch
        {
            // Ignore parse errors and return null when duration cannot be determined
        }

        return null;
    }

    private void OpenMediaUri(Uri mediaUri, string? audioSlaveUrl = null)
    {
        _currentMedia?.Dispose();
        _currentMedia = new Media(_libVlc, mediaUri);
        if (!string.IsNullOrWhiteSpace(audioSlaveUrl))
        {
            _currentMedia.AddOption($":input-slave={audioSlaveUrl}");
        }

        _mediaPlayer.Media = _currentMedia;
        _mediaPlayer.EnableMouseInput = false;
        _mediaPlayer.EnableKeyInput = false;
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

    public void SeekRelative(TimeSpan offset)
    {
        if (!IsLoaded) return;

        var currentTime = _mediaPlayer.Time;
        if (currentTime < 0) return;

        var targetTime = Math.Max(0L, currentTime + (long)offset.TotalMilliseconds);
        var length = _mediaPlayer.Length;
        if (length > 0)
        {
            targetTime = Math.Min(length, targetTime);
        }

        _mediaPlayer.Time = targetTime;
        if (length > 0)
        {
            PositionChanged?.Invoke(this, targetTime * 100d / length);
        }
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
