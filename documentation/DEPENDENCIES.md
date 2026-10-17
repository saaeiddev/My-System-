# Dependency and deployment notes

Release research was checked against official project documentation on **17 October 2026**. Exact versions actually built are pinned in the project files and build workflow; a researched upstream version is not evidence of a successful restore or build.

| Component | Verified stable release | Purpose and terms |
| --- | --- | --- |
| .NET | SDK 10.0.402, runtime 10.0.13 | Application runtime; MIT-licensed runtime with Microsoft notices. Publish self-contained for x64. |
| Windows App SDK / WinUI 3 | Microsoft.WindowsAppSDK 2.5.1 | Native UI and window management. Preserve Microsoft package notices and redistribution terms. |
| WebView2 SDK | Microsoft.Web.WebView2 1.0.4258.31 | Embedded local WebGL scene. Microsoft WebView2 redistribution license; browser runtime is a separate prerequisite. |
| LibreHardwareMonitorLib | 0.9.6 | Supported hardware sensors, MPL-2.0 and its bundled third-party notices. |
| PresentMon | 2.6.0 | Standalone ETW capture console, MIT. Include the upstream copyright/license with the bundled binary. |
| Three.js | See bundled package/version | Local 3D renderer; MIT. Preserve its copyright/license. |
| Inno Setup | See workflow tool installation | EXE installer compiler; Inno Setup license. Compiler is a build dependency, not installed with My System. |

## Windows build and distribution

The app targets `net10.0-windows10.0.22621.0` and x64 Windows. Build WinUI XAML on a Windows runner with the Windows SDK installed. A .NET SDK on macOS can validate platform-independent code, but cannot validate the Windows UI compiler, installation, native launch, or real sensors.

`SelfContained=true` bundles .NET. `WindowsAppSDKSelfContained=true` independently bundles Windows App SDK. `WindowsPackageType=None` selects unpackaged desktop deployment. The installer must copy the **entire publish directory**, including native DLLs, resource indexes, local 3D assets, licenses, and `Tools/PresentMon.exe`. Publishing only the managed EXE produces an incomplete app. Trimming is disabled because reflection, COM/WinRT bindings, and sensor providers require careful preservation.

WebView2's NuGet package does not include the browser runtime. Windows 11 generally includes the Evergreen runtime. Installation must detect it and handle the missing-runtime state. The official Evergreen standalone installer supports offline deployment; a bootstrapper requires internet access. The browser runtime's own servicing remains subject to Microsoft's policies. My System's visualization loads bundled resources and never depends on a remote CDN.

Use a dedicated WebView2 user-data directory under the current user's LocalAppData. Map a private HTTPS virtual host to the read-only bundled rendering folder, reject unrelated navigations and new windows, validate the sender URI and message schema, and expose no general host object or command execution API. No remote script loading is needed.

## Sensor compatibility and privileges

LibreHardwareMonitor 0.9.6 supports .NET 8 or higher. Its CPU/GPU/motherboard/storage providers differ in capabilities. Always preserve null readings and provider errors as unavailable. Use hardware and sensor identifiers as keys, and recurse through subhardware while updating.

The Intel GPU provider depends on CPU enumeration being enabled. The low-level CPU and motherboard paths use a separately installed **PawnIO** driver. My System does not install that driver or request elevation automatically. Sensors requiring unavailable privileged access are reported as unavailable; standard OS CPU, memory, network, and process metrics remain usable.

The unmodified LibreHardwareMonitor binary is redistributed under MPL-2.0. Include the MPL text and LibreHardwareMonitor's third-party notices, and make the corresponding [0.9.6 source](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/tree/v0.9.6) available to recipients. Changes to MPL-covered source files must be distributed in source form under MPL-2.0. Application code using the library does not automatically acquire the library's license.

Do not use `Win32_VideoController.AdapterRAM` as an authoritative VRAM capacity source: the WMI property is only 32 bits. Use a supported GPU sensor or DXGI adapter descriptor. `GetSystemTimes` kernel time includes idle time; subtract idle from the kernel-plus-user delta. It reports only the current processor group on systems exceeding 64 logical processors, so group-aware counters are needed there. Physical RAM in use is usable total physical RAM minus available physical RAM, distinct from installed DIMM capacity and committed virtual memory. English performance-counter names require a language-neutral PDH API on non-English Windows systems.

## Official references

- [.NET 10 downloads](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)
- [Windows App SDK stable downloads](https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads)
- [Unpackaged WinUI deployment](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/unpackage-winui-app)
- [WebView2 runtime distribution](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/distribution)
- [WebView2 security](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/security)
- [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor)
- [PresentMon](https://github.com/GameTechDev/PresentMon)
- [Inno Setup license and commercial policy](https://jrsoftware.org/isorder.php)
- [GetSystemTimes](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-getsystemtimes)
- [Language-neutral performance counters](https://learn.microsoft.com/en-us/windows/win32/api/pdh/nf-pdh-pdhaddenglishcounterw)
- [WMI video-controller property types](https://learn.microsoft.com/en-us/windows/win32/cimwin32prov/win32-videocontroller)
