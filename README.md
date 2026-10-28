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
