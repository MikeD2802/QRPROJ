using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using QrGuard.Core;
using QrGuard.Windows;

namespace QrGuard.Windows.Contracts;

internal static class Program
{
    [STAThread]
    public static int Main()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
        { Console.WriteLine("FAIL: Windows 10 build 19041 or later with a composing desktop is required."); return 1; }
        try
        {
            MonitorTarget monitor = MonitorTarget.Enumerate()[0];
            int width = Math.Min(monitor.Width, Limits.MaxWidth), height = Math.Min(monitor.Height, Limits.MaxHeight);
            int detailsRequests = 0; long requestedTrack = 0;
            using var renderer = new MaskRenderer(monitor, id => { detailsRequests++; requestedTrack = id; });
            TrackSnapshot track = new(1, 1, 1, new(40, 40, 80, 80), true);
            renderer.Render([track], width, height);
            nint hwnd = OwnMaskWindows().Single();
            VerifyConfiguration(hwnd);
            VerifyBounds(hwnd, track.Bounds.WithMargin(width, height), monitor);
            Assert(IsWindowVisible(hwnd), "mask_not_visible");
            Console.WriteLine("PASS: visible mask has layered/no-activate/tool/topmost styles, alpha 255, no color key, and affinity 0x11.");

            foreach (DecisionState state in Enum.GetValues<DecisionState>())
            {
                renderer.Render([track with { State = state, Decoded = state != DecisionState.UnableToDecode }], width, height);
                var title = new StringBuilder(128); GetWindowText(hwnd, title, title.Capacity);
                Assert(title.ToString().Contains(new PolicyDecision(state).Label, StringComparison.Ordinal), "mask_accessible_label");
                VerifyConfiguration(hwnd);
            }
            Assert(detailsRequests == 0, "render_requested_details");
            Console.WriteLine("PASS: six policy states expose fixed window labels and retain opacity/affinity; render never requests details.");

            Assert(SendMessage(hwnd, 0x0021, 0, 0).ToInt64() == 3, "mouseactivate_should_not_activate");
            SendMessage(hwnd, 0x0202, 0, (nint)((10 << 16) | 10));
            Assert(detailsRequests == 0, "button_up_without_down_requested_details");
            SendMessage(hwnd, 0x0201, 0, (nint)((10 << 16) | 10));
            SendMessage(hwnd, 0x0202, 0, (nint)((10 << 16) | 10));
            Assert(detailsRequests == 1 && requestedTrack == track.TrackId, "deliberate_details_callback");
            Console.WriteLine("PASS: synthetic Win32 mouse messages preserve no-activate handling and request only current track details after down/up.");

            track = track with { GeometryRevision = 2, Bounds = new(100, 60, 100, 90) };
            renderer.Render([track], width, height);
            Assert(OwnMaskWindows().Single() == hwnd, "moving_mask_recreated");
            VerifyConfiguration(hwnd);
            VerifyBounds(hwnd, track.Bounds.WithMargin(width, height), monitor);
            Console.WriteLine("PASS: movement reuses the mask window and retains opaque alpha and affinity readback.");

            renderer.Render([], width, height);
            Assert(!IsWindow(hwnd) && OwnMaskWindows().Count == 0, "retired_mask_survived");
            Console.WriteLine("PASS: retirement destroys the native mask window.");

            renderer.Render([track], width, height);
            hwnd = OwnMaskWindows().Single();
            renderer.Dispose();
            Assert(!IsWindow(hwnd) && OwnMaskWindows().Count == 0, "disposed_mask_survived");
            Console.WriteLine("PASS: disposal destroys the recreated mask window.");
            Console.WriteLine("PASS: 4 P0 and 2 P1 Windows mask API contracts. Synthetic messages are not real UI/input evidence. Capture, phone resistance, real input, sharing and performance were not tested.");
            return 0;
        }
        catch (Win32Exception error)
        { Console.WriteLine($"FAIL: native mask API error code {error.NativeErrorCode}."); return 1; }
        catch (ContractFailure error)
        { Console.WriteLine($"FAIL: {error.Reason}."); return 1; }
        catch (Exception error) when (error is not OutOfMemoryException)
        { Console.WriteLine($"FAIL: Windows mask contract ({error.GetType().Name})."); return 1; }
    }

    private static void VerifyConfiguration(nint hwnd)
    {
        long style = GetWindowLongPtr(hwnd, -20).ToInt64(); // GWL_EXSTYLE.
        const long required = 0x08000088 | 0x00080000;
        Assert((style & required) == required && (style & 0x00000020) == 0, "mask_window_styles");
        Native.Check(GetLayeredWindowAttributes(hwnd, out _, out byte alpha, out uint flags));
        Assert(alpha == 255 && flags == 0x00000002, "mask_opacity");
        Native.Check(Native.GetWindowDisplayAffinity(hwnd, out uint affinity));
        Assert(affinity == 0x00000011, "mask_affinity");
    }

    private static void VerifyBounds(nint hwnd, PixelRect expected, MonitorTarget monitor)
    {
        Native.Check(GetWindowRect(hwnd, out var rect));
        Assert(rect.Left == monitor.Bounds.Left + expected.X && rect.Top == monitor.Bounds.Top + expected.Y
            && rect.Right - rect.Left == expected.Width && rect.Bottom - rect.Top == expected.Height, "mask_bounds");
    }

    private static List<nint> OwnMaskWindows()
    {
        var windows = new List<nint>();
        WindowCallback callback = (hwnd, _) =>
        {
            var name = new StringBuilder(64);
            GetClassName(hwnd, name, name.Capacity);
            if (name.ToString() == "QrGuard.P0.OpaqueMask") windows.Add(hwnd);
            return true;
        };
        // FALSE is the documented result when this thread has no windows after cleanup.
        EnumThreadWindows(GetCurrentThreadId(), callback, 0);
        GC.KeepAlive(callback); return windows;
    }

    private static void Assert(bool condition, string reason)
    { if (!condition) throw new ContractFailure(reason); }

    private sealed class ContractFailure(string reason) : Exception
    { public string Reason { get; } = reason; }

    private delegate bool WindowCallback(nint hwnd, nint data);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EnumThreadWindows(uint thread, WindowCallback callback, nint data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint hwnd, StringBuilder name, int count);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(nint hwnd, StringBuilder text, int count);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint SendMessage(nint hwnd, uint message, nint wparam, nint lparam);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetLayeredWindowAttributes(nint hwnd, out uint colorKey, out byte alpha, out uint flags);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(nint hwnd, out Native.Rect rect);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindow(nint hwnd);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisible(nint hwnd);
}
