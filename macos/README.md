# My System for macOS — Apple Silicon Preview 0.2.1

**Target:** MacBook Pro (2021) with M1 Pro / Apple Silicon, macOS 13+.

This is a native SwiftUI + WKWebView application, not a Windows executable or emulation layer. The Mac dashboard now renders its 3D hardware model natively with SceneKit, independent of WebKit. The visual model depicts a **representative PC assembly, not the internal geometry of an M1 Pro MacBook**.

## Actual native telemetry
- CPU activity measured from Mach host CPU ticks, sampled approximately once per second.
- Unified memory total from ProcessInfo and approximate used memory from Mach active, wired and compressed pages.
- Filesystem available and total space from the macOS filesystem APIs.
- Download/upload throughput from BSD en-interface byte counters.
- Metal device name, hardware model, CPU core count and OS version.
- Recent live graphs for CPU and memory.
- Fully native SceneKit 3D hardware model: materials, lighting, camera orbit and component picking — no WebView rendering dependency.
- No account, cloud telemetry, background helper, privileged agent or driver.

## Known limitations
- CPU/GPU temperature is unavailable without unsupported/privileged interfaces; displays N/A.
- No GPU engine utilization or real Mac game FPS/overlay in this preview; displays N/A.
- No process list, per-core history, persistence or battery page yet.
- Memory estimate does not necessarily match every Activity Monitor summary.
- Network counters aggregate active BSD en interfaces and exclude tunnels/virtual interfaces.
- The illustrative 3D ATX hardware is not a scan of Apple Silicon hardware.

## Install on M1 Pro
1. Download MySystem-macOS-arm64.dmg from GitHub Releases.
2. Open the DMG and drag MySystem.app to Applications.
3. Launch from Applications.
4. This build is **ad-hoc signed**, without an Apple Developer ID signature or notarization. macOS Gatekeeper may block it. After reviewing the source and trusting the application, use Finder's Open action and, if offered, System Settings → Privacy & Security → Open Anyway. **Do not globally disable Gatekeeper.**
5. It runs offline after installation; no Xcode/Node required.

If your organization forbids running unsigned or unnotarized apps, do not bypass the policy.

## Build from source
On an Apple Silicon Mac, install Xcode CLI tools or Xcode and Node.js 22; clone the repository and run:

    git clone https://github.com/saaeiddev/My-System-.git
    cd My-System-
    bash macos/build.sh

Build outputs are in artifacts: the app bundle, macOS arm64 DMG and ZIP plus SHA256 checksums.

The GitHub Actions workflow uses the arm64 macos-15 runner, tests the shared scene, compiles the native SwiftUI/SceneKit binary, runs CPU/memory/disk/3D scene health checks, verifies code signature and generates DMG/ZIP. **CI build success is not proof of physical M1 Pro thermal/GPU capabilities or actual user-interface behavior.** Those tests are separate.

## Privacy and architecture
Telemetry is gathered locally from supported public macOS APIs. There is no user account or network data upload. The active Mac 3D atlas is rendered with SceneKit, with no browser scripting bridge involved. Shared Three.js sources are retained in the repository for the Windows edition. No network resources are loaded by the native SceneKit model.

Windows sources remain unchanged at src/MySystem.App. Native Mac sources live under macos/.

## 0.2.1 display fix
The macOS interface no longer relies on a WebKit/WebGL file URL to render its hardware atlas. A native SceneKit assembly now displays CPU, GPU, RAM, storage and cooler meshes with realistic materials and lights, drag-to-orbit and click-to-select. A live CPU sampling status line identifies when Mach host CPU readings are available. CI runs a real native `--self-test` on Apple Silicon to check two distinct CPU samples, VM memory, disk capacity and 3D model assembly. Hardware-specific UI validation on an M1 Pro remains necessary.
