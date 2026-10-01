using System.Diagnostics;
using QrGuard.Core;
using QrGuard.Decoder;

namespace QrGuard.Windows;

internal sealed class FrameResult(PixelFrame frame, DecodeBatch batch, double decodeMs, bool reused) : IDisposable
{
    public FrameIdentity Identity { get; } = frame.Identity;
    public int Width { get; } = frame.Width;
    public int Height { get; } = frame.Height;
    public DecodeBatch Batch { get; } = batch;
    public double DecodeMs { get; } = decodeMs;
    public bool Reused { get; } = reused;
    public void Dispose() => Batch.Dispose();
}

internal sealed class LabController : IDisposable
{
    private readonly IClock _clock = new SystemClock();
    private readonly BoundedDiagnostics _diagnostics = new();
    private readonly PolicyEvaluator _evaluator = new();
    private readonly LocalPolicyProvider _policies;
    private readonly LocalPolicyFile _policyFile;
    private ActionController? _actions;
    public event Action<long>? DetailsRequested;
    public CoverageState Coverage { get; private set; } = CoverageState.Stopped;
    public string PolicyStatus => _policies.LastFailure != PolicyFailure.None ? "Invalid/unavailable edit; last validated policy retained (or built-in baseline)."
        : _policies.Current.IsBaseline ? "Built-in mask-and-warn baseline" : $"Local policy loaded · revision {_policies.Current.Revision}";
    private LatestSlot<FrameResult> _results = new();
    private CancellationTokenSource? _cancel;
    private Task? _worker;
    private TrackStore? _tracks;
    private MaskRenderer? _renderer;
    private BoundedDiscovery? _discovery;
    private string _fault = "";
    private int _generation;
    private long _lastFreshTicks, _startedTicks, _endedTicks, _lastResourceTicks, _lastPolicyReadTicks;
    private TimeSpan _cpuAtStart, _cpuAtEnd;
    private readonly List<double> _latencies = [];
    private long _measurements, _peakWorkingSet, _decodeFrames, _coalescedFrames;
    private double _decodeTotalMs;
    private int _width, _height;
    private MonitorTarget? _monitor;
    public string Status { get; private set; } = "Stopped · offline MVP · physical-display acceptance pending";
    public bool Running => _cancel is not null;
    public bool CanStart => _worker is null || _worker.IsCompleted;
    public IReadOnlyList<TrackSnapshot> Masks => _tracks?.Snapshot ?? [];

    public LabController()
    {
        _policies = new(_diagnostics, _clock); _policyFile = new(_policies);
        string demo = System.IO.Path.Combine(AppContext.BaseDirectory, "policy.example.json");
        _policyFile.Select(demo);
    }
    public bool LoadPolicy(string path) => _policyFile.Select(path);
    public DetailsView? Details(long id) => Running ? _actions?.Details(id) : null;
    public bool IsCurrent(ActionTicket ticket) => Running && _actions?.IsCurrent(ticket) == true;
    public ActionOutcome Open(ActionTicket ticket, bool warningConfirmed = false) => CanAct() ? _actions?.Open(ticket, warningConfirmed) ?? ActionOutcome.ChangedOrStale : ActionOutcome.ChangedOrStale;
    public ActionOutcome Copy(ActionTicket ticket) => CanAct() ? _actions?.Copy(ticket) ?? ActionOutcome.ChangedOrStale : ActionOutcome.ChangedOrStale;
    public void CancelWarning(ActionTicket ticket) => _actions?.CancelWarning(ticket);
    private bool CanAct()
    {
        if (!Running || _cancel!.IsCancellationRequested || Volatile.Read(ref _fault).Length != 0 || _monitor is null) return false;
        // An edit between timer ticks or while a warning is open must be visible to this action.
        _policyFile.Reload();
        var info = new Native.MonitorInfo { Size = System.Runtime.InteropServices.Marshal.SizeOf<Native.MonitorInfo>() };
        if (Native.GetMonitorInfo(_monitor.Handle, ref info) && info.Monitor.Equals(_monitor.Bounds)) return true;
        FailMasking("Display context changed"); return false;
    }
    private void SetCoverage(CoverageState state)
    {
        if (Coverage == state) return;
        Coverage = state; _diagnostics.Record(new(EventCode.CoverageChanged, _clock.MonotonicTicks));
    }

    public void Start(MonitorTarget monitor)
    {
        if (Running || !CanStart) throw new InvalidOperationException("capture_worker_still_stopping");
        _results.Dispose(); _results = new();
        _tracks?.Dispose(); _renderer?.Dispose(); _discovery?.Dispose(); _cancel?.Dispose();
        _discovery = new();
        Guid session = Guid.NewGuid();
        int generation = ++_generation;
        _tracks = new(session, monitor.Index, generation);
        _renderer = new(monitor, id => DetailsRequested?.Invoke(id));
        _actions = new(_tracks, _policies, _evaluator, _clock, new BrowserLauncher(), new UserClipboard(), _diagnostics);
        _width = monitor.Width; _height = monitor.Height;
        _monitor = monitor;
        _cancel = new();
        _fault = ""; _latencies.Clear(); _measurements = 0; _peakWorkingSet = 0;
        _decodeFrames = 0; _decodeTotalMs = 0; _coalescedFrames = 0;
        _startedTicks = Stopwatch.GetTimestamp(); _lastFreshTicks = _startedTicks; _endedTicks = 0; _lastResourceTicks = 0;
        using (Process process = Process.GetCurrentProcess()) _cpuAtStart = process.TotalProcessorTime;
        Status = "Starting capture · physical display verification pending";
        SetCoverage(CoverageState.Starting);
        CancellationToken token = _cancel.Token;
        LatestSlot<FrameResult> results = _results;
        BoundedDiscovery discovery = _discovery;
        _worker = Task.Run(() =>
        {
            try
            {
                // Native capture/context use remains on one thread: the loop uses a synchronous cancellation wait.
                using var apartment = new CaptureInterop.MtaApartment();
                using var source = new MonitorCapture(monitor, session, generation);
                using var decoder = new ZxingQrDecoder();
                using var ownedDiscovery = discovery;
                while (!token.IsCancellationRequested)
                {
                    long start = Stopwatch.GetTimestamp();
                    if (source.TryCapture(out PixelFrame? captured) && captured is not null)
                    {
                        using (captured)
                        {
                            long decodeStart = Stopwatch.GetTimestamp();
                            DecodeBatch batch = discovery.Process(captured, decoder, Stopwatch.GetTimestamp(), Stopwatch.Frequency, out bool reused);
                            results.Publish(new(captured, batch, Stopwatch.GetElapsedTime(decodeStart).TotalMilliseconds, reused));
                        }
                    }
                    int wait = Math.Max(1, Limits.DiscoveryIntervalMs - (int)Stopwatch.GetElapsedTime(start).TotalMilliseconds);
                    if (token.WaitHandle.WaitOne(wait)) break;
                }
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // Fixed categories only. Native exceptions can contain arbitrary input; do not persist their messages.
                if (!token.IsCancellationRequested)
                    Interlocked.CompareExchange(ref _fault, exception is NotSupportedException ? "Unsupported capture/display" : "Capture/decoder failed", "");
            }
            finally { discovery.Dispose(); results.Dispose(); }
        });
    }

    public void Tick()
    {
        if (!Running) return;
        long now = Stopwatch.GetTimestamp();
        if (Stopwatch.GetElapsedTime(_lastPolicyReadTicks, now).TotalSeconds >= 1)
        { _policyFile.Reload(); _lastPolicyReadTicks = now; }
        string fault = Volatile.Read(ref _fault);
        if (fault.Length != 0) { _actions = null; _tracks?.Clear(); _renderer?.Clear(); SetCoverage(CoverageState.Unavailable); Status = $"Unavailable · {fault} · Stop before retry"; return; }
        using FrameResult? result = _results.Take();
        if (result is not null && _tracks is not null && _renderer is not null)
        {
            try
            {
                if (_tracks.Apply(result.Identity, result.Batch, result.Width, result.Height, now, Stopwatch.Frequency))
                {
                    _renderer.Render(_tracks.Snapshot, result.Width, result.Height);
                    // Opaque windows already exist before any classification/policy work.
                    if (_tracks.Evaluate(_evaluator, _policies.Current, _clock.UtcNow))
                        _renderer.Render(_tracks.Snapshot, result.Width, result.Height);
                    _lastFreshTicks = result.Identity.CapturedTicks;
                    if (_tracks.Snapshot.Count > 0)
                    {
                        double latency = Stopwatch.GetElapsedTime(result.Identity.CapturedTicks).TotalMilliseconds;
                        if (_latencies.Count == 2048) _latencies.RemoveAt(0);
                        _latencies.Add(latency); _measurements++;
                    }
                    if (result.Reused) _coalescedFrames++;
                    else { _decodeFrames++; _decodeTotalMs += result.DecodeMs; }
                    SetCoverage(result.Batch.UnlocatedCount > 0 || result.Batch.Saturated ? CoverageState.Degraded : CoverageState.Active);
                    Status = result.Batch.UnlocatedCount > 0 ? "Degraded · QR geometry unavailable" :
                        result.Batch.Saturated ? "Degraded · more than 16 QR observations" :
                        $"Active coverage · {_tracks.Snapshot.Count} masks · {_tracks.PayloadChanges} payload changes · physical-display acceptance pending";
                }
                else { SetCoverage(CoverageState.Degraded); Status = "Degraded · obsolete capture result rejected"; }
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                FailMasking("Mask/tracking failed");
            }
        }
        if (_tracks?.Expire(now, Stopwatch.Frequency) == true) _renderer?.Clear();
        if (_tracks is not null && _renderer is not null)
        {
            try
            {
                if (_tracks.Evaluate(_evaluator, _policies.Current, _clock.UtcNow))
                    _renderer.Render(_tracks.Snapshot, _width, _height);
            }
            catch (Exception error) when (error is not OutOfMemoryException) { FailMasking("Mask/policy update failed"); }
        }
        if (Volatile.Read(ref _fault).Length == 0 && Stopwatch.GetElapsedTime(_lastFreshTicks, now).TotalMilliseconds > Limits.MaskLifetimeMs)
        { SetCoverage(CoverageState.Degraded); Status = "Degraded · no fresh accepted observation · masks retired"; }
        else if (Volatile.Read(ref _fault).Length == 0 && Stopwatch.GetElapsedTime(_lastFreshTicks, now).TotalMilliseconds > Limits.MaxFrameAgeMs)
        { SetCoverage(CoverageState.Degraded); Status = "Degraded · observations stale · actions disabled · retirement pending"; }
        if (Stopwatch.GetElapsedTime(_lastResourceTicks, now).TotalSeconds >= 1)
        {
            using Process process = Process.GetCurrentProcess();
            process.Refresh(); _peakWorkingSet = Math.Max(_peakWorkingSet, process.WorkingSet64);
            _lastResourceTicks = now;
        }
    }

    private void FailMasking(string fixedCategory)
    {
        _actions = null; _tracks?.Clear(); _renderer?.Clear(); _cancel?.Cancel(); _discovery?.Dispose();
        Volatile.Write(ref _fault, fixedCategory); SetCoverage(CoverageState.Unavailable);
        Status = $"Unavailable · {fixedCategory} · Stop before retry";
    }

    public async Task StopAsync()
    {
        if (Running)
        {
            _endedTicks = Stopwatch.GetTimestamp();
            using Process process = Process.GetCurrentProcess(); _cpuAtEnd = process.TotalProcessorTime;
        }
        _actions = null; _cancel?.Cancel(); _renderer?.Clear(); _tracks?.Clear(); _discovery?.Dispose(); _results.Dispose();
        SetCoverage(CoverageState.Stopped);
        CancellationTokenSource? cancelled = _cancel; _cancel = null;
        Task? worker = _worker;
        if (worker is not null)
        {
            await Task.WhenAny(worker, Task.Delay(1000));
            if (worker.IsCompleted) { await worker; cancelled?.Dispose(); }
            else _ = worker.ContinueWith(_ => cancelled?.Dispose(), TaskScheduler.Default);
        }
        Status = CanStart ? "Stopped · masks removed" : "Stopped UI · native worker pending · restart unavailable";
    }

    // Explicit export only, a fixed schema containing no pixels, payloads, fingerprints, URLs, or window titles.
    public object MeasurementReport()
    {
        using Process process = Process.GetCurrentProcess(); process.Refresh();
        double seconds = _startedTicks == 0 ? 0 : Stopwatch.GetElapsedTime(_startedTicks, _endedTicks == 0 ? Stopwatch.GetTimestamp() : _endedTicks).TotalSeconds;
        TimeSpan cpuEnd = _endedTicks == 0 ? process.TotalProcessorTime : _cpuAtEnd;
        double cpu = seconds > 0 ? (cpuEnd - _cpuAtStart).TotalSeconds / seconds / Environment.ProcessorCount * 100 : 0;
        double[] ordered = _latencies.Order().ToArray();
        return new
        {
            schema_version = 1, phase = "P1", runtime = Environment.Version.ToString(),
            os_version = Environment.OSVersion.Version.ToString(), logical_processors = Environment.ProcessorCount,
            width = _width, height = _height, interval_seconds = seconds,
            cpu_percent_total_system_capacity = cpu, peak_working_set_bytes = _peakWorkingSet,
            frames_accepted = _tracks?.AcceptedFrames ?? 0, frames_rejected = _tracks?.RejectedFrames ?? 0,
            payload_changes = _tracks?.PayloadChanges ?? 0, tracks_retired = _tracks?.RetiredTracks ?? 0,
            decode_average_ms = _decodeFrames == 0 ? 0 : _decodeTotalMs / _decodeFrames,
            full_decode_passes = _decodeFrames, coalesced_frames = _coalescedFrames,
            coverage = Coverage.ToString(), diagnostics = _diagnostics.Snapshot, diagnostics_dropped = _diagnostics.Dropped,
            capture_timestamp_to_mask_api_p95_ms = ordered.Length == 0 ? (double?)null : ordered[(int)Math.Ceiling(ordered.Length * 0.95) - 1],
            latency_samples_retained = ordered.Length, latency_samples_total = _measurements,
            measurement_scope = "Process CPU includes the lab UI/fixture. Capture timestamp to mask API is not render-to-visible or phone evidence.",
            physical_phone_verified = false, desktop_sharing_verified = false, window_sharing_verified = false
        };
    }

    public void Dispose()
    {
        _actions = null; _cancel?.Cancel(); _results.Dispose(); _discovery?.Dispose(); _renderer?.Dispose(); _tracks?.Dispose(); SetCoverage(CoverageState.Stopped);
        // On process exit Windows destroys HWNDs; never await a potentially stuck native call in the close handler.
    }
}
