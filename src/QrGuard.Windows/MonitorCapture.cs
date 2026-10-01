using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using QrGuard.Core;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;

namespace QrGuard.Windows;

internal sealed class MonitorCapture : ICaptureSource
{
    private readonly MonitorTarget _monitor;
    private readonly Guid _sessionId;
    private readonly int _generation;
    private readonly ID3D11Device _device;
    private readonly ID3D11DeviceContext _context;
    private readonly IDirect3DDevice _winrtDevice;
    private readonly GraphicsCaptureItem _item;
    private readonly Direct3D11CaptureFramePool _pool;
    private readonly GraphicsCaptureSession _session;
    private ID3D11Texture2D? _staging;
    private volatile bool _closed;
    private long _frameId;

    public MonitorCapture(MonitorTarget monitor, Guid sessionId, int generation)
    {
        _monitor = monitor; _sessionId = sessionId; _generation = generation;
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) throw new NotSupportedException("windows_11_required");
        if (!GraphicsCaptureSession.IsSupported()) throw new NotSupportedException("capture_unsupported");
        if (monitor.Width is <= 0 or > Limits.MaxWidth || monitor.Height is <= 0 or > Limits.MaxHeight
            || monitor.Width < monitor.Height) throw new NotSupportedException("monitor_outside_p0_envelope");
        _device = Vortice.Direct3D11.D3D11.D3D11CreateDevice(DriverType.Hardware, DeviceCreationFlags.BgraSupport, FeatureLevel.Level_11_0);
        _context = _device.ImmediateContext;
        try
        {
            _winrtDevice = CaptureInterop.AsWinRtDevice(_device);
            _item = CaptureInterop.ForMonitor(monitor.Handle);
            if (_item.Size.Width != monitor.Width || _item.Size.Height != monitor.Height)
                throw new NotSupportedException("display_changed");
            _item.Closed += ItemClosed;
            _pool = Direct3D11CaptureFramePool.CreateFreeThreaded(_winrtDevice,
                DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, _item.Size);
            _session = _pool.CreateCaptureSession(_item);
            // Leave capture border/OS consent behavior at its default. No invisible-capture request.
            _session.StartCapture();
        }
        catch { Dispose(); throw; }
    }

    private void ItemClosed(GraphicsCaptureItem _, object args) => _closed = true;

    public bool TryCapture(out PixelFrame? result)
    {
        result = null;
        if (_closed) throw new InvalidOperationException("capture_item_closed");
        var info = new Native.MonitorInfo { Size = Marshal.SizeOf<Native.MonitorInfo>() };
        Native.Check(Native.GetMonitorInfo(_monitor.Handle, ref info));
        if (!info.Monitor.Equals(_monitor.Bounds)) throw new InvalidOperationException("display_changed");
        Direct3D11CaptureFrame? frame = _pool.TryGetNextFrame();
        if (frame is null) return false;
        try
        {
            // Drain at most the two buffers configured above. Never retain a frame history.
            Direct3D11CaptureFrame? newer = _pool.TryGetNextFrame();
            if (newer is not null) { frame.Dispose(); frame = newer; }
            if (frame.ContentSize.Width != _monitor.Width || frame.ContentSize.Height != _monitor.Height)
                throw new InvalidOperationException("display_changed");
            long captureTicks = (long)(frame.SystemRelativeTime.TotalSeconds * Stopwatch.Frequency);
            using ID3D11Texture2D texture = CaptureInterop.Texture(frame.Surface);
            Texture2DDescription description = texture.Description;
            if (description.Width != _monitor.Width || description.Height != _monitor.Height
                || description.Format != Format.B8G8R8A8_UNorm) throw new NotSupportedException("surface_outside_p0_envelope");
            if (_staging is null)
            {
                description.Usage = ResourceUsage.Staging;
                description.BindFlags = BindFlags.None;
                description.CPUAccessFlags = CpuAccessFlags.Read;
                description.MiscFlags = ResourceOptionFlags.None;
                _staging = _device.CreateTexture2D(description);
            }
            _context.CopyResource(_staging, texture);
            MappedSubresource mapped = _context.Map(_staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
            byte[]? pixels = null;
            try
            {
                int stride = checked(_monitor.Width * 4);
                if (mapped.RowPitch < stride || mapped.RowPitch > 1_048_576) throw new InvalidOperationException("invalid_row_pitch");
                pixels = new byte[checked(stride * _monitor.Height)];
                for (int row = 0; row < _monitor.Height; row++)
                    Marshal.Copy(nint.Add(mapped.DataPointer, checked(row * (int)mapped.RowPitch)), pixels, row * stride, stride);
                result = new(new(_sessionId, _monitor.Index, _generation, ++_frameId, captureTicks),
                    _monitor.Width, _monitor.Height, stride, pixels);
                pixels = null;
                return true;
            }
            finally
            {
                if (pixels is not null) CryptographicOperations.ZeroMemory(pixels);
                _context.Unmap(_staging, 0);
            }
        }
        finally { frame.Dispose(); }
    }

    public void Dispose()
    {
        if (_item is not null) _item.Closed -= ItemClosed;
        _session?.Dispose(); _pool?.Dispose(); _staging?.Dispose();
        if (_winrtDevice is IDisposable disposable) disposable.Dispose();
        _context?.Dispose(); _device?.Dispose();
    }
}
