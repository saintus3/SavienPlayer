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
        _mediaPlayer.EncounteredError += (_, _) => QueueCallback(() => Complete(PlaybackState.Error));
    }

    public void Open(string mediaPath)
    {
        lock (_commandGate)
        {
            if (_disposed) return;
            _mediaPlayer.Stop();
            Interlocked.Increment(ref _mediaVersion);
            ResetTimeline();
            _currentMedia?.Dispose();
            _currentMedia = new Media(_libVlc, new Uri(mediaPath));
            _mediaPlayer.Media = _currentMedia;
            IsLoaded = true;
            _requestedState = PlaybackState.Opening;
            SetState(PlaybackState.Opening);

            IsSubtitleLoaded = false;
            CurrentSubtitlePath = null;
            OffsetMilliseconds = 0;
        }
    }

    public void Play()
    {
        lock (_commandGate)
        {
            if (_disposed || !IsLoaded || _requestedState == PlaybackState.Playing) return;
            var resume = _requestedState == PlaybackState.Paused;
            _requestedState = PlaybackState.Playing;
            if (resume)
                _mediaPlayer.SetPause(false);
            else if (!_mediaPlayer.Play())
                Complete(PlaybackState.Error);
        }
    }

    public void Pause()
    {
        lock (_commandGate)
        {
            if (_disposed || !IsLoaded || _requestedState != PlaybackState.Playing) return;
            _requestedState = PlaybackState.Paused;
            _mediaPlayer.SetPause(true);
        }
    }

    public void Stop()
    {
        lock (_commandGate)
        {
            if (_disposed || !IsLoaded) return;
            _requestedState = PlaybackState.Stopped;
            _mediaPlayer.Stop();
            Interlocked.Increment(ref _mediaVersion);
            ResetTimeline();
            SetState(PlaybackState.Stopped);
            TimeChanged?.Invoke(this, TimeSpan.Zero);
            PositionChanged?.Invoke(this, 0);
        }
    }

    public void Seek(double positionPercent)
    {
        lock (_commandGate)
        {
            if (_disposed || !IsLoaded || !double.IsFinite(positionPercent) ||
                !_mediaPlayer.IsSeekable || _mediaPlayer.Length <= 0 ||
                _requestedState is not (PlaybackState.Playing or PlaybackState.Paused)) return;

            ResetTimeline();
            var normalized = Math.Clamp(positionPercent / 100d, 0d, 1d);
            _seekTarget = (long)(_mediaPlayer.Length * normalized);
            _seekStarted = Stopwatch.GetTimestamp();
            Trace.WriteLine($"Playback seek: target={_seekTarget}ms, intent={_requestedState}");
            // VLC's time seek flushes the demux/decoder buffers and resets its shared A/V clock.
            // Do not stop/restart playback or toggle pause around this asynchronous operation.
            _mediaPlayer.Time = _seekTarget;
        }
    }

    public void SetVolume(int volume)
    {
        lock (_commandGate)
        {
            if (_disposed) return;
            _mediaPlayer.Volume = Math.Clamp(volume, 0, 100);
        }
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

            if (!File.Exists(subtitlePath))
            {
                errorMessage = "자막 파일을 찾을 수 없습니다.";
                return false;
            }

            var ok = _mediaPlayer.AddSlave(MediaSlaveType.Subtitle, subtitlePath, true);
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
    }

    public void Clear()
    {
        lock (_commandGate)
        {
            if (_disposed) return;
            IsSubtitleLoaded = false;
            CurrentSubtitlePath = null;
            OffsetMilliseconds = 0;
            _mediaPlayer.SetSpuDelay(0);
        }
    }

    public void SetOffset(int milliseconds)
    {
        lock (_commandGate)
        {
            if (_disposed) return;
            OffsetMilliseconds = milliseconds;
            _mediaPlayer.SetSpuDelay(milliseconds * 1000L);
        }
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
        _requestedState = state;
        ResetTimeline();
        SetState(state);
    }

    private void ResetTimeline()
    {
        Interlocked.Increment(ref _timelineVersion);
        _seekTarget = -1;
    }

    private void OnTimeChanged(long time)
    {
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
            _libVlc.Dispose();
        }
    }
}
