using System.Runtime.InteropServices;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX.Direct3D11;

namespace QrGuard.Windows;

// Small supported ABI bridge; the GPU API itself is handled by the pinned Vortice binding.
internal static unsafe class CaptureInterop
{
    [DllImport("combase.dll")] private static extern int RoInitialize(uint type);
    [DllImport("combase.dll")] private static extern void RoUninitialize();
    [DllImport("combase.dll", CharSet = CharSet.Unicode)] private static extern int WindowsCreateString(string text, int length, out nint value);
    [DllImport("combase.dll")] private static extern int WindowsDeleteString(nint value);
    [DllImport("combase.dll")] private static extern int RoGetActivationFactory(nint classId, in Guid iid, out nint factory);
    [DllImport("d3d11.dll")] private static extern int CreateDirect3D11DeviceFromDXGIDevice(nint dxgiDevice, out nint device);

    public sealed class MtaApartment : IDisposable
    {
        public MtaApartment() => Marshal.ThrowExceptionForHR(RoInitialize(1));
        public void Dispose() => RoUninitialize();
    }

    public static GraphicsCaptureItem ForMonitor(nint monitor)
    {
        const string name = "Windows.Graphics.Capture.GraphicsCaptureItem";
        Marshal.ThrowExceptionForHR(WindowsCreateString(name, name.Length, out nint classId));
        nint factory = 0, item = 0;
        try
        {
            Guid interopId = new("3628e81b-3cac-4c60-b7f4-23ce0e0c3356");
            Marshal.ThrowExceptionForHR(RoGetActivationFactory(classId, interopId, out factory));
            Guid itemId = new("79c3f95b-31f7-4ec2-a464-632ef5d30760");
            var create = (delegate* unmanaged[Stdcall]<nint, nint, Guid*, nint*, int>)(*(nint**)factory)[4];
            Marshal.ThrowExceptionForHR(create(factory, monitor, &itemId, &item));
            return WinRT.MarshalInterface<GraphicsCaptureItem>.FromAbi(item);
        }
        finally
        {
            if (item != 0) Marshal.Release(item);
            if (factory != 0) Marshal.Release(factory);
            WindowsDeleteString(classId);
        }
    }

    public static IDirect3DDevice AsWinRtDevice(ID3D11Device device)
    {
        using var dxgi = device.QueryInterface<IDXGIDevice>();
        Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgi.NativePointer, out nint pointer));
        try { return WinRT.MarshalInterface<IDirect3DDevice>.FromAbi(pointer); }
        finally { Marshal.Release(pointer); }
    }

    public static ID3D11Texture2D Texture(IDirect3DSurface surface)
    {
        nint surfacePointer = WinRT.MarshalInterface<IDirect3DSurface>.FromManaged(surface), access = 0;
        try
        {
            Guid accessId = new("a9b3d012-3df2-4ee3-b8d1-8695f457d3c1");
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(surfacePointer, in accessId, out access));
            Guid textureId = typeof(ID3D11Texture2D).GUID;
            nint texture = 0;
            var get = (delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)(*(nint**)access)[3];
            Marshal.ThrowExceptionForHR(get(access, &textureId, &texture));
            return new ID3D11Texture2D(texture);
        }
        finally
        {
            if (access != 0) Marshal.Release(access);
            Marshal.Release(surfacePointer);
        }
    }
}
