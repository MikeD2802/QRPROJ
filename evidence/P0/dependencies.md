# P0 dependencies and API verification

| Dependency | Pinned choice | Purpose / primary source |
|---|---|---|
| .NET SDK | 10.0.401, `rollForward: disable` | .NET 10 LTS; [Microsoft downloads](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) |
| Windows API projections | Microsoft.Windows.SDK.NET.Ref 10.0.19041.57 | SDK-selected projections, explicitly fixed by `WindowsSdkPackageVersion` |
| ZXingCpp | 0.5.3, exact NuGet range and content-hash lock | Official .NET binding, native zxing-cpp 3.1.1; [package](https://www.nuget.org/packages/ZXingCpp/0.5.3), [upstream binding](https://github.com/zxing-cpp/zxing-cpp/tree/v3.1.1/wrappers/dotnet) |
| Vortice.Direct3D11 | 3.8.3, exact NuGet range and content-hash lock | D3D11 staging/readback; [package](https://www.nuget.org/packages/Vortice.Direct3D11/3.8.3), [upstream](https://github.com/amerkoleci/Vortice.Windows) |

Vortice 3.8.3's upstream dependency graph includes SharpGen.Runtime / SharpGen.Runtime.COM **2.4.2-beta**, plus Vortice DXGI/DirectX 3.8.3 and Mathematics 2.1.0. These are locked transitively; this spike does not claim a fully stable transitive graph or production supply-chain approval. There is no OpenCV, web backend, external agent framework, Python runtime, network client, updater or service in the application.

Use committed `packages.lock.json` files and `--locked-mode` for reproducibility. .NET/Windows framework references are also tied to the exact SDK and Windows projection package. GitHub Actions references exact action commits. Dependency restore is a developer/build operation; the endpoint application itself does not restore packages or perform update checks.

Supported APIs used:

- [Monitor capture interop](https://learn.microsoft.com/en-us/windows/win32/api/windows.graphics.capture.interop/nf-windows-graphics-capture-interop-igraphicscaptureiteminterop-createformonitor)
- [Windows Graphics Capture](https://learn.microsoft.com/en-us/windows/apps/develop/media-authoring-processing/screen-capture)
- [SetWindowDisplayAffinity](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowdisplayaffinity)
- [Window styles](https://learn.microsoft.com/en-us/windows/win32/winmsg/extended-window-styles)

Capture border and OS consent behavior remain at their defaults. The custom interop bridge calls the supported monitor factory, WinRT apartment initialization and DXGI-surface interfaces. Compilation establishes ABI declarations bind; correctness on a real compositor remains unverified until P0's Windows acceptance.
