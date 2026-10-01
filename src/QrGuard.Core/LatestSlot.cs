namespace QrGuard.Core;

// One replaceable pending result; no Channel backlog or Dispatcher operation per frame.
public sealed class LatestSlot<T> : IDisposable where T : class, IDisposable
{
    private readonly object _gate = new();
    private T? _pending;
    private bool _closed;

    public void Publish(T value)
    {
        lock (_gate)
        {
            if (_closed) { value.Dispose(); return; }
            _pending?.Dispose();
            _pending = value;
        }
    }

    public T? Take()
    {
        lock (_gate) { T? value = _pending; _pending = null; return value; }
    }

    public void Dispose()
    {
        lock (_gate) { _closed = true; _pending?.Dispose(); _pending = null; }
    }
}
