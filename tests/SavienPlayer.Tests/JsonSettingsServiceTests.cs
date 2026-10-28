using System.Text.Json;
using SavienPlayer.Core;
using SavienPlayer.Infrastructure;
using Xunit;

namespace SavienPlayer.Tests;

public sealed class JsonSettingsServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Directory.GetCurrentDirectory(), "TestResults", Guid.NewGuid().ToString("N"));
    private string SettingsPath => Path.Combine(_directory, "playlist.json");

    [Fact]
    public void MissingFileReturnsDefaults()
    {
        var settings = new JsonSettingsService(SettingsPath).Load();
        Assert.Equal(70, settings.Volume);
        Assert.False(settings.AllowDuplicates);
        Assert.Empty(settings.Files);
    }

    [Fact]
    public void LegacyPlaylistKeepsDuplicatesAndDefaultsVolume()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(SettingsPath, """{"Files":["a.mp4","a.mp4"],"AllowDuplicates":false}""");
        var settings = new JsonSettingsService(SettingsPath).Load();
        Assert.Equal(new[] { "a.mp4", "a.mp4" }, settings.Files);
        Assert.False(settings.AllowDuplicates);
        Assert.Equal(70, settings.Volume);
    }

    [Fact]
    public void SaveRoundTripsAllSettingsAndReplacesExistingFile()
    {
        var service = new JsonSettingsService(SettingsPath);
        service.Save(new() { Volume = 40 });
        var expected = new PlayerSettings { Files = new[] { "a.mp4", "b.mp4" }, AllowDuplicates = true, Volume = 81 };
        service.Save(expected);
        var actual = service.Load();
        Assert.Equal(expected.Files, actual.Files);
        Assert.Equal(expected.AllowDuplicates, actual.AllowDuplicates);
        Assert.Equal(expected.Volume, actual.Volume);
        Assert.False(File.Exists(SettingsPath + ".tmp"));
    }

    [Fact]
    public void MalformedSettingsAreReportedToCallerWithoutOverwritingFile()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(SettingsPath, "{malformed");
        Assert.Throws<JsonException>(() => new JsonSettingsService(SettingsPath).Load());
        Assert.Equal("{malformed", File.ReadAllText(SettingsPath));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
