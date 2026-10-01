using System.ComponentModel;
using System.Runtime.InteropServices;

namespace QrGuard.Windows;

internal static class Native
{
    internal const uint WsExLayered = 0x00080000;
    internal const uint LwaAlpha = 0x00000002;
    internal const uint WdaExcludeFromCapture = 0x00000011;
    internal delegate bool MonitorCallback(nint monitor, nint hdc, nint rect, nint data);
    internal delegate nint WindowProc(nint hwnd, uint message, nint wparam, nint lparam);
    [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] internal struct MonitorInfo
    { public int Size; public Rect Monitor, Work; public uint Flags; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] internal struct WindowClass
    {
        public uint Size, Style;
        public WindowProc WndProc;
        public int ClassExtra, WindowExtra;
        public nint Instance, Icon, Cursor, Background;
        public string? MenuName;
        public string ClassName;
        public nint SmallIcon;
    }
    [StructLayout(LayoutKind.Sequential)] internal unsafe struct PaintStruct
    {
        public nint Hdc;
        public int Erase;
        public Rect Paint;
        public int Restore, IncUpdate;
        public fixed byte Reserved[32];
    }
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool EnumDisplayMonitors(nint hdc, nint clip, MonitorCallback callback, nint data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern ushort RegisterClassEx(ref WindowClass windowClass);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] internal static extern nint GetModuleHandle(string? module);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern nint CreateWindowEx(uint exStyle, string className, string title, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern nint DefWindowProc(nint hwnd, uint message, nint wparam, nint lparam);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool DestroyWindow(nint hwnd);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool SetLayeredWindowAttributes(nint hwnd, uint colorKey, byte alpha, uint flags);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool SetWindowDisplayAffinity(nint hwnd, uint affinity);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetWindowDisplayAffinity(nint hwnd, out uint affinity);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool SetWindowPos(nint hwnd, nint insertAfter, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool ShowWindow(nint hwnd, int command);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool InvalidateRect(nint hwnd, nint rect, [MarshalAs(UnmanagedType.Bool)] bool erase);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool UpdateWindow(nint hwnd);
    [DllImport("user32.dll")] internal static extern nint BeginPaint(nint hwnd, out PaintStruct paint);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool EndPaint(nint hwnd, ref PaintStruct paint);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetClientRect(nint hwnd, out Rect rect);
    [DllImport("user32.dll")] internal static extern int FillRect(nint hdc, ref Rect rect, nint brush);
    [DllImport("gdi32.dll")] internal static extern nint CreateSolidBrush(uint color);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool DeleteObject(nint obj);
    [DllImport("gdi32.dll")] internal static extern uint SetTextColor(nint hdc, uint color);
    [DllImport("gdi32.dll")] internal static extern int SetBkMode(nint hdc, int mode);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int DrawText(nint hdc, string text, int length, ref Rect rect, uint format);

    internal static void Check(bool result) { if (!result) throw new Win32Exception(Marshal.GetLastWin32Error()); }
}

internal sealed record MonitorTarget(nint Handle, int Index, Native.Rect Bounds)
{
    public int Width => Bounds.Right - Bounds.Left;
    public int Height => Bounds.Bottom - Bounds.Top;
    public override string ToString() => $"Monitor {Index}: {Width} × {Height} at ({Bounds.Left}, {Bounds.Top})";

    public static IReadOnlyList<MonitorTarget> Enumerate()
    {
        var monitors = new List<MonitorTarget>();
        Native.MonitorCallback callback = (monitor, _, _, _) =>
        {
            var info = new Native.MonitorInfo { Size = Marshal.SizeOf<Native.MonitorInfo>() };
            Native.Check(Native.GetMonitorInfo(monitor, ref info));
            monitors.Add(new(monitor, monitors.Count + 1, info.Monitor)); return true;
        };
        Native.Check(Native.EnumDisplayMonitors(0, 0, callback, 0)); GC.KeepAlive(callback);
        return monitors;
    }
}
