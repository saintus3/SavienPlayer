using System.IO;
using System.Text.Json;
using SavienPlayer.Core;

namespace SavienPlayer.App;

internal static class PlaylistStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SavienPlayer", "playlist.json");

    public static void Load(Playlist playlist)
    {
        if (!File.Exists(FilePath)) return;
        var saved = JsonSerializer.Deserialize<SavedPlaylist>(File.ReadAllText(FilePath));
        if (saved is null) return;
        var files = (saved.Files ?? Array.Empty<string>())
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath).ToArray();

        // Restore existing duplicates even when subsequent additions are blocked.
        playlist.AllowDuplicates = true;
        foreach (var path in files) playlist.Add(path);
        playlist.AllowDuplicates = saved.AllowDuplicates;
    }

    public static void Save(Playlist playlist)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var saved = new SavedPlaylist
        {
            AllowDuplicates = playlist.AllowDuplicates,
            Files = playlist.Items.Select(item => item.FilePath).ToArray()
        };
        var temporaryPath = FilePath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(saved));
        File.Move(temporaryPath, FilePath, true);
    }

    private sealed class SavedPlaylist
    {
        public bool AllowDuplicates { get; set; }
        public string[]? Files { get; set; }
    }
}
