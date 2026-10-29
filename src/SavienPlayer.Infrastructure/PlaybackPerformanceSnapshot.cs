using SavienPlayer.Core;

namespace SavienPlayer.Infrastructure;

public sealed record PlaybackPerformanceSnapshot(
    long MediaGeneration,
    PlaybackState State,
    int? DecodedVideo,
    int? DisplayedPictures,
    int? LostPictures);
