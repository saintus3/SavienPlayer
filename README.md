# SavienPlayer

Windows C# video player (WPF, .NET 8) inspired by modern desktop players.

## Goals
- Solid MVP playback UX
- Extensible MVVM architecture
- Subtitle support and playback controls

## Tech Stack
- C# / .NET 8
- WPF
- LibVLCSharp (planned integration)
- Serilog (planned integration)

## Roadmap
1. Bootstrap WPF solution and projects
2. Implement media player service abstraction
3. Wire playback controls and timeline
4. Add subtitle loading/sync and settings
5. Stabilize and package for Windows

## Run
Prerequisites:
- Visual Studio 2022 or newer
- .NET 8 SDK

Open the solution and run `SavienPlayer.App`.

## Project Structure
- `src/SavienPlayer.App` - WPF UI layer
- `src/SavienPlayer.Core` - domain models and interfaces
- `src/SavienPlayer.Infrastructure` - infrastructure implementations (stub for now)
- `tests/SavienPlayer.Tests` - test project placeholder
