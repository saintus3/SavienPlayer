using System.Diagnostics;
using System.Text.Json;
using SavienPlayer.Core;

namespace SavienPlayer.Infrastructure;

public sealed class PerformanceLogSession : IPerformanceLogSession
{
    private const long MaximumBytes = 16 * 1024 * 1024;
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Task _sampling;

    public string LogPath { get; }

    public PerformanceLogSession(string directory, Func<PlaybackPerformanceSnapshot> snapshot,
        Action<Exception> onError)
    {
        Directory.CreateDirectory(directory);
        // Keep disk usage bounded across sessions as well as within each session.
        foreach (var old in new DirectoryInfo(directory).GetFiles("performance-*.jsonl")
                     .OrderByDescending(file => file.LastWriteTimeUtc).Skip(9))
        {
            try { old.Delete(); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        LogPath = Path.Combine(directory, $"performance-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.jsonl");
        var stream = new FileStream(LogPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        _sampling = Task.Run(async () =>
        {
            try
            {
                using var writer = new StreamWriter(stream) { AutoFlush = true };
                using var process = Process.GetCurrentProcess();
                using var gpu = new WindowsGpuSampler();
                using var timer = new PeriodicTimer(Interval);
                var clock = Stopwatch.StartNew();
                var previousTime = clock.Elapsed.TotalSeconds;
                var previousCpu = process.TotalProcessorTime.TotalSeconds;
                gpu.Sample();
                writer.WriteLine(JsonSerializer.Serialize(new
                {
                    Type = "session", SchemaVersion = 1, StartedUtc = DateTimeOffset.UtcNow,
                    SampleIntervalSeconds = Interval.TotalSeconds,
                    LogicalProcessors = Environment.ProcessorCount,
                    OS = Environment.OSVersion.ToString(), Runtime = Environment.Version.ToString(),
                    Architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString()
                }));

                try
                {
                    while (await timer.WaitForNextTickAsync(_cancellation.Token).ConfigureAwait(false))
                    {
                        process.Refresh();
                        var elapsed = clock.Elapsed.TotalSeconds;
                        var cpu = process.TotalProcessorTime.TotalSeconds;
                        var cpuPercent = Math.Clamp(100 * (cpu - previousCpu) /
                            ((elapsed - previousTime) * Environment.ProcessorCount), 0, 100);
                        previousTime = elapsed;
                        previousCpu = cpu;
                        var playback = snapshot();
                        writer.WriteLine(JsonSerializer.Serialize(new
                        {
                            Type = "sample", TimestampUtc = DateTimeOffset.UtcNow,
                            ElapsedSeconds = elapsed, CpuPercent = cpuPercent,
                            GpuBusiestEnginePercent = gpu.Sample(),
                            WorkingSetBytes = process.WorkingSet64,
                            PrivateBytes = process.PrivateMemorySize64,
                            ManagedHeapBytes = GC.GetTotalMemory(false),
                            playback.MediaGeneration, State = playback.State.ToString(),
                            playback.DecodedVideo, playback.DisplayedPictures, playback.LostPictures
                        }));
                        if (stream.Position >= MaximumBytes)
                        {
                            writer.WriteLine("{\"Type\":\"end\",\"Reason\":\"size-limit\"}");
                            onError(new IOException("성능 로그 크기 제한에 도달했습니다."));
                            return;
                        }
                    }
                }
                catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
                {
                    writer.WriteLine("{\"Type\":\"end\",\"Reason\":\"stopped\"}");
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                onError(ex);
            }
        });
    }

    public void Dispose()
    {
        _cancellation.Cancel();
        _sampling.GetAwaiter().GetResult();
        _cancellation.Dispose();
    }
}
