namespace SavienPlayer.Core;

public interface ISubtitleService
{
    bool TryLoad(string subtitlePath, out string? errorMessage);
    void Clear();
    void SetOffset(int milliseconds);

    bool IsLoaded { get; }
    string? CurrentSubtitlePath { get; }
    int OffsetMilliseconds { get; }
}
