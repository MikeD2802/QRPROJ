using System.ComponentModel;
using System.Runtime.InteropServices;
using QrGuard.Core;

namespace QrGuard.Windows;

internal sealed class MaskRenderer(MonitorTarget monitor) : IMaskRenderer
{
    private const string ClassName = "QrGuard.P0.OpaqueMask";
    private static readonly Native.WindowProc Procedure = WndProc;
    private static ushort _class;
    private static readonly Dictionary<nint, bool> Decoded = [];
    private readonly Dictionary<long, nint> _windows = [];

    private static void Register()
    {
        if (_class != 0) return;
        var windowClass = new Native.WindowClass
        {
            Size = (uint)Marshal.SizeOf<Native.WindowClass>(), WndProc = Procedure,
            Instance = Native.GetModuleHandle(null), ClassName = ClassName
        };
        _class = Native.RegisterClassEx(ref windowClass);
        if (_class == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    public void Render(IReadOnlyList<TrackSnapshot> tracks, int frameWidth, int frameHeight)
    {
        Register();
        foreach (long retired in _windows.Keys.Except(tracks.Select(t => t.TrackId)).ToArray())
        { Destroy(_windows[retired]); _windows.Remove(retired); }
        foreach (TrackSnapshot track in tracks)
        {
            PixelRect rect = track.Bounds.WithMargin(frameWidth, frameHeight);
            int x = checked(monitor.Bounds.Left + rect.X), y = checked(monitor.Bounds.Top + rect.Y);
            if (!_windows.TryGetValue(track.TrackId, out nint hwnd))
            {
                // GetWindowDisplayAffinity requires WS_EX_LAYERED. Layering does not require translucency.
                // Retain WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST and WS_POPUP.
                hwnd = Native.CreateWindowEx(0x08000088 | Native.WsExLayered, ClassName, "QR masked · P0", 0x80000000,
                    x, y, rect.Width, rect.Height, 0, 0, Native.GetModuleHandle(null), 0);
                if (hwnd == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
                try
                {
                    // Constant alpha 255 is fully opaque; LWA_ALPHA alone enables no color-key holes.
                    Native.Check(Native.SetLayeredWindowAttributes(hwnd, 0, 255, Native.LwaAlpha));
                    // Configure and verify before showing. Keep the mask present continuously during capture.
                    Native.Check(Native.SetWindowDisplayAffinity(hwnd, Native.WdaExcludeFromCapture));
                    Native.Check(Native.GetWindowDisplayAffinity(hwnd, out uint affinity));
                    if (affinity != Native.WdaExcludeFromCapture)
                        throw new InvalidOperationException("mask_affinity_unavailable");
                    _windows.Add(track.TrackId, hwnd);
                }
                catch { Native.DestroyWindow(hwnd); throw; }
            }
            Decoded[hwnd] = track.Decoded;
            Native.Check(Native.SetWindowPos(hwnd, (nint)(-1), x, y, rect.Width, rect.Height, 0x0010 | 0x0040));
            Native.InvalidateRect(hwnd, 0, true);
            Native.UpdateWindow(hwnd);
        }
    }

    private static nint WndProc(nint hwnd, uint message, nint wparam, nint lparam)
    {
        switch (message)
        {
            case 0x0021: return 4; // WM_MOUSEACTIVATE: MA_NOACTIVATEANDEAT.
            case 0x0084: return 1; // WM_NCHITTEST: only this rectangular HWND is HTCLIENT.
            case 0x0201: case 0x0202: case 0x0203: case 0x0204: case 0x0205: case 0x0206:
            case 0x0207: case 0x0208: case 0x0209: case 0x020B: case 0x020C: case 0x020D:
                return 0; // Consume mouse-button input; no click-through to an invisible control.
            case 0x0014: return 1; // WM_ERASEBKGND is painted opaquely in WM_PAINT.
            case 0x000F:
                nint dc = Native.BeginPaint(hwnd, out var paint);
                try
                {
                    Native.GetClientRect(hwnd, out var rect);
                    nint brush = Native.CreateSolidBrush(0x00251C16);
                    try { Native.FillRect(dc, ref rect, brush); }
                    finally { Native.DeleteObject(brush); }
                    Native.SetTextColor(dc, 0x00FFFFFF); Native.SetBkMode(dc, 1);
                    string text = Decoded.GetValueOrDefault(hwnd) ? "QR masked\nUnverified · P0" : "QR masked\nUnable to decode";
                    Native.DrawText(dc, text, text.Length, ref rect, 0x0001 | 0x0010 | 0x0800);
                }
                finally { Native.EndPaint(hwnd, ref paint); }
                return 0;
        }
        return Native.DefWindowProc(hwnd, message, wparam, lparam);
    }

    private static void Destroy(nint hwnd) { Decoded.Remove(hwnd); Native.DestroyWindow(hwnd); }
    public void Clear() { foreach (nint hwnd in _windows.Values) Destroy(hwnd); _windows.Clear(); }
    public void Dispose() => Clear();
}
