using LibVLCSharp.Shared;
using SavienPlayer.Core;

namespace SavienPlayer.Infrastructure;

public sealed class VlcSubtitleService(MediaPlayer player, IMediaPlayerService playback) : ISubtitleService
{
    public bool IsLoaded { get; private set; }
    public string? CurrentSubtitlePath { get; private set; }
    public int OffsetMilliseconds { get; private set; }

    public bool TryLoad(string subtitlePath, out string? errorMessage)
    {
        errorMessage = null;
        if (!playback.IsLoaded)
        {
            errorMessage = "먼저 동영상을 열어주세요.";
            return false;
        }
        if (!File.Exists(subtitlePath))
        {
            errorMessage = "자막 파일을 찾을 수 없습니다.";
            return false;
        }
        var uri = new Uri(Path.GetFullPath(subtitlePath)).AbsoluteUri;
        if (!player.AddSlave(MediaSlaveType.Subtitle, uri, true))
        {
            errorMessage = "자막 로드에 실패했습니다.";
            return false;
        }
        IsLoaded = true;
        CurrentSubtitlePath = subtitlePath;
        player.SetSpuDelay(OffsetMilliseconds * 1000L);
        return true;
    }

    public void Clear()
    {
        if (IsLoaded) player.SetSpu(-1);
        IsLoaded = false;
        CurrentSubtitlePath = null;
        SetOffset(0);
    }

    public void SetOffset(int milliseconds)
    {
        OffsetMilliseconds = milliseconds;
        player.SetSpuDelay(milliseconds * 1000L);
    }
}
