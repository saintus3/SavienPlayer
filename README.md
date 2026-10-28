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

## Playback stability verification (#1)

The test project is currently a placeholder (no test runner). Validate on Windows
with the installed VLC runtime and both a 1080p and a 4K file, including media
longer than two hours. Use the same files/VLC version before and after the change.

1. Play, then seek at least 10 times, alternating forward/backward between 5%,
   80%, 20%, 95%, and 50%. Include rapid repeated seeks and a seek near the end.
   Check lip sync or a visible/audible clap after each seek settles.
2. Pause, seek repeatedly, and wait five seconds: playback must remain paused.
   Resume and check that sound and picture continue from the new position.
3. Repeat pause/resume 30 times, including rapid double clicks on Pause and Play,
   and pause immediately after starting or seeking. Pause must never toggle back
   to playing; elapsed time must not accumulate while paused.
4. Stop during a seek, play again, and open another file during playback.
   Confirm the old position/state does not reappear and the UI remains responsive.
   Repeat with a high-bitrate 4K file while dragging the seek bar.

Capture `System.Diagnostics.Trace` output (Visual Studio Output while debugging,
or a host-configured trace listener). State transitions include playback intent.
Seek observations include the target/observed timestamp, signed error, elapsed
wall time, and VLC's cumulative displayed/lost picture counts. Compare lost-picture
deltas over equivalent playback intervals, not across different media sessions.
`confirmed=True` means a timestamp was observed within one second of the target;
it is **not** a measurement or guarantee of A/V drift. `confirmed=False` after
five seconds of observed playback warrants investigation. Paused seeks may not
emit a timestamp until resume. Unseekable/unknown-length media and non-finite
seek inputs are ignored rather than altering the current playback clock.

VLC owns audio/video clock and decoder-buffer resynchronization during a time
seek. The service preserves play/pause intent rather than forcing a restart.
Visual A/V validation on real hardware is still required; timestamp logs alone
cannot establish lip-sync accuracy or guarantee frame delivery under overload.
