using System.ComponentModel;
using System.Runtime.InteropServices;
using QrGuard.Core;

namespace QrGuard.Windows;

internal sealed class MaskRenderer(MonitorTarget monitor, Action<long>? detailsRequested = null) : IMaskRenderer
{
    private const string ClassName = "QrGuard.P0.OpaqueMask";
    private static readonly Native.WindowProc Procedure = WndProc;
    private static ushort _class;
    private sealed class MaskContent(long trackId, Action<long>? request)
    {
        public long TrackId = trackId;
        public Action<long>? Request = request;
        public string Label = "";
        public bool Pressed;
    }
    private static readonly Dictionary<nint, MaskContent> Content = [];
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
                hwnd = Native.CreateWindowEx(0x08000088 | Native.WsExLayered, ClassName, "QR Guard mask", 0x80000000,
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
                    Content.Add(hwnd, new(track.TrackId, detailsRequested));
                }
                catch { Native.DestroyWindow(hwnd); throw; }
            }
            string label = new PolicyDecision(track.Decoded ? track.State : DecisionState.UnableToDecode).Label;
            if (Content[hwnd].Label != label)
            {
                Content[hwnd].Label = label;
                Native.Check(Native.SetWindowText(hwnd, "QR masked · " + label + " · click for details"));
            }
            Native.Check(Native.SetWindowPos(hwnd, (nint)(-1), x, y, rect.Width, rect.Height, 0x0010 | 0x0040));
            Native.InvalidateRect(hwnd, 0, true);
            Native.UpdateWindow(hwnd);
        }
    }

    private static nint WndProc(nint hwnd, uint message, nint wparam, nint lparam)
    {
        switch (message)
        {
            case 0x0021: return 3; // MA_NOACTIVATE: deliver the deliberate click without activating the mask.
            case 0x0084: return 1; // WM_NCHITTEST: only this rectangular HWND is HTCLIENT.
            case 0x0201:
                if (Content.TryGetValue(hwnd, out var down)) down.Pressed = true;
                return 0;
            case 0x0202:
                if (Content.TryGetValue(hwnd, out var up) && up.Pressed)
                {
                    up.Pressed = false;
                    Native.GetClientRect(hwnd, out var client);
                    int mouseX = (short)(lparam.ToInt64() & 0xffff), mouseY = (short)((lparam.ToInt64() >> 16) & 0xffff);
                    if (mouseX >= 0 && mouseY >= 0 && mouseX < client.Right && mouseY < client.Bottom)
                        up.Request?.Invoke(up.TrackId); // Details only; no URL/action exists in the HWND.
                }
                return 0;
            case 0x0203: case 0x0204: case 0x0205: case 0x0206:
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
                    string text = "QR masked\n" + (Content.TryGetValue(hwnd, out var content) ? content.Label : "Unable to decode");
                    Native.DrawText(dc, text, text.Length, ref rect, 0x0001 | 0x0010 | 0x0800);
                }
                finally { Native.EndPaint(hwnd, ref paint); }
                return 0;
        }
        return Native.DefWindowProc(hwnd, message, wparam, lparam);
    }

    private static void Destroy(nint hwnd) { Content.Remove(hwnd); Native.DestroyWindow(hwnd); }
    public void Clear() { foreach (nint hwnd in _windows.Values) Destroy(hwnd); _windows.Clear(); }
    public void Dispose() => Clear();
}
