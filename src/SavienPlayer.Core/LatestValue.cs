namespace SavienPlayer.Core;

public sealed class LatestValue<T> where T : struct
{
    private readonly object _gate = new();
    private T? _value;

    public void Publish(T value)
    {
        lock (_gate) _value = value;
    }

    public bool TryTake(out T value)
    {
        lock (_gate)
        {
            value = _value.GetValueOrDefault();
            var available = _value.HasValue;
            _value = null;
            return available;
        }
    }
}
