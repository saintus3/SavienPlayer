using System.Collections.ObjectModel;

namespace SavienPlayer.Core;

public sealed class Playlist
{
    private readonly ObservableCollection<PlaylistItem> _items = new();

    public Playlist()
    {
        Items = new ReadOnlyObservableCollection<PlaylistItem>(_items);
    }

    public ReadOnlyObservableCollection<PlaylistItem> Items { get; }
    public PlaylistItem? Current { get; private set; }
    public bool AllowDuplicates { get; set; }

    public PlaylistItem Add(string filePath)
    {
        var fullPath = Path.GetFullPath(filePath);
        if (!AllowDuplicates)
        {
            var existing = _items.FirstOrDefault(item =>
                StringComparer.OrdinalIgnoreCase.Equals(item.FilePath, fullPath));
            if (existing is not null) return existing;
        }

        var added = new PlaylistItem(fullPath);
        _items.Add(added);
        return added;
    }

    public void SetCurrent(PlaylistItem? item)
    {
        if (item is not null && !_items.Contains(item))
        {
            throw new ArgumentException("항목이 플레이리스트에 없습니다.", nameof(item));
        }

        if (Current is not null) Current.IsCurrent = false;
        Current = item;
        if (Current is not null) Current.IsCurrent = true;
    }

    public PlaylistItem? GetNext()
    {
        var index = Current is null ? -1 : _items.IndexOf(Current);
        return index + 1 < _items.Count ? _items[index + 1] : null;
    }

    public void Remove(IEnumerable<PlaylistItem> items)
    {
        var removed = items.ToHashSet();
        if (Current is not null && removed.Contains(Current))
        {
            var successor = _items.Skip(_items.IndexOf(Current) + 1)
                .FirstOrDefault(item => !removed.Contains(item));
            SetCurrent(successor);
        }

        foreach (var item in removed) _items.Remove(item);
    }

    public void Move(PlaylistItem item, int index)
    {
        var oldIndex = _items.IndexOf(item);
        if (oldIndex < 0) throw new ArgumentException("항목이 플레이리스트에 없습니다.", nameof(item));
        if (index < 0 || index >= _items.Count) throw new ArgumentOutOfRangeException(nameof(index));
        _items.Move(oldIndex, index);
    }
}
