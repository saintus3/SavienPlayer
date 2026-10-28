namespace SavienPlayer.Core;

public sealed class PlayerSettings
{
    public const int CurrentVersion = 1;
    public int Version { get; set; } = CurrentVersion;
    public int Volume { get; set; } = 70;
    public double PlaybackRate { get; set; } = 1;
    public double WindowWidth { get; set; } = 1100;
    public double WindowHeight { get; set; } = 680;
    public bool IsMaximized { get; set; }
    public bool IsFullscreen { get; set; }
    public string? LastMediaPath { get; set; }
    public long LastPositionMilliseconds { get; set; }
    public int SubtitleOffsetMilliseconds { get; set; }
    public string SubtitleFontFamily { get; set; } = "Malgun Gothic";
    public double SubtitleFontSize { get; set; } = 32;
    public double SubtitleOutlineWidth { get; set; } = 2;
    public string SubtitlePosition { get; set; } = "Bottom";
    public bool SubtitleOriginalStyle { get; set; }

    public void Normalize()
    {
        Volume = Math.Clamp(Volume, 0, 100);
        SubtitleOffsetMilliseconds = Math.Clamp(SubtitleOffsetMilliseconds, -600000, 600000);
        SubtitleFontFamily = string.IsNullOrWhiteSpace(SubtitleFontFamily) || SubtitleFontFamily.Length > 100
            ? "Malgun Gothic" : SubtitleFontFamily;
        SubtitleFontSize = double.IsFinite(SubtitleFontSize) ? Math.Clamp(SubtitleFontSize, 12, 96) : 32;
        SubtitleOutlineWidth = double.IsFinite(SubtitleOutlineWidth) ? Math.Clamp(SubtitleOutlineWidth, 0, 6) : 2;
        if (SubtitlePosition is not ("Top" or "Center" or "Bottom")) SubtitlePosition = "Bottom";
        PlaybackRate = double.IsFinite(PlaybackRate) ? Math.Clamp(PlaybackRate, 0.25, 4) : 1;
        WindowWidth = double.IsFinite(WindowWidth) ? Math.Clamp(WindowWidth, 900, 16384) : 1100;
        WindowHeight = double.IsFinite(WindowHeight) ? Math.Clamp(WindowHeight, 560, 16384) : 680;
        LastPositionMilliseconds = Math.Clamp(LastPositionMilliseconds, 0,
            TimeSpan.MaxValue.Ticks / TimeSpan.TicksPerMillisecond);
        if (string.IsNullOrWhiteSpace(LastMediaPath))
        {
            LastMediaPath = null;
            LastPositionMilliseconds = 0;
        }
    }
}
