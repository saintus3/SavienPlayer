# SavienPlayer

Windows C# video player (WPF, .NET 8) inspired by modern desktop players.

## Goals
- Solid MVP playback UX
- Extensible MVVM architecture
- Subtitle support and playback controls

## Tech Stack
- C# / .NET 8
- WPF
- LibVLCSharp
- Serilog (planned integration)

## Current Status
- WPF app bootstrap complete
- LibVLCSharp integrated
- Open / Play / Pause / Stop / Seek / Volume / Fullscreen wired
- Subtitle v1: load/clear + sync(ms) apply
- Playlist: multi-file add, bulk removal, drag-to-reorder, duplicate policy, and automatic next playback (#7)
- Opt-in performance logs and bounded seek-bar updates (#8)
- Per-monitor DPI scaling, adaptive controls, and monitor-aware fullscreen restoration (#9)
- Interface-injected ViewModel, separate playback/settings/subtitle services, and core unit tests (#10)

## Prerequisites (Important)
LibVLCSharp uses native VLC runtime. Install one of:
- VLC player (64-bit) in default location, or
- LibVLC native binaries deployment strategy for app distribution

If playback does not start, verify VLC runtime is installed.

## Run
Prerequisites:
- Visual Studio 2022 or newer
- .NET 8 SDK

Open `SavienPlayer.sln`, set `SavienPlayer.App` as startup project, run.

## Subtitle v1 Usage
1. Open a video file
2. Click `자막 열기` and select `.srt` (or `.ass/.ssa/.vtt`)
3. Enter sync offset in milliseconds and click `적용`
   - Positive: subtitle later
   - Negative: subtitle earlier
4. Click `자막 해제` to clear subtitle state

## Project Structure
- `src/SavienPlayer.App` - WPF views, UI adapters, native window handling, and application composition
- `src/SavienPlayer.Core` - platform-independent ViewModel, commands, domain models, and service interfaces
- `src/SavienPlayer.Infrastructure` - VLC playback/subtitles, JSON settings, and performance logging
- `tests/SavienPlayer.Tests` - core scenario and persistence unit tests, runnable without WPF or native VLC

## Playlist Usage
- `열기` adds multiple selected files and plays the first; `파일 추가` appends without interrupting playback.
- Double-click an item to play it. The current item is bold and green, independently of selection.
- Ctrl/Shift-select items and use `선택 삭제` for bulk removal. Drag an item onto another row to move it to that row's position, or into the empty area to move it to the end.
- Duplicates are blocked by default using case-insensitive absolute file paths. `중복 추가 허용` allows separate entries for the same file. Turning it off blocks future duplicates but does not remove existing entries.
- Natural completion and `다음` follow the current list order without wrapping. Moving the current item does not interrupt playback. Removing it plays the next surviving item after it if playback was active; otherwise the successor is loaded without playing. If there is no successor, playback stops.
- Order, duplicate policy, and volume are saved after edits in `%LOCALAPPDATA%\SavienPlayer\playlist.json` and restored on startup without autoplay. Existing playlist files remain compatible; a missing volume defaults to 70.

## Build Validation
Run `dotnet build SavienPlayer.sln` on Windows. Cross-compilation on other platforms uses `dotnet build SavienPlayer.sln -p:EnableWindowsTargeting=true`; running the WPF UI still requires Windows and native VLC.

Run the unit tests with `dotnet test tests/SavienPlayer.Tests/SavienPlayer.Tests.csproj`. They use fake dependencies and temporary settings directories, so they do not require Windows, native VLC, or access to the user's settings.

## Architecture and Test Boundaries (#10)

- **Core `PlayerViewModel`** owns observable playback/settings state and commands, playlist navigation and editing, subtitle status/sync validation, and performance logging opt-in. It depends only on `IMediaPlayerService`, `ISubtitleService`, `ISettingsService`, `IUiDispatcher`, `IUserNotifications`, and `IPerformanceLogService`; it has no WPF or LibVLC dependency.
- **Infrastructure services** implement native playback (`VlcMediaPlayerService`), native subtitle operations/state (`VlcSubtitleService`), JSON persistence (`JsonSettingsService`), and performance-session creation (`PerformanceLogService`). Subtitle state no longer lives in the playback service. JSON writes replace a temporary file; persisted files keep the existing `Files` and `AllowDuplicates` fields and add `Volume`.
- **App composition** in `App.OnStartup` creates and constructor-injects the services and ViewModel explicitly, without a service locator or DI-container dependency. `MainWindow` receives the ViewModel and a native player only for `VideoView` rendering; it no longer casts a playback interface to the VLC service.
- **View-only code** handles file dialogs, multi-selection/drag gestures, scrolling, the 100-ms position-drain timer, and native DPI/fullscreen layout. Playback actions use XAML commands; volume, duplicate policy, subtitle input/status and performance state use bindings.
- **Threading and ownership** remain explicit: native events post work through the dispatcher abstraction, queued work is checked against playback/session versions, and positions use a bounded latest-value mailbox. Closing the window stops view timers, unsubscribes/disposes the ViewModel and its logging session, and detaches rendering before the application disposes the native player.

The xUnit tests replace native playback, subtitles, settings, UI dispatch, notifications and logging with controlled fakes. Core scenarios cover settings restoration/defaults/failures, property notifications and commands, duplicate handling, reorder/delete playback intent, delayed or stale completion/position events, subtitle validation/reset, and performance error/disposal behavior. Persistence tests exercise the real JSON service in isolated temporary directories, including legacy files and malformed input. They do not substitute for Windows video-rendering, DPI/multi-monitor or long-running GPU benchmarks described below.

## Performance Measurement (#8)

Enable `성능 로그 기록 (5초 간격)` before playback; disable it after the run to flush and close the log. Logging is off by default and runs on a background worker, not on VLC's callback thread or the WPF dispatcher. The status line shows the JSONL file in `%LOCALAPPDATA%\SavienPlayer\Performance`.

Each file starts with a schema/version, UTC start time, sampling interval, processor count, OS, runtime, and process architecture. Each subsequent sample contains:

| Field | Meaning |
| --- | --- |
| `ElapsedSeconds`, `TimestampUtc` | Monotonic session duration and UTC timestamp |
| `CpuPercent` | Process CPU-time delta divided by elapsed time and logical processor count (0–100% of available CPU capacity) |
| `GpuBusiestEnginePercent` | Busiest valid **process-specific engine instance** from Windows PDH `GPU Engine(*)\Utilization Percentage` (0–100%); not summed across engines/adapters |
| `WorkingSetBytes`, `PrivateBytes`, `ManagedHeapBytes` | Resident process memory, private committed memory (includes native allocations), and managed heap estimate without forcing a GC |
| `MediaGeneration`, `State` | Media-open sequence within this app instance and playback state |
| `DecodedVideo`, `DisplayedPictures`, `LostPictures` | Cumulative VLC video counters for that media |

GPU data is `null` until counters have two valid readings, or when the OS/driver/performance counters do not support it; `null` is **not zero usage**. Frame counters are `null` when VLC statistics are unavailable. They describe VLC's decoder/output, not display-compositor drops. No media paths or titles are logged. Memory use is constant per sample: logs are streamed, not buffered in a growing list. A session stops at approximately 16 MiB (plus its final record); starting another session retains only the ten newest matching log files. Logging errors disable the checkbox instead of terminating playback.

### Repeatable Windows benchmark

1. Use a Release build, the same Windows machine, GPU driver, VLC runtime, power plan, decoder settings, window size, and local media files for baseline and candidate. Close unrelated applications. Record commit, CPU/GPU/RAM, media SHA-256, codec, resolution, frame rate, bitrate, and whether hardware decoding is active alongside each log.
2. Use fixed 1080p and 4K assets long enough for uninterrupted runs. Run each resolution for **10 minutes and 2 hours**, three times per configuration. Restart the app before every run, enable logging before opening the asset, and do not seek, switch media, resize, or change subtitles during the steady-state interval.
3. Treat the first five minutes as warm-up. Compare CPU/GPU median and p95 on the remaining playing samples; do not aggregate paused/stopped states or mix different `MediaGeneration` values.
4. Compute interval drop rate as `delta(LostPictures) / (delta(DisplayedPictures) + delta(LostPictures))`; skip intervals with negative deltas, unavailable counters, or a zero denominator. Do not compare cumulative totals from runs of different durations.
5. Compare median `PrivateBytes` and `ManagedHeapBytes` in minutes 5–10 against the final five minutes of each two-hour run. The initial regression budget is private-memory growth **≤ max(32 MiB, 10% of baseline)** and managed-heap growth **≤ max(8 MiB, 10% of baseline)**. Inspect the trend too: a sustained upward slope across all three runs requires investigation even below the budget. Working set alone is not a leak detector.
6. Require no frame-drop regression on identical assets/settings; missing GPU/frame data must be marked unavailable rather than counted as a pass. Separately repeat pause/resume, seek, playlist-next, and shutdown with logging enabled to check lifecycle behavior.

These are explicit evaluation budgets, not a claim that all hardware or long-running playback already passes. Preserve raw logs and record baseline/candidate medians, p95, frame-drop rates, and memory deltas for each of the four scenario combinations.

### Bottleneck improvement and comparison

Previously each position event allocated a captured callback and queued it on the dispatcher. While the UI was stalled, every obsolete value stayed queued. Position events now replace one `(playback version, position)` value under a short lock; a background-priority WPF timer consumes the latest value at most every 100 ms. There are no per-position dispatcher posts, storage is bounded to one pending value, and stale playback versions remain ignored. Completion/error events still dispatch asynchronously; they are not coalesced.

A Linux x64 Release-mode .NET 8-targeted console experiment (SDK/runtime 10.0.401/10.0.12) published 100,000 distinct positions without a consumer, then drained them. The old model used `Queue<Action>` with one captured position per callback; the candidate used the production `LatestValue<(long, double)>`. After 1,000 warm-up publications, `GC.GetAllocatedBytesForCurrentThread` measured the publication loop:

| Metric | Old callback-queue model | Latest-value implementation |
| --- | ---: | ---: |
| Pending position values after burst | 100,000 | 1 |
| Publication allocations | 11,697,544 bytes | 0 bytes |
| Last consumed position | 99,999 | 99,999 |

This is a deterministic backlog/allocation comparison, **not a WPF throughput or CPU/GPU playback benchmark**. To repeat, run the two publication loops in a Release console referencing Core, with no consumption until the burst ends; warm up first, create the mailbox before the measured interval, and measure only current-thread allocations. Queue growth and runtime versions affect the old byte total. The candidate must retain only the last complete version/position pair and must not allocate per publication.

Validation for #8 also exercised concurrent publication, consumption, actual five-second process sampling, JSONL metadata, missing GPU data, retention, cancellation, and sampling-error reporting. Those checks used a temporary console before the unit test infrastructure was introduced in #10.

| Actual playback comparison | Status |
| --- | --- |
| 1080p / 10 min; 4K / 10 min | Pending Windows/VLC/GPU benchmark |
| 1080p / 2 hours; 4K / 2 hours | Pending Windows/VLC/GPU benchmark |

The Linux implementation environment cannot run WPF or validate Windows PDH/GPU playback. Consequently real playback CPU/GPU deltas and compliance with the two-hour memory budget remain unverified; do not substitute the synthetic queue experiment for those results.

## DPI and Multiple Monitors (#9)

The application manifest requests **Per-Monitor V2** DPI awareness, with Per-Monitor fallback. WPF sizes remain in device-independent units; layout rounding and pixel snapping keep control edges aligned at fractional scales. Playback/subtitle controls wrap on narrower windows and scroll vertically when space is limited. Buttons and text inputs have a minimum 32-DIP height; playlist rows and checkboxes have a minimum 28-DIP height. The playlist shares available width with the video rather than forcing a fixed minimum window width.

On startup and DPI/display/work-area changes, a normal window is constrained to its current monitor's usable work area, with minimum sizes reduced when the monitor cannot accommodate them. Fullscreen covers the current monitor, including its taskbar area. Exiting fullscreen restores the previous native window placement, including an originally maximized state and the original frame/resize style. Saved bounds are remapped to the original monitor's current DPI and constrained to its work area; if that monitor has been removed, an available monitor is used instead. Native monitor coordinates are physical pixels and can be negative; they are not assigned directly to WPF `Left`/`Top`. Display and DPI notifications trigger deferred bounds updates after WPF processes the DPI change. If a monitor is disconnected, Windows selects an available monitor and the bounds update keeps the window accessible.

### Windows verification matrix

Use the built executable (so its embedded manifest is applied), installed native VLC, and actual video playback. For each case below, test **125%, 150%, and 200%**, plus a mixed-scale pair such as 100%/200%. Include a secondary display left of or above the primary, and taskbars on different edges.

| Scenario | Expected result |
| --- | --- |
| Start at each scale, resize to the smallest usable window | Readable text, no overlapping controls; playback/subtitle actions wrap and remain reachable by scrolling |
| Click every button, checkbox, slider, text input and playlist row | Visible control and hit area agree; seek, volume, subtitle sync, selection and drag/drop still work |
| Drag a normal window between mixed-DPI displays | Text and controls rescale without bitmap blur; video and playlist resize without manual scaling |
| Enter/exit fullscreen on each monitor from a normal window | Fullscreen stays on that monitor, covers the screen, and restores the original position and size |
| Enter/exit fullscreen from a maximized window | Original maximized state is restored, not replaced with a normal window |
| Move fullscreen with Win+Shift+Arrow | Window covers the destination display; repeat exiting/re-entering fullscreen |
| Change resolution, scale, taskbar edge or auto-hide setting while open | Bounds follow the current monitor/work area; controls remain reachable |
| Disconnect the active monitor while normal and while fullscreen | Window stays reachable on a remaining display; fullscreen can be exited |
| Repeat fullscreen toggles during playback, pause and subtitle display | No loss of video, incorrect scaling, misplaced click targets, or unexpected playback changes |

Record Windows build, monitor resolutions/scales, taskbar configuration and failures/screenshots for each run. Build and XAML/manifest validation can run on Linux, but the native window transitions, video-host rendering, text sharpness and click alignment require Windows hardware validation. These interactive matrix checks have **not** been executed in the Linux implementation environment.

Implementation validation for #9 included Debug/Release cross-builds, embedded manifest/XAML checks, and 11 temporary-console checks of the production placement calculation covering DPI ratios, negative monitor origins, removal, taskbar workspace offsets and work-area clamping. No additional test dependencies were introduced for #9; unit test infrastructure was subsequently added in #10.
