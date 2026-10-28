namespace SavienPlayer.Core;

public interface ISubtitleService
{
    bool TryLoad(string subtitlePath, out string? errorMessage);
    void Clear();
    void SetOffset(int milliseconds);
    string GetText(TimeSpan position);
    bool UseOriginalStyle(bool enabled);

    bool IsLoaded { get; }
    bool UsesOriginalStyle { get; }
    string? CurrentSubtitlePath { get; }
    int OffsetMilliseconds { get; }
}
