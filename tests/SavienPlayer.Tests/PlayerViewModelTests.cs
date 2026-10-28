using System.Text.Json;
using SavienPlayer.Core;
using Xunit;

namespace SavienPlayer.Tests;

public sealed class PlayerViewModelTests
{
    [Fact]
    public void ConstructorUsesDefaultsAndDoesNotSaveOrStartPlayback()
    {
        using var f = new PlayerFixture();
        var vm = f.ViewModel;
        Assert.Equal(70, vm.Volume);
        Assert.Equal(70, f.Player.Volume);
        Assert.False(vm.AllowDuplicates);
        Assert.Empty(vm.Playlist.Items);
        Assert.False(vm.PlayCommand.CanExecute(null));
        Assert.False(vm.PauseCommand.CanExecute(null));
        Assert.Equal(0, f.Settings.Saves);
        Assert.Empty(f.Player.Opened);
        Assert.False(vm.PerformanceLogging);
    }

    [Fact]
    public void ConstructorRestoresDuplicatesWithoutAllowingNewDuplicates()
    {
        using var f = new PlayerFixture();
        f.Settings.Saved = new() { Files = new[] { "a.mp4", "a.mp4", "", "b.mp4" }, Volume = 35 };
        var vm = f.ViewModel;
        Assert.Equal(3, vm.Playlist.Items.Count);
        Assert.False(vm.AllowDuplicates);
        Assert.Equal(35, f.Player.Volume);
        vm.AddFiles(new[] { "a.mp4" });
        Assert.Equal(3, vm.Playlist.Items.Count);
        Assert.Equal(vm.Playlist.Items.Select(i => i.FilePath), f.Settings.Saved.Files);
    }

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(120, 100)]
    public void ConstructorClampsSavedVolume(int saved, int expected)
    {
        using var f = new PlayerFixture();
        f.Settings.Saved = new() { Volume = saved };
        Assert.Equal(expected, f.ViewModel.Volume);
        Assert.Equal(expected, f.Player.Volume);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedSettingsLoadIsReportedAndDefaultsRemainUsable(bool malformed)
    {
        using var f = new PlayerFixture();
        f.Settings.LoadError = malformed ? new JsonException() : new IOException();
        Assert.Equal(70, f.ViewModel.Volume);
        Assert.Single(f.Notifications.Messages);
        f.AddThree();
        Assert.NotNull(f.ViewModel.Playlist.Current);
    }

    [Fact]
    public void InvalidSavedPathDoesNotPartiallyRestorePlaylist()
    {
        using var f = new PlayerFixture();
        f.Settings.Saved = new() { Files = new[] { "a.mp4", "bad\0path" }, AllowDuplicates = true };
        Assert.Empty(f.ViewModel.Playlist.Items);
        Assert.False(f.ViewModel.AllowDuplicates);
        Assert.Single(f.Notifications.Messages);
    }

    [Fact]
    public void SettingsChangesNotifyPersistAndAvoidRedundantSaves()
    {
        using var f = new PlayerFixture();
        var changed = new List<string?>();
        f.ViewModel.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        f.ViewModel.Volume = 500;
        f.ViewModel.Volume = 100;
        f.ViewModel.AllowDuplicates = true;
        f.ViewModel.AllowDuplicates = true;
        Assert.Equal(new[] { "Volume", "AllowDuplicates" }, changed);
        Assert.Equal(2, f.Settings.Saves);
        Assert.Equal(100, f.Settings.Saved.Volume);
        Assert.True(f.Settings.Saved.AllowDuplicates);
    }

    [Fact]
    public void FailedSaveIsReportedWithoutLosingInMemoryChanges()
    {
        using var f = new PlayerFixture();
        f.Settings.FailSave = true;
        f.ViewModel.Volume = 12;
        Assert.Equal(12, f.Player.Volume);
        Assert.Equal(12, f.ViewModel.Volume);
        Assert.Single(f.Notifications.Messages);
    }

    [Fact]
    public void AppendDoesNotPlayAndOpenPlaysFirstAddedItem()
    {
        using var f = new PlayerFixture();
        var refreshes = 0;
        f.ViewModel.PlayCommand.CanExecuteChanged += (_, _) => ++refreshes;
        f.AddThree(false);
        Assert.Empty(f.Player.Opened);
        Assert.True(f.ViewModel.PlayCommand.CanExecute(null));
        f.ViewModel.AddFiles(new[] { "d.mp4", "e.mp4" }, true);
        Assert.Equal("d.mp4", f.ViewModel.Playlist.Current?.DisplayName);
        Assert.Single(f.Player.Opened);
        Assert.Equal(1, f.Player.Plays);
        Assert.True(refreshes > 0);
    }

    [Fact]
    public void PlayUsesSelectedItemWhenNoCurrentItem()
    {
        using var f = new PlayerFixture();
        f.AddThree(false);
        f.ViewModel.SelectedItem = f.ViewModel.Playlist.Items[1];
        f.ViewModel.PlayCommand.Execute(null);
        Assert.Same(f.ViewModel.SelectedItem, f.ViewModel.Playlist.Current);
        Assert.True(f.ViewModel.PauseCommand.CanExecute(null));
    }

    [Fact]
    public void DuplicatePolicyDoesNotRemoveExistingDuplicates()
    {
        using var f = new PlayerFixture();
        var vm = f.ViewModel;
        vm.AddFiles(new[] { "a.mp4", "a.mp4" });
        Assert.Single(vm.Playlist.Items);
        vm.AllowDuplicates = true;
        vm.AddFiles(new[] { "a.mp4" });
        vm.AllowDuplicates = false;
        vm.AddFiles(new[] { "a.mp4" });
        Assert.Equal(2, vm.Playlist.Items.Count);
        Assert.NotSame(vm.Playlist.Items[0], vm.Playlist.Items[1]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BulkDeleteCurrentUsesFirstSurvivingSuccessorAndPreservesIntent(bool paused)
    {
        using var f = new PlayerFixture();
        f.AddThree();
        var vm = f.ViewModel;
        if (paused) vm.PauseCommand.Execute(null);
        var before = f.Player.Plays;
        vm.Remove(vm.Playlist.Items.Take(2).ToArray());
        Assert.Equal("c.mp4", vm.Playlist.Current?.DisplayName);
        Assert.Equal(before + (paused ? 0 : 1), f.Player.Plays);
        Assert.Single(f.Settings.Saved.Files);
        f.Player.Emit(PlaybackState.Ended);
        f.Dispatcher.Drain();
        Assert.Equal(2, f.Player.Opened.Count);
    }

    [Fact]
    public void RemovingNonCurrentDoesNotInterruptAndDeletingLastCurrentClearsState()
    {
        using var f = new PlayerFixture();
        f.AddThree();
        var vm = f.ViewModel;
        vm.LoadSubtitle("test.srt");
        vm.Remove(new[] { vm.Playlist.Items[1] });
        Assert.Single(f.Player.Opened);
        Assert.True(f.Subtitles.IsLoaded);
        vm.Remove(vm.Playlist.Items.ToArray());
        Assert.Null(vm.Playlist.Current);
        Assert.False(f.Subtitles.IsLoaded);
        Assert.Equal(0, vm.Position);
        Assert.False(vm.PlayCommand.CanExecute(null));
        f.Player.Emit(PlaybackState.Ended);
        f.Dispatcher.Drain();
        Assert.Single(f.Player.Opened);
    }

    [Fact]
    public void ReorderingPreservesCurrentIdentityAndNextFollowsNewOrder()
    {
        using var f = new PlayerFixture();
        f.AddThree();
        var vm = f.ViewModel;
        var current = vm.Playlist.Current;
        vm.Move(vm.Playlist.Items[2], 1);
        Assert.Same(current, vm.Playlist.Current);
        Assert.True(current!.IsCurrent);
        vm.NextCommand.Execute(null);
        Assert.Equal("c.mp4", vm.Playlist.Current?.DisplayName);
        Assert.False(current.IsCurrent);
        Assert.Equal(new[] { "a.mp4", "c.mp4", "b.mp4" }, f.Settings.Saved.Files.Select(Path.GetFileName));
    }

    [Fact]
    public void EndedThenStoppedIsDeferredAndAdvancesOnlyOnce()
    {
        using var f = new PlayerFixture();
        f.AddThree();
        f.Player.Emit(PlaybackState.Ended);
        f.Player.Emit(PlaybackState.Ended);
        f.Player.Emit(PlaybackState.Stopped);
        Assert.Single(f.Player.Opened);
        f.Dispatcher.Drain();
        Assert.Equal("b.mp4", f.ViewModel.Playlist.Current?.DisplayName);
        Assert.Equal(2, f.Player.Opened.Count);
    }

    [Theory]
    [InlineData("stop")]
    [InlineData("pause")]
    [InlineData("open")]
    [InlineData("play")]
    public void QueuedCompletionIsInvalidatedByExplicitPlaybackCommands(string command)
    {
        using var f = new PlayerFixture();
        f.AddThree();
        f.Player.Emit(PlaybackState.Ended);
        switch (command)
        {
            case "stop": f.ViewModel.Stop(); break;
            case "pause": f.ViewModel.Pause(); break;
            case "open": f.ViewModel.Open(f.ViewModel.Playlist.Items[2]); break;
            case "play": f.ViewModel.Play(); break;
        }
        f.Dispatcher.Drain();
        Assert.Equal(command == "open" ? "c.mp4" : "a.mp4", f.ViewModel.Playlist.Current?.DisplayName);
    }

    [Fact]
    public void StopGeneratedCompletionCannotAdvanceNewlyOpenedMedia()
    {
        using var f = new PlayerFixture();
        f.Player.EndOnStop = true;
        f.AddThree();
        f.Dispatcher.Drain();
        Assert.Equal("a.mp4", f.ViewModel.Playlist.Current?.DisplayName);
        Assert.Single(f.Player.Opened);
    }

    [Fact]
    public void LastCompletionAndErrorDoNotContinueOnLaterUnrelatedTerminalEvents()
    {
        using var f = new PlayerFixture();
        f.AddThree();
        f.Player.Emit(PlaybackState.Error);
        f.Dispatcher.Drain();
        f.Player.Emit(PlaybackState.Ended);
        f.Dispatcher.Drain();
        Assert.Single(f.Player.Opened);
        f.ViewModel.Open(f.ViewModel.Playlist.Items[2]);
        f.Player.Emit(PlaybackState.Ended);
        f.Dispatcher.Drain();
        Assert.False(f.ViewModel.NextCommand.CanExecute(null));
        Assert.Equal(2, f.Player.Opened.Count);
    }

    [Fact]
    public void PauseToggleRestoresPlaybackIntentForDeletion()
    {
        using var f = new PlayerFixture();
        f.AddThree();
        f.ViewModel.Pause();
        f.ViewModel.Pause();
        var plays = f.Player.Plays;
        f.ViewModel.Remove(new[] { f.ViewModel.Playlist.Current! });
        Assert.Equal(plays + 1, f.Player.Plays);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PauseAfterStopOrNaturalEndDoesNotRequestPlaybackWhenDeletingCurrent(bool ended)
    {
        using var f = new PlayerFixture();
        var vm = f.ViewModel;
        vm.AddFiles(new[] { "a.mp4" }, true);
        if (ended)
        {
            f.Player.Emit(PlaybackState.Ended);
            f.Dispatcher.Drain();
        }
        else vm.Stop();
        vm.AddFiles(new[] { "b.mp4", "c.mp4" });
        var plays = f.Player.Plays;
        vm.Pause();
        vm.Remove(new[] { vm.Playlist.Current! });
        f.Dispatcher.Drain();
        Assert.Equal("b.mp4", vm.Playlist.Current?.DisplayName);
        Assert.Equal(plays, f.Player.Plays);
    }

    [Fact]
    public void PauseWithQueuedNativeEndCancelsCompletionWithoutResumingSuccessorOnDelete()
    {
        using var f = new PlayerFixture();
        f.AddThree();
        f.Player.Emit(PlaybackState.Ended);
        var plays = f.Player.Plays;
        f.ViewModel.Pause();
        f.ViewModel.Remove(new[] { f.ViewModel.Playlist.Current! });
        f.Dispatcher.Drain();
        Assert.Equal("b.mp4", f.ViewModel.Playlist.Current?.DisplayName);
        Assert.Equal(plays, f.Player.Plays);
    }

    [Fact]
    public void PositionsAreLatestOnlyBoundedAndDoNotPostUiCallbacks()
    {
        using var f = new PlayerFixture();
        f.AddThree();
        f.Dispatcher.Drain();
        for (var i = 0; i < 10000; ++i) f.Player.EmitPosition(i);
        Assert.Empty(f.Dispatcher.Pending);
        Assert.Equal(0, f.ViewModel.Position);
        f.ViewModel.DrainPosition();
        Assert.Equal(100, f.ViewModel.Position);
        f.Player.EmitPosition(-4);
        f.ViewModel.DrainPosition();
        Assert.Equal(0, f.ViewModel.Position);
        f.Player.EmitPosition(double.NaN);
        f.ViewModel.DrainPosition();
        Assert.Equal(0, f.ViewModel.Position);
    }

    [Fact]
    public void PositionsQueuedBeforePlaybackCommandAreDiscarded()
    {
        using var f = new PlayerFixture();
        f.AddThree();
        f.Player.EmitPosition(80);
        f.ViewModel.Next();
        f.ViewModel.DrainPosition();
        Assert.Equal(0, f.ViewModel.Position);
        f.Player.EmitPosition(12);
        f.ViewModel.DrainPosition();
        Assert.Equal(12, f.ViewModel.Position);
        f.ViewModel.Seek(150);
        Assert.Equal(100, f.Player.Position);
    }

    [Fact]
    public void OpenFailureCancelsOldCallbacksAndAllowsRecovery()
    {
        using var f = new PlayerFixture();
        f.AddThree();
        f.Player.Emit(PlaybackState.Ended);
        f.Player.FailOpen = true;
        f.ViewModel.Next();
        f.Dispatcher.Drain();
        Assert.Null(f.ViewModel.Playlist.Current);
        Assert.Single(f.Notifications.Messages);
        f.Player.FailOpen = false;
        f.ViewModel.Play();
        Assert.Equal("a.mp4", f.ViewModel.Playlist.Current?.DisplayName);
    }

    [Fact]
    public void SubtitleValidationAndOffsetsAreViewModelState()
    {
        using var f = new PlayerFixture();
        var vm = f.ViewModel;
        vm.LoadSubtitle("test.srt");
        Assert.Equal("먼저 동영상을 열어주세요.", Assert.Single(f.Notifications.Messages));
        vm.SubtitleOffset = "invalid";
        vm.ApplySubtitleOffsetCommand.Execute(null);
        Assert.Equal("자막 싱크 값은 정수(ms)로 입력해주세요.", f.Notifications.Messages.Last());
        vm.SubtitleOffset = "-150";
        vm.ApplySubtitleOffset();
        Assert.Equal(-150, f.Subtitles.OffsetMilliseconds);
        Assert.Contains("기본 싱크 -150ms", vm.SubtitleStatus);
        f.AddThree();
        Assert.Equal(0, f.Subtitles.OffsetMilliseconds);
        vm.LoadSubtitle("test.srt");
        Assert.Equal("자막: test.srt", vm.SubtitleStatus);
        vm.SubtitleOffset = "250";
        vm.ApplySubtitleOffset();
        Assert.Contains("싱크 250ms", vm.SubtitleStatus);
        vm.ClearSubtitleCommand.Execute(null);
        Assert.False(f.Subtitles.IsLoaded);
        Assert.Equal("0", vm.SubtitleOffset);
        Assert.Equal("자막: 없음", vm.SubtitleStatus);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SubtitleLoadFailuresAreReportedWithoutChangingStatus(bool throws)
    {
        using var f = new PlayerFixture();
        f.AddThree();
        f.Subtitles.ThrowOnLoad = throws;
        f.Subtitles.Error = "자막 파일을 찾을 수 없습니다.";
        f.ViewModel.LoadSubtitle("missing.srt");
        Assert.Single(f.Notifications.Messages);
        Assert.Equal("자막: 없음", f.ViewModel.SubtitleStatus);
    }

    [Fact]
    public void SwitchingMediaResetsLoadedSubtitleAndSync()
    {
        using var f = new PlayerFixture();
        f.AddThree();
        f.ViewModel.LoadSubtitle("test.srt");
        f.ViewModel.SubtitleOffset = "100";
        f.ViewModel.ApplySubtitleOffset();
        f.ViewModel.Next();
        Assert.False(f.Subtitles.IsLoaded);
        Assert.Equal(0, f.Subtitles.OffsetMilliseconds);
        Assert.Equal("0", f.ViewModel.SubtitleOffset);
    }

    [Fact]
    public void PerformanceOptInDisposesSessionsAndPostsErrors()
    {
        using var f = new PlayerFixture();
        var vm = f.ViewModel;
        Assert.Empty(f.Performance.Sessions);
        vm.PerformanceLogging = true;
        var session = Assert.Single(f.Performance.Sessions);
        Assert.Contains(session.LogPath, vm.PerformanceStatus);
        session.Fail();
        Assert.True(vm.PerformanceLogging);
        f.Dispatcher.Drain();
        Assert.False(vm.PerformanceLogging);
        Assert.Equal("성능 로그: 기록 실패", vm.PerformanceStatus);
        Assert.Equal(1, session.Disposals);
    }

    [Fact]
    public void NativePlaybackStateNotificationsAreMarshalledAndStaleStateIsIgnored()
    {
        using var f = new PlayerFixture();
        f.AddThree();
        Assert.Equal(PlaybackState.None, f.ViewModel.State);
        f.Dispatcher.Drain();
        Assert.Equal(PlaybackState.Playing, f.ViewModel.State);
        f.Player.Emit(PlaybackState.Buffering);
        f.ViewModel.Stop();
        f.Dispatcher.Drain();
        Assert.Equal(PlaybackState.Stopped, f.ViewModel.State);
    }

    [Fact]
    public void StalePerformanceErrorCannotDisableNewSession()
    {
        using var f = new PlayerFixture();
        var vm = f.ViewModel;
        vm.PerformanceLogging = true;
        var old = f.Performance.Sessions[0];
        old.Fail();
        vm.PerformanceLogging = false;
        vm.PerformanceLogging = true;
        f.Dispatcher.Drain();
        Assert.True(vm.PerformanceLogging);
        Assert.Equal(1, old.Disposals);
        Assert.Equal(0, f.Performance.Sessions[1].Disposals);
    }

    [Fact]
    public void PerformanceStartFailureRollsBackUiState()
    {
        using var f = new PlayerFixture();
        f.Performance.FailStart = true;
        f.ViewModel.PerformanceLogging = true;
        Assert.False(f.ViewModel.PerformanceLogging);
        Assert.Equal("성능 로그: 파일 생성 실패", f.ViewModel.PerformanceStatus);
    }

    [Fact]
    public void DisposalUnsubscribesAndInvalidatesAlreadyQueuedCallbacks()
    {
        using var f = new PlayerFixture();
        f.AddThree();
        var vm = f.ViewModel;
        vm.PerformanceLogging = true;
        f.Performance.Sessions[0].Fail();
        f.Player.Emit(PlaybackState.Ended);
        f.Player.EmitPosition(60);
        Assert.Equal(1, f.Player.PositionSubscribers);
        Assert.Equal(1, f.Player.StateSubscribers);
        vm.Dispose();
        vm.Dispose();
        Assert.Equal(0, f.Player.PositionSubscribers);
        Assert.Equal(0, f.Player.StateSubscribers);
        Assert.False(vm.PlayCommand.CanExecute(null));
        Assert.False(vm.ApplySubtitleOffsetCommand.CanExecute(null));
        f.Dispatcher.Drain();
        vm.DrainPosition();
        Assert.Equal(0, vm.Position);
        Assert.Single(f.Player.Opened);
        Assert.Equal(1, f.Performance.Sessions[0].Disposals);
    }
}
