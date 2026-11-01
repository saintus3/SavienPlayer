using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SavienPlayer.App;

public sealed class PlaylistItem : INotifyPropertyChanged
{
    private string _durationText = "--:--";

    public required string FilePath { get; init; }
    public required string FileName { get; init; }

    public string DurationText
    {
        get => _durationText;
        set
        {
            if (_durationText == value)
            {
                return;
            }

            _durationText = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public static string FormatDuration(TimeSpan? duration)
    {
        if (duration is null || duration.Value <= TimeSpan.Zero)
        {
            return "--:--";
        }

        var value = duration.Value;
        return value.TotalHours >= 1
            ? $"{(int)value.TotalHours:D2}:{value.Minutes:D2}:{value.Seconds:D2}"
            : $"{value.Minutes:D2}:{value.Seconds:D2}";
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
