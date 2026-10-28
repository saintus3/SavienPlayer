using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace SavienPlayer.Infrastructure;

public sealed class SubtitleDocument
{
    private sealed record Cue(long Start, long End, string Text);
    private readonly Cue[] _cues;
    private readonly long[] _maximumEnd;
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

    private SubtitleDocument(IEnumerable<Cue> cues)
    {
        _cues = cues.OrderBy(c => c.Start).ToArray();
        _maximumEnd = new long[_cues.Length];
        for (var i = 0; i < _cues.Length; i++)
            _maximumEnd[i] = Math.Max(i == 0 ? 0 : _maximumEnd[i - 1], _cues[i].End);
    }

    public static string? FindSidecar(string mediaPath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(mediaPath))!;
        var basename = Path.GetFileNameWithoutExtension(mediaPath);
        return Directory.EnumerateFiles(directory)
            .Where(path => string.Equals(Path.GetFileNameWithoutExtension(path), basename, StringComparison.OrdinalIgnoreCase))
            .Where(path => Path.GetExtension(path).Equals(".srt", StringComparison.OrdinalIgnoreCase) ||
                           Path.GetExtension(path).Equals(".ass", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => Path.GetExtension(path).Equals(".srt", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(path => path, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    public static SubtitleDocument Load(string path)
    {
        using var stream = File.OpenRead(path);
        if (stream.Length > 8 * 1024 * 1024) throw new InvalidDataException("Subtitle exceeds 8 MiB.");
        using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
        var content = reader.ReadToEnd().Replace("\r\n", "\n").Replace('\r', '\n');
        var extension = Path.GetExtension(path);
        var ass = extension.Equals(".ass", StringComparison.OrdinalIgnoreCase) ||
                  extension.Equals(".ssa", StringComparison.OrdinalIgnoreCase);
        var cues = new List<Cue>();
        if (ass)
        {
            var inEvents = false;
            string[]? fields = null;
            foreach (var line in content.Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith('['))
                {
                    inEvents = trimmed.Equals("[Events]", StringComparison.OrdinalIgnoreCase);
                    continue;
                }
                if (!inEvents) continue;
                if (trimmed.StartsWith("Format:", StringComparison.OrdinalIgnoreCase))
                    fields = trimmed[7..].Split(',').Select(f => f.Trim()).ToArray();
                if (!trimmed.StartsWith("Dialogue:", StringComparison.OrdinalIgnoreCase) || fields == null) continue;
                var values = trimmed[9..].TrimStart().Split(',', fields.Length);
                var start = Array.FindIndex(fields, f => f.Equals("Start", StringComparison.OrdinalIgnoreCase));
                var end = Array.FindIndex(fields, f => f.Equals("End", StringComparison.OrdinalIgnoreCase));
                var text = Array.FindIndex(fields, f => f.Equals("Text", StringComparison.OrdinalIgnoreCase));
                if (start < 0 || end < 0 || text != fields.Length - 1 || values.Length != fields.Length) continue;
                AddCue(values[start], values[end], values[text], true, cues);
            }
        }
        else if (extension.Equals(".srt", StringComparison.OrdinalIgnoreCase) ||
                 extension.Equals(".vtt", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var block in Regex.Split(content, @"\n[ \t]*\n", RegexOptions.None, RegexTimeout))
            {
                var lines = block.Trim().Split('\n');
                var timing = Array.FindIndex(lines, line => line.Contains("-->", StringComparison.Ordinal));
                if (timing < 0 || timing + 1 >= lines.Length) continue;
                var range = lines[timing].Split("-->", 2);
                AddCue(range[0].Trim(), range[1].Trim().Split(' ')[0],
                    string.Join("\n", lines.Skip(timing + 1)), false, cues);
            }
        }
        else throw new InvalidDataException("Unsupported subtitle extension.");
        if (cues.Count == 0) throw new InvalidDataException("No valid subtitle cues.");
        return new SubtitleDocument(cues);
    }

    private static void AddCue(string start, string end, string text, bool ass, List<Cue> cues)
    {
        if (!TryTime(start, out var from) || !TryTime(end, out var to) || to <= from) return;
        if (ass)
        {
            text = Regex.Replace(text, @"\{[^}]*\}", "", RegexOptions.None, RegexTimeout)
                .Replace("\\N", "\n").Replace("\\n", "\n").Replace("\\h", "\u00a0");
        }
        else
        {
            text = WebUtility.HtmlDecode(Regex.Replace(text, "<[^>]*>", "", RegexOptions.None, RegexTimeout));
        }
        if (!string.IsNullOrWhiteSpace(text)) cues.Add(new Cue(from, to, text));
    }

    private static bool TryTime(string text, out long milliseconds)
    {
        milliseconds = 0;
        var parts = text.Trim().Replace(',', '.').Split(':');
        if (parts.Length is not (2 or 3) ||
            !double.TryParse(parts[^1], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var seconds) ||
            !double.IsFinite(seconds) || seconds is < 0 or >= 60 ||
            !int.TryParse(parts[^2], out var minutes) || minutes is < 0 or >= 60)
            return false;
        var hours = 0;
        if (parts.Length == 3 && (!int.TryParse(parts[0], out hours) || hours < 0)) return false;
        var total = ((long)hours * 3600 + minutes * 60) * 1000 + seconds * 1000;
        if (total > TimeSpan.MaxValue.Ticks / TimeSpan.TicksPerMillisecond) return false;
        milliseconds = (long)total;
        return true;
    }

    public string GetText(TimeSpan position, int offsetMilliseconds)
    {
        var time = position.Ticks / TimeSpan.TicksPerMillisecond - offsetMilliseconds;
        var low = 0;
        var high = _cues.Length;
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (_cues[middle].Start <= time) low = middle + 1;
            else high = middle;
        }
        var active = new List<string>();
        for (var i = low - 1; i >= 0 && _maximumEnd[i] > time; i--)
            if (_cues[i].End > time) active.Add(_cues[i].Text);
        active.Reverse();
        return string.Join("\n", active);
    }
}
