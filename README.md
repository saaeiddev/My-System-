# My System

**Advanced 3D Hardware Monitoring & System Control Center**

A native Windows 11 x64 application built with C#/.NET, WinUI 3 and a locally bundled Three.js hardware inspector. All production measurements come from Windows or supported hardware sensors. Unsupported readings stay `N/A` with source and availability details.

Repository: **https://github.com/saaeiddev/My-System-** (the final hyphen is intentional).

## Release status

This is the initial **0.1.0 preview**. The Windows workflow tests, publishes a self-contained application, builds `MySystem-Setup.exe`, then verifies installation, native-window launch, clean shutdown and uninstallation before publishing a GitHub prerelease. A green build does **not** qualify physical Intel/NVIDIA/AMD hardware, gaming capture or sustained monitoring overhead. See [testing](documentation/TESTING.md) and [known limitations](documentation/KNOWN-LIMITATIONS.md).

Download only from [this repository's Releases](https://github.com/saaeiddev/My-System-/releases). Assets appear only after the workflow succeeds:

- `MySystem-Setup.exe`: per-user Windows installer.
- `MySystem-win-x64.zip`: compiled app and all managed/native dependencies.
- `SHA256SUMS.txt`: build checksums.

The release is **unsigned**. No code-signing certificate or signature is claimed.

## Install and launch

1. Use Windows 11 x64 (build 22000 or later).
2. Download and run `MySystem-Setup.exe`. It installs under `%LOCALAPPDATA%\Programs\My System`, adds a Start Menu shortcut, and offers a Desktop shortcut.
3. Microsoft Edge WebView2 Runtime is normally present on Windows 11. If absent, setup runs Microsoft's signed bootstrapper, which requires internet. The app and its 3D content do not need internet after setup. The rest of monitoring can start if WebView2 is unavailable.
4. Open **My System** from Start. Administrator rights and developer tools are not required.
5. Uninstall through Windows Settings → Apps. Local preferences/history are retained under `%LOCALAPPDATA%\MySystem`; remove that folder manually if you want to erase them. The optional Windows startup entry is removed by the uninstaller.

For the ZIP, extract **all** files to one directory before running `MySystem.exe`. Do not copy only the EXE.

## Use

- **Overview:** real CPU, GPU, RAM, disk and network measurements alongside an interactive computer model. Rotate/zoom, choose a component, isolate it or inspect its assembly.
- **CPU / GPU / Memory / Storage / Network:** current measurements, histories and per-device data. GPU counter aggregation is identified in each source tooltip; different GPU sensors may expose different measurements.
- **Thermals:** available temperature/fan sensors, session minimum/maximum and alert events. Opt into LibreHardwareMonitor access in Settings. This app never installs a sensor driver, changes hardware controls or elevates itself.
- **Gaming:** select a process and start PresentMon ETW capture. FPS uses actual present events, not GPU utilization. See [capture methodology and permissions](documentation/GAMING.md).
- **Processes:** search, sort and inspect CPU/memory use. Ending a selected process requires a confirmation and respects Windows permissions.
- **System Information:** WMI inventory, display information, battery, storage and network details; export JSON or TXT.
- **Performance History:** select a metric and 60-second, 5-minute, 30-minute or 1-hour range, hover values, pause plotting and export CSV. Disk retention is off by default.
- **Settings:** appearance/accent, refresh, rendering quality/cap, reduced motion, units, alerts/cooldowns, sensor access, startup, tray behavior, overlay and retention.
- **Overlay:** toggle with `Ctrl+Alt+M`, the Gaming page or the tray menu. Set position, opacity and compact/expanded preset in Settings. This is an ordinary topmost Windows window for desktop/borderless use. It does not inject into games.

Thresholds are user preferences, not universal hardware safety limits. Physical RAM is total usable physical memory minus available memory; it is not committed memory. Sensor and counter errors never become invented zeroes.

## Build from source

On Windows 11 with Visual Studio C++/Windows SDK tools, .NET SDK **10.0.402**, Node 22 and Inno Setup 6:

```powershell
git clone https://github.com/saaeiddev/My-System-.git
cd My-System-
./build/Build.ps1
./build/SmokeTest.ps1
```

`Build.ps1` restores pinned npm dependencies, tests the renderer, bundles local scene assets, runs .NET unit tests, publishes the x64 app and creates the installer. It downloads the official PresentMon 2.6.0 console executable and verifies its published SHA256, and verifies the Microsoft signature on the WebView2 bootstrapper. Downloads require network only at build/install time. For compiled app files without Inno Setup, use `./build/Build.ps1 -SkipInstaller`.

Portable tests can also run on macOS/Linux: `dotnet test tests/MySystem.Tests/MySystem.Tests.csproj`. Full WinUI XAML compilation and execution require Windows. The workflow in `.github/workflows/windows.yml` provides the Windows runner.

## Architecture

- `MySystem.Core`: timestamped metric contracts, bounded history, alerts, frame statistics.
- `MySystem.Hardware`: Win32/performance counters, network byte deltas, WMI inventory, optional LibreHardwareMonitor providers.
- `MySystem.Telemetry`: serial cancellable background acquisition and fault isolation.
- `MySystem.Gaming`: bounded PresentMon subprocess capture, CSV parsing, swap-chain selection and stale detection.
- `MySystem.Infrastructure`: atomic settings, per-user startup, local diagnostics, bounded opt-in history, exports.
- `MySystem.App`: native WinUI navigation, view model, charts, tray, confirmation dialogs and desktop overlay.
- `MySystem.Rendering`, `assets/3d-models`: local WebGL scene and original procedural geometry; no CDN.

The WebView only receives telemetry and display configuration. Its only accepted outbound message selects a fixed component. Navigation, new windows, downloads, permissions and nonlocal resources are blocked. No native host object is exposed.

## Privacy and licenses

No account, analytics, remote telemetry, overclocking, fan control, services, process injection or anti-cheat bypass. Settings/diagnostics and optional history remain on this PC. History omits process paths/inventory, is limited to 20 MiB/day and 100 MiB total, and is pruned to the selected 1–30 days while recording.

See [dependency notes](documentation/DEPENDENCIES.md), [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) and `licenses/`. Application code and original assets are MIT licensed. LibreHardwareMonitor is unmodified MPL-2.0 software, with corresponding upstream source linked in the notices.
