namespace QrGuard.Core;

public enum CoverageState { Starting, Active, Degraded, Stopped, Unavailable }
public enum EventCode { PolicyAccepted, PolicyRejected, CoverageChanged, Opened, Copied, ActionRefused, WarningRequired }
public readonly record struct DiagnosticEvent(EventCode Code, long MonotonicTicks);
public interface IEventSink { void Record(DiagnosticEvent value); }

// No free-form message, URL, payload hash, host, rule reason or exception can enter this sink.
public sealed class BoundedDiagnostics : IEventSink
{
    public const int Capacity = 128;
    private readonly Queue<DiagnosticEvent> _events = new();
    public long Dropped { get; private set; }
    public IReadOnlyList<DiagnosticEvent> Snapshot { get { lock (_events) return _events.ToArray(); } }
    public void Record(DiagnosticEvent value)
    {
        lock (_events)
        {
            if (_events.Count == Capacity) { _events.Dequeue(); Dropped++; }
            _events.Enqueue(value);
        }
    }
}

public interface IClock { long MonotonicTicks { get; } long TicksPerSecond { get; } DateTimeOffset UtcNow { get; } }
public sealed class SystemClock : IClock
{
    public long MonotonicTicks => System.Diagnostics.Stopwatch.GetTimestamp();
    public long TicksPerSecond => System.Diagnostics.Stopwatch.Frequency;
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

public interface IReputationProvider { bool Available { get; } }
public sealed class OfflineReputationProvider : IReputationProvider { public bool Available => false; }
