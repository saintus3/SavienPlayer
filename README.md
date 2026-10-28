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

## Playback failures and recovery (#2)

Playback failures show an actionable Korean message for missing files, read
permissions, I/O errors, unavailable codecs, damaged/undecodable data, or an
unknown cause. Failed playback is stopped, the timeline/subtitle UI is reset, and
the application stays open. After fixing the problem, click **재생** to reopen
the last requested file, or **열기** to choose another file. Opening a new file
invalidates pending errors from the previous session.

The app writes diagnostics to
`%LOCALAPPDATA%\SavienPlayer\Logs\playback-YYYYMMDD-PID.log`. The error dialog
includes the path. Logs contain the operation, error category, local media path,
playback state, managed HRESULT/stack trace when available, and up to 16 recent
VLC warnings/errors (each limited to 2,048 characters). VLC's log events are
delivered asynchronously, so these engine-wide records are explicitly marked
unattributed and are not used to classify the current file. Late log events are
also written individually. Logs are local only and
can contain private file paths; review/redact them before sharing and delete old
logs as needed. If logging cannot be initialized, a debugger trace is still
attempted. A later disk-write failure disables the file listener rather than
crashing playback.

VLC's `EncounteredError` event does not provide a structured error code.
Codec/decoding classifications therefore require explicit evidence from a
session-versioned VLC error-dialog callback; ambiguous failures remain
**Unknown** rather than guessing from file
extensions. An empty file is classified as damaged data before invoking VLC.
The player does not claim that every malformed file or partially unsupported
audio/video track will produce a fatal VLC error.

### Failure/recovery checks (Windows + VLC)

The existing test project has no runner; these are manual scenarios, not
automated test results. Run as a normal user (not administrator):

| Case | Setup | Expected result |
| --- | --- | --- |
| Missing file | Open a valid file, stop, delete/rename it, then click Play | Missing-file message; no crash |
| Read permission | Remove the current user's read access to a test copy, then select it | Permission-specific message, not missing-file |
| Damaged data | Open an empty `.mp4`, then a truncated copy of a known-good video | Empty file reports damaged data; native failure on truncated media includes VLC diagnostics, possibly Unknown if inconclusive |
| Unsupported codec | Use a known sample requiring a decoder absent from the installed VLC build | When VLC reports an explicit missing-decoder failure, codec-specific advice; no inference from the extension alone |
| I/O failure | Keep a test file exclusively locked by another process, then open it | I/O-specific message and managed error code/stack |
| Native/runtime setup | Launch without a compatible 64-bit VLC runtime | Startup guidance and diagnostic log, followed by clean exit |

After each playback failure, restore access/the file and click Play, then open
a different valid file. Confirm audio/video, seek, pause/resume, and subtitles
work again, and the previous error dialog does not reappear. Check one failure
record per attempt, the matching category/file, and either a managed exception
or native diagnostic context. Also deny log-folder writes and verify playback
errors still show without a secondary crash. Re-run the #1 stress scenarios to
check the nonblocking native-callback/UI boundary.

## Settings and resume playback (#3)

Preferences are stored locally in
`%LOCALAPPDATA%\SavienPlayer\settings.json` (JSON schema version `1`).
They include volume (0–100), requested playback speed (0.25–4×), normal window
size, maximized/fullscreen state, and the last opened file and position in
milliseconds. The speed slider changes the requested rate; VLC may reject or
not honor a rate for some media/protocols, which is recorded in the trace.
Fullscreen exit restores the previous windowed maximized/normal state.
Window dimensions are fitted to the primary work area on startup; position
and monitor placement are not persisted.

- Volume/speed changes, seeks, Stop, fullscreen toggles, and file selection
  trigger a save. Window size and playback position are checkpointed every five
  seconds and on normal window close, before the player is disposed.
- Writes use a unique temporary file in the settings folder, flush it, and
  replace the settings file. Failed reads/writes are logged without preventing
  playback. An unexpected process termination may lose changes since the last
  checkpoint; simultaneous app instances use last-writer-wins.
- Missing, invalid, oversized (over 1 MiB), or unreadable settings fall back to
  defaults. Known-version numeric values are clamped to supported ranges.
  Version `1` has no migrations yet; missing properties use defaults.
  Unknown versions are not loaded **or overwritten** during that session.
- At startup the last accessible file automatically starts playback. Its saved
  timestamp is applied once VLC has actually started and reports a seekable
  stream with a known duration. Stop, another file, or an explicit seek cancels
  a pending resume. A position within the last second or beyond the new duration
  starts at zero; a stopped/finished file also restarts at zero.
  If readiness is still unavailable after five seconds from native playback start,
  the next time notification cancels resume and tracks actual playback instead.
- If the saved file was moved/deleted or cannot be found, a Korean notice is
  shown and the player remains ready for **열기**. Other playback failures use
  the #2 error/retry flow. Settings contain local media paths and stay on this
  machine; review them before sharing.

### Settings verification (Windows + VLC)

There is no executable test framework in the existing placeholder test project.
Use these repeatable manual checks:

1. Change volume and speed (for example 35 and 1.5×), resize the normal window,
   enable fullscreen, and play/seek into a known file. Close normally and relaunch.
   Verify all preferences, fullscreen, and the saved playback point are restored.
   Exit fullscreen and confirm the saved windowed dimensions/state return.
2. Repeat while paused after seeking, while maximized, and after leaving playback
   running for more than five seconds. Verify long-file resume uses milliseconds
   rather than rounded percentage. Stop or finish playback and restart: it should
   begin at zero, not immediately end.
3. Close immediately after opening a saved file, before VLC is ready, and
   relaunch: the pending resume target must not be overwritten with startup zero.
   During startup resume, Stop/open another file and verify no delayed seek leaks
   into the new session. Check the `Playback resume: target=...ms` trace.
4. Rename/delete the last file and relaunch. Dismiss the notice and open a valid
   file. Restore a permission-denied/damaged file and verify the #2 recovery UI.
5. Back up `settings.json`, then test invalid JSON, `null`, an oversized file,
   negative position, invalid dimensions, and out-of-range volume/speed.
   The app must start safely with defaults or normalized values. Test a future
   `Version` value and confirm the original file is unchanged after closing.
6. Deny writes to the settings directory and repeat preference changes/close.
   Playback should continue and diagnostics should explain the save failure.
   Restore the backup and permissions afterwards; repeat the #1 seek/pause stress
   checks with non-default playback speed.
