using System.Text.Json;
using SavienPlayer.Core;

namespace SavienPlayer.Infrastructure;

public sealed class JsonSettingsService(string filePath) : ISettingsService
{
    public PlayerSettings Load() => File.Exists(filePath)
        ? JsonSerializer.Deserialize<PlayerSettings>(File.ReadAllText(filePath)) ?? new PlayerSettings()
        : new PlayerSettings();

    public void Save(PlayerSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(filePath))!);
        var temporaryPath = filePath + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings));
            File.Move(temporaryPath, filePath, true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }
}
