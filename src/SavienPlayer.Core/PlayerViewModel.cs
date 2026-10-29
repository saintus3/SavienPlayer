using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SavienPlayer.Core;

public sealed class PlayerViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IMediaPlayerService _player;
    private readonly ISubtitleService _subtitles;
    private readonly ISettingsService _settings;
    private readonly IUiDispatcher _dispatcher;
    private readonly IUserNotifications _notifications;
    private readonly IPerformanceLogService _performance;
    private readonly LatestValue<(long Version, double Position)> _positions = new();
    private long _version;
    private bool _playbackRequested;
    private bool _disposed;
    private int _volume = 70;
    private double _position;
    private PlaybackState _state;
    private string _subtitleOffset = "0";
    private string _subtitleStatus = "자막: 없음";
    private string _performanceStatus = "성능 로그: 꺼짐";
    private IPerformanceLogSession? _session;
    private long _logVersion;
    private bool _logging;
    private PlaylistItem? _selectedItem;

    public PlayerViewModel(IMediaPlayerService player, ISubtitleService subtitles,
        ISettingsService settings, IUiDispatcher dispatcher, IUserNotifications notifications,
        IPerformanceLogService performance)
    {
        _player = player;
        _subtitles = subtitles;
        _settings = settings;
        _dispatcher = dispatcher;
        _notifications = notifications;
        _performance = performance;
        PlayCommand = new(_ => Play(), _ => !_disposed && Playlist.Items.Count > 0);
        PauseCommand = new(_ => Pause(), _ => !_disposed && Playlist.Current is not null);
        StopCommand = new(_ => Stop(), _ => !_disposed && Playlist.Current is not null);
        NextCommand = new(_ => Next(), _ => !_disposed && Playlist.GetNext() is not null);
        ClearSubtitleCommand = new(_ => ClearSubtitle(), _ => !_disposed);
        ApplySubtitleOffsetCommand = new(_ => ApplySubtitleOffset(), _ => !_disposed);
        try
        {
            var saved = settings.Load();
            var paths = (saved.Files ?? Array.Empty<string>()).Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(Path.GetFullPath).ToArray();
            Playlist.AllowDuplicates = true;
            foreach (var path in paths) Playlist.Add(path);
            Playlist.AllowDuplicates = saved.AllowDuplicates;
            _volume = Math.Clamp(saved.Volume, 0, 100);
        }
        catch (Exception ex) when (IsSettingsError(ex))
        {
            _notifications.Show("플레이리스트를 복원하지 못했습니다.");
        }
        _player.SetVolume(_volume);
        _state = _player.State;
        _player.PositionChanged += OnPositionChanged;
        _player.PlaybackStateChanged += OnPlaybackStateChanged;
    }

    public Playlist Playlist { get; } = new();
    public PlaylistItem? CurrentItem => Playlist.Current;
    public PlaylistItem? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (_selectedItem == value) return;
            _selectedItem = value;
            Changed();
        }
    }
    public PlayerCommand PlayCommand { get; }
    public PlayerCommand PauseCommand { get; }
    public PlayerCommand StopCommand { get; }
    public PlayerCommand NextCommand { get; }
    public PlayerCommand ClearSubtitleCommand { get; }
    public PlayerCommand ApplySubtitleOffsetCommand { get; }
    public bool AllowDuplicates
    {
        get => Playlist.AllowDuplicates;
        set
        {
            if (value == Playlist.AllowDuplicates) return;
            Playlist.AllowDuplicates = value;
            Changed();
            Save();
        }
    }
    public int Volume
    {
        get => _volume;
        set
        {
            value = Math.Clamp(value, 0, 100);
            if (_volume == value) return;
            _volume = value;
            _player.SetVolume(value);
            Changed();
            Save();
        }
    }
    public double Position { get => _position; private set { _position = value; Changed(); } }
    public PlaybackState State { get => _state; private set { _state = value; Changed(); } }
    public string SubtitleOffset { get => _subtitleOffset; set { _subtitleOffset = value; Changed(); } }
    public string SubtitleStatus { get => _subtitleStatus; private set { _subtitleStatus = value; Changed(); } }
    public string PerformanceStatus { get => _performanceStatus; private set { _performanceStatus = value; Changed(); } }
    public bool PerformanceLogging
    {
        get => _logging;
        set
        {
            if (_disposed || _logging == value) return;
            var version = ++_logVersion;
            _logging = value;
            Changed();
            _session?.Dispose();
            _session = null;
            PerformanceStatus = "성능 로그: 꺼짐";
            if (!value) return;
            try
            {
                _session = _performance.Start(_ => _dispatcher.Post(() =>
                {
                    if (_disposed || version != _logVersion) return;
                    PerformanceLogging = false;
                    PerformanceStatus = "성능 로그: 기록 실패";
                }));
                PerformanceStatus = $"성능 로그: {_session.LogPath}";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                PerformanceLogging = false;
                PerformanceStatus = "성능 로그: 파일 생성 실패";
            }
        }
    }

    public void AddFiles(IEnumerable<string> files, bool play = false)
    {
        PlaylistItem? first = null;
        foreach (var file in files)
        {
            var item = Playlist.Add(file);
            first ??= item;
        }
        Save();
        RefreshCommands();
        if (play && first is not null) Open(first);
    }

    public void Open(PlaylistItem item, bool play = true)
    {
        if (_disposed) return;
        if (!Playlist.Items.Contains(item)) throw new ArgumentException("항목이 플레이리스트에 없습니다.", nameof(item));
        InvalidatePlayback();
        _playbackRequested = false;
        try
        {
            _player.Stop();
            // Stop can emit a terminal native event; invalidate it before opening the new media.
            InvalidatePlayback();
            Playlist.SetCurrent(item);
            ClearSubtitle();
            Position = 0;
            _player.Open(item.FilePath);
            _playbackRequested = play;
            if (play) _player.Play();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            InvalidatePlayback();
            _playbackRequested = false;
            Playlist.SetCurrent(null);
            State = PlaybackState.Error;
            _notifications.Show("동영상을 열지 못했습니다.");
        }
        RefreshCommands();
    }

    public void Play()
    {
        if (_disposed) return;
        if (Playlist.Current is null)
        {
            var item = SelectedItem is not null && Playlist.Items.Contains(SelectedItem) ? SelectedItem : Playlist.GetNext();
            if (item is not null) Open(item);
            return;
        }
        InvalidatePlayback();
        _playbackRequested = true;
        _player.Play();
    }

    public void Pause()
    {
        if (_disposed || Playlist.Current is null) return;
        InvalidatePlayback();
        _playbackRequested = _player.State == PlaybackState.Paused;
        _player.Pause();
    }

    public void Stop()
    {
        if (_disposed) return;
        InvalidatePlayback();
        _playbackRequested = false;
        _player.Stop();
    }

    public void Next()
    {
        if (Playlist.GetNext() is { } next) Open(next);
    }

    public void Remove(IEnumerable<PlaylistItem> items)
    {
        var current = Playlist.Current;
        var play = _playbackRequested;
        Playlist.Remove(items.ToArray());
        if (current != Playlist.Current)
        {
            if (Playlist.Current is { } next) Open(next, play);
            else
            {
                Stop();
                Position = 0;
                ClearSubtitle();
            }
        }
        Save();
        RefreshCommands();
    }

    public void Move(PlaylistItem item, int index)
    {
        Playlist.Move(item, index);
        Save();
        RefreshCommands();
    }

    public void Seek(double position)
    {
        if (double.IsFinite(position)) _player.Seek(Math.Clamp(position, 0, 100));
    }

    public void LoadSubtitle(string path)
    {
        try
        {
            if (!_player.IsLoaded || Playlist.Current is null)
            {
                _notifications.Show("먼저 동영상을 열어주세요.");
                return;
            }
            if (_subtitles.TryLoad(path, out var error)) SubtitleStatus = $"자막: {Path.GetFileName(path)}";
            else _notifications.Show(error ?? "자막 로드에 실패했습니다.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            _notifications.Show("자막 로드에 실패했습니다.");
        }
    }

    public void ClearSubtitle()
    {
        _subtitles.Clear();
        SubtitleStatus = "자막: 없음";
        SubtitleOffset = "0";
    }

    public void ApplySubtitleOffset()
    {
        if (!int.TryParse(SubtitleOffset, out var offset))
        {
            _notifications.Show("자막 싱크 값은 정수(ms)로 입력해주세요.");
            return;
        }
        _subtitles.SetOffset(offset);
        SubtitleStatus = _subtitles.IsLoaded
            ? $"자막: 적용됨 (싱크 {offset}ms)"
            : $"자막: 없음 (기본 싱크 {offset}ms 저장)";
    }

    private void OnPositionChanged(object? sender, double position) =>
        _positions.Publish((Interlocked.Read(ref _version), position));

    public void DrainPosition()
    {
        if (!_disposed && _positions.TryTake(out var latest) &&
            latest.Version == Interlocked.Read(ref _version) && double.IsFinite(latest.Position))
            Position = Math.Clamp(latest.Position, 0, 100);
    }

    private void OnPlaybackStateChanged(object? sender, PlaybackState state)
    {
        var version = Interlocked.Read(ref _version);
        // Native callbacks must never wait for the UI or re-enter VLC.
        _dispatcher.Post(() =>
        {
            if (_disposed || version != Interlocked.Read(ref _version)) return;
            State = state;
            if (Playlist.Current is null) return;
            if (state == PlaybackState.Error) _playbackRequested = false;
            else if (state == PlaybackState.Ended && _playbackRequested)
            {
                if (Playlist.GetNext() is { } next) Open(next);
                else _playbackRequested = false;
            }
        });
    }

    private void InvalidatePlayback() => Interlocked.Increment(ref _version);
    private void Save()
    {
        try
        {
            _settings.Save(new PlayerSettings
            {
                Files = Playlist.Items.Select(item => item.FilePath).ToArray(),
                AllowDuplicates = AllowDuplicates, Volume = Volume
            });
        }
        catch (Exception ex) when (IsSettingsError(ex))
        {
            _notifications.Show("플레이리스트를 저장하지 못했습니다.");
        }
    }
    private static bool IsSettingsError(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException or NotSupportedException;
    private void RefreshCommands()
    {
        Changed(nameof(CurrentItem));
        PlayCommand.Refresh();
        PauseCommand.Refresh();
        StopCommand.Refresh();
        NextCommand.Refresh();
        ClearSubtitleCommand.Refresh();
        ApplySubtitleOffsetCommand.Refresh();
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        InvalidatePlayback();
        ++_logVersion;
        _player.PositionChanged -= OnPositionChanged;
        _player.PlaybackStateChanged -= OnPlaybackStateChanged;
        _session?.Dispose();
        _session = null;
        RefreshCommands();
    }
}
