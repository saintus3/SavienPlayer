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
- `src/SavienPlayer.App` - WPF UI layer
- `src/SavienPlayer.Core` - domain models and interfaces
- `src/SavienPlayer.Infrastructure` - VLC-based media player service
- `tests/SavienPlayer.Tests` - test project placeholder

## Playlist Usage
- `열기` adds multiple selected files and plays the first; `파일 추가` appends without interrupting playback.
- Double-click an item to play it. The current item is bold and green, independently of selection.
- Ctrl/Shift-select items and use `선택 삭제` for bulk removal. Drag an item onto another row to move it to that row's position, or into the empty area to move it to the end.
- Duplicates are blocked by default using case-insensitive absolute file paths. `중복 추가 허용` allows separate entries for the same file. Turning it off blocks future duplicates but does not remove existing entries.
- Natural completion and `다음` follow the current list order without wrapping. Moving the current item does not interrupt playback. Removing it plays the next surviving item after it if playback was active; otherwise the successor is loaded without playing. If there is no successor, playback stops.
- Order and duplicate policy are saved after edits in `%LOCALAPPDATA%\SavienPlayer\playlist.json` and restored on startup without autoplay.

## Build Validation
Run `dotnet build SavienPlayer.sln` on Windows. Cross-compilation on other platforms uses `dotnet build SavienPlayer.sln -p:EnableWindowsTargeting=true`; running the WPF UI still requires Windows and native VLC. The test project is currently a placeholder with no test runner.

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

Validation also exercised concurrent publication, consumption, actual five-second process sampling, JSONL metadata, missing GPU data, retention, cancellation, and sampling-error reporting. The repository test project remains a placeholder; these checks used a temporary console without adding dependencies.

| Actual playback comparison | Status |
| --- | --- |
| 1080p / 10 min; 4K / 10 min | Pending Windows/VLC/GPU benchmark |
| 1080p / 2 hours; 4K / 2 hours | Pending Windows/VLC/GPU benchmark |

The Linux implementation environment cannot run WPF or validate Windows PDH/GPU playback. Consequently real playback CPU/GPU deltas and compliance with the two-hour memory budget remain unverified; do not substitute the synthetic queue experiment for those results.
