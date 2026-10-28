using System.Diagnostics;
using System.IO;

namespace SavienPlayer.App;

internal sealed class PlaybackLogListener : TraceListener
{
    private readonly StreamWriter _writer;
    private bool _failed;
    public bool IsAvailable => !_failed;

    public PlaybackLogListener(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _writer = new StreamWriter(path, append: true) { AutoFlush = true };
    }

    public override void Write(string? message) => TryWrite(() => _writer.Write(message));

    public override void WriteLine(string? message) =>
        TryWrite(() => _writer.WriteLine($"{DateTimeOffset.UtcNow:O} {message}"));

    public override void Flush() => TryWrite(_writer.Flush);

    private void TryWrite(Action write)
    {
        if (_failed) return;
        try
        {
            write();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ObjectDisposedException)
        {
            // Logging must not turn a recoverable playback error into an application crash.
            _failed = true;
            Debug.WriteLine($"Playback log unavailable: {exception}");
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            try
            {
                _writer.Dispose();
            }
            catch (IOException exception)
            {
                Debug.WriteLine($"Playback log close failed: {exception}");
            }
        }
        base.Dispose(disposing);
    }
}
