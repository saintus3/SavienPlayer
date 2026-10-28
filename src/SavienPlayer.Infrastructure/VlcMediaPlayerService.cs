using SavienPlayer.Core;
using LibVLCSharp.Shared;
using System.Diagnostics;

namespace SavienPlayer.Infrastructure;

public sealed class VlcMediaPlayerService : IMediaPlayerService, ISubtitleService, IDisposable
{
    private readonly LibVLC _libVlc;
    private readonly MediaPlayer _mediaPlayer;
    private Media? _currentMedia;
    private readonly object _commandGate = new();
    private readonly object _callbackGate = new();
    private Task _callbacks = Task.CompletedTask;
    private int _mediaVersion;
    private int _timelineVersion;
    private bool _disposed;
    private PlaybackState _requestedState = PlaybackState.None;
    private long _seekTarget = -1;
    private long _seekStarted;
    private string? _mediaPath;
    private long? _resumeTime;
    private float _playbackRate = 1;
    private bool _playbackStarted;
    private long _resumeWaitStarted;
    private readonly object _diagnosticGate = new();
    private readonly Queue<string> _nativeErrors = new();
    private SubtitleDocument? _subtitleDocument;
    private bool _subtitlesDisabled;
    private bool _originalSubtitleStyle;
    private string? _nativeSubtitlePath;
    private bool _muted;

    public event EventHandler<double>? PositionChanged;
    public event EventHandler<TimeSpan>? TimeChanged;
    public event EventHandler<PlaybackState>? PlaybackStateChanged;
    public event EventHandler<PlaybackError>? PlaybackFailed;

    public bool IsLoaded { get; private set; }
    public PlaybackState State { get; private set; } = PlaybackState.None;
    public PlaybackError? LastError { get; private set; }
    public PlaybackResumePoint? GetResumePoint()
    {
        lock (_commandGate)
        {
            if (_disposed || !IsLoaded || _mediaPath == null) return null;
            var time = _requestedState is PlaybackState.Stopped or PlaybackState.Ended
                ? 0 : _resumeTime ?? (_seekTarget >= 0 ? _seekTarget : Math.Max(0, _mediaPlayer.Time));
            var position = TimeSpan.FromTicks(Math.Clamp(time, 0,
                TimeSpan.MaxValue.Ticks / TimeSpan.TicksPerMillisecond) * TimeSpan.TicksPerMillisecond);
            return new PlaybackResumePoint(_mediaPath, position);
        }
    }

    public MediaPlayer NativePlayer => _mediaPlayer;

    public bool IsSubtitleLoaded { get; private set; }
    public string? CurrentSubtitlePath { get; private set; }
    public int OffsetMilliseconds { get; private set; }
    public bool UsesOriginalStyle => _originalSubtitleStyle;
    bool ISubtitleService.IsLoaded => IsSubtitleLoaded;

    public VlcMediaPlayerService()
    {
        LibVLCSharp.Shared.Core.Initialize();
        _libVlc = new LibVLC();
        _mediaPlayer = new MediaPlayer(_libVlc);
        _mediaPlayer.EnableKeyInput = false;
        _libVlc.Log += OnVlcLog;
        _libVlc.SetDialogHandlers(
            (title, text) =>
            {
                var diagnostic = $"{title}: {text}";
                QueueCallback(() => FailPlayback("VLC error dialog", nativeDiagnostic: diagnostic));
                return Task.CompletedTask;
            },
            (dialog, _, _, _, _, _) => { dialog.Dismiss(); return Task.CompletedTask; },
            (dialog, _, _, _, _, _, _, _) => { dialog.Dismiss(); return Task.CompletedTask; },
            (dialog, _, _, _, _, _, _) => { dialog.Dismiss(); return Task.CompletedTask; },
            (_, _, _) => Task.CompletedTask);

        _mediaPlayer.TimeChanged += (_, e) =>
        {
            var time = e.Time;
            QueueCallback(() => OnTimeChanged(time), timeline: true);
        };
        _mediaPlayer.PositionChanged += (_, e) =>
        {
            var position = e.Position * 100d;
            QueueCallback(() => PositionChanged?.Invoke(this, position), timeline: true);
        };

        _mediaPlayer.Playing += (_, _) => QueueCallback(() =>
        {
            if (!_playbackStarted) _resumeWaitStarted = Stopwatch.GetTimestamp();
            _playbackStarted = true;
            _mediaPlayer.Mute = _muted;
            if (_originalSubtitleStyle && IsSubtitleLoaded)
            {
                if (!ApplyOriginalSubtitle())
                {
                    _originalSubtitleStyle = false;
                    _mediaPlayer.SetSpu(-1);
                }
            }
            else if (IsSubtitleLoaded || _subtitlesDisabled)
                _mediaPlayer.SetSpu(-1);
            ApplyPlaybackRate();
            TryRestorePosition();
            if (_requestedState == PlaybackState.Paused)
                _mediaPlayer.SetPause(true);
            else if (_requestedState == PlaybackState.Playing)
                SetState(PlaybackState.Playing);
        });
        _mediaPlayer.Paused += (_, _) => QueueCallback(() =>
        {
            if (_requestedState == PlaybackState.Playing)
                _mediaPlayer.SetPause(false);
            else if (_requestedState == PlaybackState.Paused)
                SetState(PlaybackState.Paused);
        });
        _mediaPlayer.Buffering += (_, e) =>
        {
            var cache = e.Cache;
            QueueCallback(() =>
            {
                if (_requestedState == PlaybackState.Playing)
                    SetState(cache < 100 ? PlaybackState.Buffering : PlaybackState.Playing);
            });
        };
        _mediaPlayer.EndReached += (_, _) => QueueCallback(() => Complete(PlaybackState.Ended));
        _mediaPlayer.EncounteredError += (_, _) => QueueCallback(() => FailPlayback("VLC.EncounteredError"));
        _mediaPlayer.LengthChanged += (_, _) => QueueCallback(TryRestorePosition);
        _mediaPlayer.SeekableChanged += (_, _) => QueueCallback(TryRestorePosition);
    }

    public void Open(string mediaPath, TimeSpan? startPosition = null)
    {
        ExecuteCommand("Open", () =>
        {
            if (_disposed) return;
            _mediaPath = mediaPath;
            LastError = null;
            _mediaPlayer.Stop();
            _playbackStarted = false;
            Interlocked.Increment(ref _mediaVersion);
            ResetTimeline();
            _resumeTime = startPosition.HasValue && startPosition.Value > TimeSpan.Zero
                ? startPosition.Value.Ticks / TimeSpan.TicksPerMillisecond : null;
            IsLoaded = false;
            lock (_diagnosticGate) _nativeErrors.Clear();
            _mediaPlayer.Media = null;
            _currentMedia?.Dispose();
            _currentMedia = null;
            _mediaPath = Path.GetFullPath(mediaPath);
            // Opening the file, rather than File.Exists, preserves access-denied and I/O errors.
            using (var file = File.Open(_mediaPath, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete))
            {
                if (file.Length == 0)
                    throw new InvalidDataException("The media file is empty.");
            }
            _currentMedia = new Media(_libVlc, new Uri(_mediaPath));
            _mediaPlayer.Media = _currentMedia;
            IsLoaded = true;
            _requestedState = PlaybackState.Opening;
            SetState(PlaybackState.Opening);

            IsSubtitleLoaded = false;
            CurrentSubtitlePath = null;
            _subtitleDocument = null;
            _nativeSubtitlePath = null;
            _subtitlesDisabled = false;
            try
            {
                var sidecar = SubtitleDocument.FindSidecar(_mediaPath);
                if (sidecar != null && !TryLoad(sidecar, out var error))
                    Trace.TraceWarning($"Automatic subtitle load failed: {error}");
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                Trace.TraceWarning($"Automatic subtitle discovery failed: {exception}");
            }
        });
    }

    public void Play()
    {
        ExecuteCommand("Play", () =>
        {
            if (!_disposed && LastError != null && _mediaPath != null)
                Open(_mediaPath);
            if (_disposed || !IsLoaded || _requestedState == PlaybackState.Playing) return;
            var resume = _requestedState == PlaybackState.Paused;
            _requestedState = PlaybackState.Playing;
            if (resume)
                _mediaPlayer.SetPause(false);
            else if (!_mediaPlayer.Play())
                FailPlayback("VLC.Play returned false");
        });
    }

    public void Pause()
    {
        ExecuteCommand("Pause", () =>
        {
            if (_disposed || !IsLoaded || _requestedState != PlaybackState.Playing) return;
            _requestedState = PlaybackState.Paused;
            _mediaPlayer.SetPause(true);
        });
    }

    public void Stop()
    {
        ExecuteCommand("Stop", () =>
        {
            if (_disposed || !IsLoaded) return;
            _requestedState = PlaybackState.Stopped;
            _nativeSubtitlePath = null;
            _playbackStarted = false;
            _resumeTime = null;
            _mediaPlayer.Stop();
            Interlocked.Increment(ref _mediaVersion);
            ResetTimeline();
            SetState(PlaybackState.Stopped);
            TimeChanged?.Invoke(this, TimeSpan.Zero);
            PositionChanged?.Invoke(this, 0);
        });
    }

    public void TogglePlayback()
    {
        ExecuteCommand("TogglePlayback", () =>
        {
            if (_requestedState == PlaybackState.Playing)
                Pause();
            else
                Play();
        });
    }

    public void Seek(double positionPercent)
    {
        ExecuteCommand("Seek", () =>
        {
            if (_disposed || !IsLoaded || !double.IsFinite(positionPercent) ||
                !_mediaPlayer.IsSeekable || _mediaPlayer.Length <= 0 ||
                _requestedState is not (PlaybackState.Playing or PlaybackState.Paused)) return;

            var normalized = Math.Clamp(positionPercent / 100d, 0d, 1d);
            SeekTo((long)(_mediaPlayer.Length * normalized));
        });
    }

    public void SeekRelative(TimeSpan offset)
    {
        ExecuteCommand("SeekRelative", () =>
        {
            if (!IsLoaded || !_mediaPlayer.IsSeekable || _mediaPlayer.Length <= 0 ||
                _requestedState is not (PlaybackState.Playing or PlaybackState.Paused)) return;
            var current = _resumeTime ?? (_seekTarget >= 0 ? _seekTarget : Math.Max(0, _mediaPlayer.Time));
            var target = Math.Clamp((decimal)current + offset.Ticks / TimeSpan.TicksPerMillisecond,
                0, _mediaPlayer.Length);
            SeekTo((long)target);
        });
    }

    private void SeekTo(long time)
    {
        ResetTimeline();
        _resumeTime = null;
        _seekTarget = time;
        _seekStarted = Stopwatch.GetTimestamp();
        Trace.WriteLine($"Playback seek: target={_seekTarget}ms, intent={_requestedState}");
        // VLC's time seek flushes the demux/decoder buffers and resets its shared A/V clock.
        // Do not stop/restart playback or toggle pause around this asynchronous operation.
        _mediaPlayer.Time = _seekTarget;
    }

    public void SetMuted(bool muted)
    {
        ExecuteCommand("SetMuted", () =>
        {
            _muted = muted;
            _mediaPlayer.Mute = muted;
        });
    }

    public void SetVolume(int volume)
    {
        ExecuteCommand("SetVolume", () =>
        {
            if (_disposed) return;
            _mediaPlayer.Volume = Math.Clamp(volume, 0, 100);
        });
    }

    public void SetPlaybackRate(double rate)
    {
        ExecuteCommand("SetPlaybackRate", () =>
        {
            if (!double.IsFinite(rate) || rate is < 0.25 or > 4) return;
            _playbackRate = (float)rate;
            if (IsLoaded && _requestedState is PlaybackState.Playing or PlaybackState.Paused)
                ApplyPlaybackRate();
        });
    }

    private void ApplyPlaybackRate()
    {
        if (_mediaPlayer.SetRate(_playbackRate) != 0)
            Trace.TraceWarning($"VLC rejected playback rate {_playbackRate}; requested preference is retained.");
    }

    private void TryRestorePosition()
    {
        if (!_resumeTime.HasValue || !_playbackStarted || !IsLoaded || !_mediaPlayer.IsSeekable ||
            _mediaPlayer.Length <= 0 ||
            _requestedState is not (PlaybackState.Playing or PlaybackState.Paused)) return;
        var target = _resumeTime.Value;
        _resumeTime = null;
        // A completed or shortened file starts over rather than immediately reaching EOF.
        if (target >= _mediaPlayer.Length - 1000) target = 0;
        ResetTimeline();
        _seekTarget = target;
        _seekStarted = Stopwatch.GetTimestamp();
        Trace.WriteLine($"Playback resume: target={target}ms");
        _mediaPlayer.Time = target;
    }

    public bool TryLoad(string subtitlePath, out string? errorMessage)
    {
        lock (_commandGate)
        {
            errorMessage = null;

            if (_disposed || !IsLoaded)
            {
                errorMessage = "먼저 동영상을 열어주세요.";
                return false;
            }

            try
            {
                var document = SubtitleDocument.Load(subtitlePath);
                if (_originalSubtitleStyle && _playbackStarted)
                {
                    if (!_mediaPlayer.AddSlave(MediaSlaveType.Subtitle, subtitlePath, true))
                    {
                        errorMessage = "VLC에서 원본 자막을 불러오지 못했습니다.";
                        return false;
                    }
                    _nativeSubtitlePath = subtitlePath;
                    _mediaPlayer.SetSpuDelay(OffsetMilliseconds * 1000L);
                }
                else _mediaPlayer.SetSpu(-1);
                _subtitleDocument = document;
                IsSubtitleLoaded = true;
                CurrentSubtitlePath = subtitlePath;
                _subtitlesDisabled = false;
                return true;
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or
                System.Security.SecurityException or System.Text.RegularExpressions.RegexMatchTimeoutException)
            {
                Trace.TraceWarning($"Subtitle load failed: {exception}");
                errorMessage = "자막을 읽을 수 없습니다. UTF-8/유니코드 SRT·ASS·SSA·VTT 형식과 파일 접근 권한을 확인해주세요.";
                return false;
            }
        }
    }

    public void Clear()
    {
        lock (_commandGate)
        {
            if (_disposed) return;
            IsSubtitleLoaded = false;
            CurrentSubtitlePath = null;
            _subtitleDocument = null;
            _subtitlesDisabled = true;
            _nativeSubtitlePath = null;
            _mediaPlayer.SetSpu(-1);
            _mediaPlayer.SetSpuDelay(0);
        }
    }

    public void SetOffset(int milliseconds)
    {
        lock (_commandGate)
        {
            if (_disposed) return;
            OffsetMilliseconds = Math.Clamp(milliseconds, -600000, 600000);
            if (_originalSubtitleStyle) _mediaPlayer.SetSpuDelay(OffsetMilliseconds * 1000L);
        }
    }

    public string GetText(TimeSpan position)
    {
        lock (_commandGate)
            return _originalSubtitleStyle ? "" : _subtitleDocument?.GetText(position, OffsetMilliseconds) ?? "";
    }

    public bool UseOriginalStyle(bool enabled)
    {
        lock (_commandGate)
        {
            if (_disposed) return false;
            if (enabled && IsSubtitleLoaded && _playbackStarted && !ApplyOriginalSubtitle()) return false;
            _originalSubtitleStyle = enabled;
            if (!enabled)
            {
                _nativeSubtitlePath = null;
                _mediaPlayer.SetSpu(-1);
                _mediaPlayer.SetSpuDelay(0);
            }
            return true;
        }
    }

    private bool ApplyOriginalSubtitle()
    {
        if (CurrentSubtitlePath == null) return true;
        if (_nativeSubtitlePath != CurrentSubtitlePath)
        {
            if (!_mediaPlayer.AddSlave(MediaSlaveType.Subtitle, CurrentSubtitlePath, true))
            {
                Trace.TraceWarning("VLC original subtitle load failed.");
                return false;
            }
            _nativeSubtitlePath = CurrentSubtitlePath;
        }
        _mediaPlayer.SetSpuDelay(OffsetMilliseconds * 1000L);
        return true;
    }

    private void SetState(PlaybackState state)
    {
        if (State == state) return;
        Trace.WriteLine($"Playback state: {State} -> {state}, intent={_requestedState}");
        State = state;
        PlaybackStateChanged?.Invoke(this, state);
    }

    private void Complete(PlaybackState state)
    {
        _nativeSubtitlePath = null;
        _resumeTime = null;
        _playbackStarted = false;
        _requestedState = state;
        ResetTimeline();
        SetState(state);
    }

    private void ExecuteCommand(string operation, Action command)
    {
        lock (_commandGate)
        {
            if (_disposed) return;
            try
            {
                command();
            }
            catch (Exception exception)
            {
                FailPlayback(operation, exception);
            }
        }
    }

    private void OnVlcLog(object? sender, LogEventArgs e)
    {
        if (e.Level != LogLevel.Error && e.Level != LogLevel.Warning) return;
        // Bound diagnostic history independently of playback state and native command locks.
        var diagnostic = $"{e.Level} [{e.Module}] {e.Message}";
        if (diagnostic.Length > 2048) diagnostic = diagnostic[..2048];
        lock (_diagnosticGate)
        {
            if (_nativeErrors.Count == 16) _nativeErrors.Dequeue();
            _nativeErrors.Enqueue(diagnostic);
        }
        // LibVLCSharp delivers Log via Task.Run: it cannot be attributed to the active media.
        // Write late diagnostics too, but never use this engine-wide history for classification.
        Trace.TraceWarning($"VLC diagnostic (unattributed): {diagnostic}");
    }

    private void FailPlayback(string operation, Exception? exception = null, string? nativeDiagnostic = null)
    {
        if (LastError != null) return;
        string diagnostic;
        lock (_diagnosticGate) diagnostic = string.Join(Environment.NewLine, _nativeErrors);
        var kind = exception switch
        {
            FileNotFoundException or DirectoryNotFoundException => PlaybackErrorKind.FileNotFound,
            UnauthorizedAccessException or System.Security.SecurityException => PlaybackErrorKind.AccessDenied,
            InvalidDataException => PlaybackErrorKind.Decoding,
            IOException => PlaybackErrorKind.InputOutput,
            _ => ClassifyNativeError(nativeDiagnostic ?? "")
        };
        LastError = new PlaybackError(kind);
        Trace.TraceError($"Playback failure: operation={operation}, kind={kind}, file={_mediaPath}, " +
            $"state={State}, HRESULT={exception?.HResult:X8}{Environment.NewLine}{exception}" +
            $"{Environment.NewLine}VLC error callback: {nativeDiagnostic}" +
            $"{Environment.NewLine}Recent engine diagnostics (unattributed):{Environment.NewLine}{diagnostic}");

        _requestedState = PlaybackState.Error;
        _playbackStarted = false;
        _resumeTime = null;
        IsLoaded = false;
        try
        {
            _mediaPlayer.Stop();
            _mediaPlayer.Media = null;
            _currentMedia?.Dispose();
            _currentMedia = null;
        }
        catch (Exception cleanupException)
        {
            Trace.TraceError($"Playback failure cleanup: {cleanupException}");
        }
        Interlocked.Increment(ref _mediaVersion);
        ResetTimeline();
        IsSubtitleLoaded = false;
        CurrentSubtitlePath = null;
        _subtitleDocument = null;
        SetState(PlaybackState.Error);
        TimeChanged?.Invoke(this, TimeSpan.Zero);
        PositionChanged?.Invoke(this, 0);
        PlaybackFailed?.Invoke(this, LastError);
    }

    private static PlaybackErrorKind ClassifyNativeError(string diagnostic)
    {
        // EncounteredError has no error code. Only classify explicit native evidence.
        if (diagnostic.Contains("No such file or directory", StringComparison.OrdinalIgnoreCase))
            return PlaybackErrorKind.FileNotFound;
        if (diagnostic.Contains("Permission denied", StringComparison.OrdinalIgnoreCase) ||
            diagnostic.Contains("Access denied", StringComparison.OrdinalIgnoreCase))
            return PlaybackErrorKind.AccessDenied;
        if (diagnostic.Contains("Input/output error", StringComparison.OrdinalIgnoreCase))
            return PlaybackErrorKind.InputOutput;
        if (diagnostic.Contains("no suitable decoder module", StringComparison.OrdinalIgnoreCase) ||
            diagnostic.Contains("no decoder modules matched", StringComparison.OrdinalIgnoreCase) ||
            diagnostic.Contains("Codec not supported", StringComparison.OrdinalIgnoreCase))
            return PlaybackErrorKind.UnsupportedCodec;
        if (diagnostic.Contains("cannot peek", StringComparison.OrdinalIgnoreCase) ||
            diagnostic.Contains("moov atom not found", StringComparison.OrdinalIgnoreCase) ||
            diagnostic.Contains("invalid data", StringComparison.OrdinalIgnoreCase) ||
            diagnostic.Contains("decoding failed", StringComparison.OrdinalIgnoreCase))
            return PlaybackErrorKind.Decoding;
        return PlaybackErrorKind.Unknown;
    }

    private void ResetTimeline()
    {
        Interlocked.Increment(ref _timelineVersion);
        _seekTarget = -1;
    }

    private void OnTimeChanged(long time)
    {
        if (_resumeTime.HasValue)
        {
            TryRestorePosition();
            if (!_resumeTime.HasValue) return;
            if (!_playbackStarted || Stopwatch.GetElapsedTime(_resumeWaitStarted).TotalSeconds < 5) return;
            Trace.TraceWarning("Resume skipped: media did not become seekable with a known duration within five seconds.");
            _resumeTime = null;
        }
        if (_seekTarget >= 0)
        {
            var elapsed = Stopwatch.GetElapsedTime(_seekStarted).TotalMilliseconds;
            var error = time - _seekTarget;
            if (Math.Abs(error) <= 1000 || elapsed >= 5000)
            {
                var stats = _currentMedia?.Statistics;
                Trace.WriteLine($"Playback seek observation: target={_seekTarget}ms, actual={time}ms, " +
                    $"error={error}ms, elapsed={elapsed:F0}ms, confirmed={Math.Abs(error) <= 1000}, " +
                    $"displayed={stats?.DisplayedPictures}, lost={stats?.LostPictures}");
                _seekTarget = -1;
            }
        }
        TimeChanged?.Invoke(this, TimeSpan.FromMilliseconds(time));
    }

    private void QueueCallback(Action callback, bool timeline = false)
    {
        var mediaVersion = Volatile.Read(ref _mediaVersion);
        var timelineVersion = Volatile.Read(ref _timelineVersion);
        // Never block a VLC event thread: Stop/Dispose may wait for that thread to return.
        lock (_callbackGate)
        {
            _callbacks = _callbacks.ContinueWith(_ =>
            {
                lock (_commandGate)
                {
                    if (_disposed || mediaVersion != _mediaVersion ||
                        (timeline && timelineVersion != _timelineVersion)) return;
                    try
                    {
                        callback();
                    }
                    catch (Exception exception)
                    {
                        Trace.WriteLine($"Playback callback failed: {exception}");
                    }
                }
            }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
        }
    }

    public void Dispose()
    {
        lock (_commandGate)
        {
            if (_disposed) return;
            _disposed = true;
            IsLoaded = false;
            _mediaPlayer.Stop();
            _mediaPlayer.Dispose();
            _currentMedia?.Dispose();
            _libVlc.UnsetDialogHandlers();
            _libVlc.Log -= OnVlcLog;
            _libVlc.Dispose();
        }
    }
}
