# SavienPlayer repository instructions

## Build and verification

Requires the .NET 8 SDK; the app is a Windows WPF application.

```powershell
dotnet build SavienPlayer.sln
dotnet test SavienPlayer.sln
```

`tests/SavienPlayer.Tests` currently has no test framework or test cases. Once tests are added, run one with:

```powershell
dotnet test tests/SavienPlayer.Tests/SavienPlayer.Tests.csproj --filter "FullyQualifiedName~<TestName>"
```

There is no separate lint command configured. The README describes running the app from Visual Studio with `SavienPlayer.App` as the startup project.

## Architecture

- `SavienPlayer.App` is the WPF presentation layer. `App` creates and owns the player service for the application lifetime; `MainWindow` wires controls and playback events to it, and `PlaylistWindow` manages the observable playlist and delegates playback through a callback.
- `SavienPlayer.Core` defines the backend-neutral `IMediaPlayerService`, `ISubtitleService`, and `PlaybackState` contract. Playback state and position are surfaced through events; keep consumers coupled to these contracts where possible.
- `SavienPlayer.Infrastructure` contains the VLC-backed implementation and a stub implementation. `VlcMediaPlayerService` implements both Core service interfaces and owns LibVLC resources.
- The video surface is a LibVLCSharp WPF `VideoView`. `MainWindow` also needs `VlcMediaPlayerService.NativePlayer` to attach it, so changes to player construction or the backend must account for this concrete integration point.

## Repository-specific conventions

- Playback control belongs behind `IMediaPlayerService`; subtitle load/clear/offset belongs behind `ISubtitleService`. When changing a service, preserve the event/state contract used by the WPF UI.
- `MainWindow` is code-behind driven rather than currently implemented with a ViewModel. Keep UI control handling in the App layer and backend behavior in Infrastructure; Core should remain free of WPF and LibVLC types.
- The `VlcMediaPlayerService` instance is disposed by `App.OnExit`. Keep native VLC/media resources within that service's ownership and lifetime.
- UI labels and user-facing messages are primarily Korean; retain that language for related UI additions.
