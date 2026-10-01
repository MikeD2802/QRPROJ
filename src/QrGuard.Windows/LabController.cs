using System.Diagnostics;
using QrGuard.Core;
using QrGuard.Decoder;

namespace QrGuard.Windows;

internal sealed class FrameResult(PixelFrame frame, DecodeBatch batch, double decodeMs) : IDisposable
{
    public FrameIdentity Identity { get; } = frame.Identity;
    public int Width { get; } = frame.Width;
    public int Height { get; } = frame.Height;
    public DecodeBatch Batch { get; } = batch;
    public double DecodeMs { get; } = decodeMs;
    public void Dispose() => Batch.Dispose();
}

internal sealed class LabController : IDisposable
{
    private LatestSlot<FrameResult> _results = new();
    private CancellationTokenSource? _cancel;
    private Task? _worker;
    private TrackStore? _tracks;
    private MaskRenderer? _renderer;
    private string _fault = "";
    private int _generation;
    private long _lastFreshTicks, _startedTicks, _endedTicks, _lastResourceTicks;
    private TimeSpan _cpuAtStart, _cpuAtEnd;
    private readonly List<double> _latencies = [];
    private long _measurements, _peakWorkingSet, _decodeFrames;
    private double _decodeTotalMs;
    private int _width, _height;
    public string Status { get; private set; } = "Stopped · P0 lab only";
    public bool Running => _cancel is not null;
    public bool CanStart => _worker is null || _worker.IsCompleted;

    public void Start(MonitorTarget monitor)
    {
        if (Running || !CanStart) throw new InvalidOperationException("capture_worker_still_stopping");
        _results.Dispose(); _results = new();
        _tracks?.Dispose(); _renderer?.Dispose(); _cancel?.Dispose();
        Guid session = Guid.NewGuid();
        int generation = ++_generation;
        _tracks = new(session, monitor.Index, generation); _renderer = new(monitor);
        _width = monitor.Width; _height = monitor.Height;
        _cancel = new();
        _fault = ""; _latencies.Clear(); _measurements = 0; _peakWorkingSet = 0;
        _decodeFrames = 0; _decodeTotalMs = 0;
        _startedTicks = Stopwatch.GetTimestamp(); _lastFreshTicks = _startedTicks; _endedTicks = 0; _lastResourceTicks = 0;
        using (Process process = Process.GetCurrentProcess()) _cpuAtStart = process.TotalProcessorTime;
        Status = "Starting capture · physical display verification pending";
        CancellationToken token = _cancel.Token;
        LatestSlot<FrameResult> results = _results;
        _worker = Task.Run(() =>
        {
            try
            {
                // Native capture/context use remains on one thread: the loop uses a synchronous cancellation wait.
                using var apartment = new CaptureInterop.MtaApartment();
                using var source = new MonitorCapture(monitor, session, generation);
                using var decoder = new ZxingQrDecoder();
                while (!token.IsCancellationRequested)
                {
                    long start = Stopwatch.GetTimestamp();
                    if (source.TryCapture(out PixelFrame? captured) && captured is not null)
                    {
                        using (captured)
                        {
                            long decodeStart = Stopwatch.GetTimestamp();
                            DecodeBatch batch = decoder.Decode(captured);
                            results.Publish(new(captured, batch, Stopwatch.GetElapsedTime(decodeStart).TotalMilliseconds));
                        }
                    }
                    int wait = Math.Max(1, Limits.DiscoveryIntervalMs - (int)Stopwatch.GetElapsedTime(start).TotalMilliseconds);
                    if (token.WaitHandle.WaitOne(wait)) break;
                }
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // Fixed categories only. Native exceptions can contain arbitrary input; do not persist their messages.
                Volatile.Write(ref _fault, exception is NotSupportedException ? "Unsupported capture/display" : "Capture/decoder failed");
            }
            finally { results.Dispose(); }
        });
    }

    public void Tick()
    {
        if (!Running) return;
        long now = Stopwatch.GetTimestamp();
        string fault = Volatile.Read(ref _fault);
        if (fault.Length != 0) { _tracks?.Clear(); _renderer?.Clear(); Status = $"Degraded · {fault} · Stop before retry"; return; }
        using FrameResult? result = _results.Take();
        if (result is not null && _tracks is not null && _renderer is not null)
        {
            try
            {
                if (_tracks.Apply(result.Identity, result.Batch, result.Width, result.Height, now, Stopwatch.Frequency))
                {
                    _renderer.Render(_tracks.Snapshot, result.Width, result.Height);
                    _lastFreshTicks = result.Identity.CapturedTicks;
                    if (_tracks.Snapshot.Count > 0)
                    {
                        double latency = Stopwatch.GetElapsedTime(result.Identity.CapturedTicks).TotalMilliseconds;
                        if (_latencies.Count == 2048) _latencies.RemoveAt(0);
                        _latencies.Add(latency); _measurements++;
                    }
                    _decodeFrames++; _decodeTotalMs += result.DecodeMs;
                    Status = result.Batch.UnlocatedCount > 0 ? "Degraded · QR geometry unavailable" :
                        result.Batch.Saturated ? "Degraded · more than 16 QR observations" :
                        $"Capture running · {_tracks.Snapshot.Count} masks · {_tracks.PayloadChanges} payload changes · P0 unverified";
                }
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                _tracks.Clear(); _renderer.Clear();
                _cancel!.Cancel(); Volatile.Write(ref _fault, "Mask/tracking failed");
                Status = "Degraded · Mask/tracking failed · Stop before retry";
            }
        }
        if (_tracks?.Expire(now, Stopwatch.Frequency) == true) _renderer?.Clear();
        if (Stopwatch.GetElapsedTime(_lastFreshTicks, now).TotalMilliseconds > Limits.MaskLifetimeMs)
            Status = "Degraded · no fresh accepted observation · masks retired";
        if (Stopwatch.GetElapsedTime(_lastResourceTicks, now).TotalSeconds >= 1)
        {
            using Process process = Process.GetCurrentProcess();
            process.Refresh(); _peakWorkingSet = Math.Max(_peakWorkingSet, process.WorkingSet64);
            _lastResourceTicks = now;
        }
    }

    public async Task StopAsync()
    {
        if (Running)
        {
            _endedTicks = Stopwatch.GetTimestamp();
            using Process process = Process.GetCurrentProcess(); _cpuAtEnd = process.TotalProcessorTime;
        }
        _cancel?.Cancel(); _renderer?.Clear(); _tracks?.Clear(); _results.Dispose();
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
            schema_version = 1, phase = "P0", runtime = Environment.Version.ToString(),
            os_version = Environment.OSVersion.Version.ToString(), logical_processors = Environment.ProcessorCount,
            width = _width, height = _height, interval_seconds = seconds,
            cpu_percent_total_system_capacity = cpu, peak_working_set_bytes = _peakWorkingSet,
            frames_accepted = _tracks?.AcceptedFrames ?? 0, frames_rejected = _tracks?.RejectedFrames ?? 0,
            payload_changes = _tracks?.PayloadChanges ?? 0, tracks_retired = _tracks?.RetiredTracks ?? 0,
            decode_average_ms = _decodeFrames == 0 ? 0 : _decodeTotalMs / _decodeFrames,
            capture_timestamp_to_mask_api_p95_ms = ordered.Length == 0 ? (double?)null : ordered[(int)Math.Ceiling(ordered.Length * 0.95) - 1],
            latency_samples_retained = ordered.Length, latency_samples_total = _measurements,
            measurement_scope = "Process CPU includes the lab UI/fixture. Capture timestamp to mask API is not render-to-visible or phone evidence.",
            physical_phone_verified = false, desktop_sharing_verified = false, window_sharing_verified = false
        };
    }

    public void Dispose()
    {
        _cancel?.Cancel(); _results.Dispose(); _renderer?.Dispose(); _tracks?.Dispose();
        // On process exit Windows destroys HWNDs; never await a potentially stuck native call in the close handler.
    }
}
