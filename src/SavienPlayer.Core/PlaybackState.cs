namespace SavienPlayer.Core;

public enum PlaybackState
{
    None,
    Opening,
    Playing,
    Paused,
    Stopped,
    Buffering,
    Ended,
    Error
}
