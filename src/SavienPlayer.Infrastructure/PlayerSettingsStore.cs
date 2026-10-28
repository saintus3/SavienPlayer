using System.Diagnostics;
using System.Text.Json;
using SavienPlayer.Core;

namespace SavienPlayer.Infrastructure;

public sealed class PlayerSettingsStore
{
    private readonly string _path;
    private bool _canSave = true;

    public PlayerSettingsStore(string path) => _path = path;

    public PlayerSettings Load()
    {
        try
        {
            using var stream = File.OpenRead(_path);
            if (stream.Length > 1024 * 1024)
                throw new JsonException("Settings exceed the size limit.");
            using var document = JsonDocument.Parse(stream);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new JsonException("Settings must be an object.");
            // Inspect the envelope before binding properties whose types may change in a future schema.
            if (document.RootElement.TryGetProperty(nameof(PlayerSettings.Version), out var version) &&
                (version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var number) ||
                 number != PlayerSettings.CurrentVersion))
            {
                _canSave = false;
                Trace.TraceWarning("Unsupported settings version; using defaults without overwriting.");
                return new PlayerSettings();
            }
            var settings = document.RootElement.Deserialize<PlayerSettings>()!;
            settings.Normalize();
            return settings;
        }
        catch (FileNotFoundException) { return new PlayerSettings(); }
        catch (DirectoryNotFoundException) { return new PlayerSettings(); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            System.Security.SecurityException or JsonException)
        {
            Trace.TraceWarning($"Settings load failed; using defaults: {exception}");
            return new PlayerSettings();
        }
    }

    public void Save(PlayerSettings settings)
    {
        if (!_canSave) return;
        var temporaryPath = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            settings.Normalize();
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, settings, new JsonSerializerOptions { WriteIndented = true });
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporaryPath, _path, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            System.Security.SecurityException)
        {
            Trace.TraceWarning($"Settings save failed; playback will continue: {exception}");
        }
        finally
        {
            try { File.Delete(temporaryPath); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                Trace.TraceWarning($"Settings temporary-file cleanup failed: {exception}");
            }
        }
    }
}
