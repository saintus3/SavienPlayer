using SavienPlayer.Core;
using Xunit;

namespace SavienPlayer.Tests;

public sealed class PlaylistTests
{
    [Fact]
    public void AddNormalizesPathsAndComparesCaseInsensitively()
    {
        var playlist = new Playlist();
        var first = playlist.Add("video.mp4");
        Assert.Same(first, playlist.Add("./video.mp4"));
        Assert.Same(first, playlist.Add("VIDEO.MP4"));
        Assert.True(Path.IsPathFullyQualified(first.FilePath));
    }

    [Fact]
    public void CurrentFlagsNotifyAndNextDoesNotWrap()
    {
        var playlist = new Playlist();
        var first = playlist.Add("a.mp4");
        var second = playlist.Add("b.mp4");
        var notifications = new List<string?>();
        first.PropertyChanged += (_, e) => notifications.Add(e.PropertyName);
        Assert.Same(first, playlist.GetNext());
        playlist.SetCurrent(first);
        Assert.True(first.IsCurrent);
        Assert.Same(second, playlist.GetNext());
        playlist.SetCurrent(second);
        Assert.False(first.IsCurrent);
        Assert.True(second.IsCurrent);
        Assert.Null(playlist.GetNext());
        Assert.Equal(new[] { "IsCurrent", "IsCurrent" }, notifications);
    }

    [Fact]
    public void DeletingTailCurrentDoesNotFallBackToPreviousItem()
    {
        var playlist = new Playlist();
        var first = playlist.Add("a.mp4");
        var last = playlist.Add("b.mp4");
        playlist.SetCurrent(last);
        playlist.Remove(new[] { last });
        Assert.Null(playlist.Current);
        Assert.False(last.IsCurrent);
        Assert.Same(first, Assert.Single(playlist.Items));
    }

    [Fact]
    public void InvalidCurrentAndMoveAreRejectedWithoutMutatingCollection()
    {
        var playlist = new Playlist();
        var item = playlist.Add("a.mp4");
        var other = new Playlist().Add("b.mp4");
        Assert.Throws<ArgumentException>(() => playlist.SetCurrent(other));
        Assert.Throws<ArgumentException>(() => playlist.Move(other, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => playlist.Move(item, 1));
        Assert.Same(item, Assert.Single(playlist.Items));
    }

    [Fact]
    public void LatestValueReplacesPendingValueAndConsumesOnce()
    {
        var mailbox = new LatestValue<int>();
        Assert.False(mailbox.TryTake(out _));
        for (var i = 0; i < 10000; ++i) mailbox.Publish(i);
        Assert.True(mailbox.TryTake(out var value));
        Assert.Equal(9999, value);
        Assert.False(mailbox.TryTake(out _));
    }
}
