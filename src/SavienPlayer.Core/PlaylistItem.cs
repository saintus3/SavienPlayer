using System.ComponentModel;

namespace SavienPlayer.Core;

public sealed class PlaylistItem : INotifyPropertyChanged
{
    private bool _isCurrent;

    internal PlaylistItem(string filePath)
    {
        FilePath = filePath;
    }

    public string FilePath { get; }
    public string DisplayName => Path.GetFileName(FilePath);

    public bool IsCurrent
    {
        get => _isCurrent;
        internal set
        {
            if (_isCurrent == value) return;
            _isCurrent = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsCurrent)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
