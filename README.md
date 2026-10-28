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

## Project Structure
- `src/SavienPlayer.App` - WPF UI layer
- `src/SavienPlayer.Core` - domain models and interfaces
- `src/SavienPlayer.Infrastructure` - VLC-based media player service
- `tests/SavienPlayer.Tests` - test project placeholder
