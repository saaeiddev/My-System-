# Complete macOS source and release file manifest

Repository: https://github.com/saaeiddev/My-System-

Target: Apple Silicon (arm64), including MacBook Pro 2021 with M1 Pro, macOS 13 or later. Preview version: 0.2.0.

## Native macOS app (committed on `main`)

| File | Purpose |
| --- | --- |
| [macos/MySystemMac.swift](../macos/MySystemMac.swift) | Complete native SwiftUI program; Mach CPU, memory, network/storage telemetry; dashboard, history and WKWebView wrapper |
| [macos/mac-bridge.js](../macos/mac-bridge.js) | Constrained local JavaScript ⇄ native message adapter |
| [macos/Info.plist](../macos/Info.plist) | App identity, Apple Silicon compatibility, macOS bundle settings |
| [macos/build.sh](../macos/build.sh) | Reproducible build, app bundle assembly, ad-hoc signing, ZIP/DMG packaging and SHA256 generation |
| [macos/README.md](../macos/README.md) | Mac installation, build, privacy and known limitations |
| [.github/workflows/macos.yml](../.github/workflows/macos.yml) | Apple Silicon runner: tests, compiles, builds and publishes release artifacts |

## Shared 3D visualization source (committed on `main`)

| Path | Purpose |
| --- | --- |
| [src/MySystem.Rendering/scene.js](../src/MySystem.Rendering/scene.js) | Interactive Three.js scene, orbiting and hardware selection |
| [src/MySystem.Rendering/scene.css](../src/MySystem.Rendering/scene.css) | Dashboard 3D view styling |
| [src/MySystem.Rendering/index.html](../src/MySystem.Rendering/index.html) | 3D interface markup and content security policy |
| [src/MySystem.Rendering/telemetry.mjs](../src/MySystem.Rendering/telemetry.mjs) | Data validation, unavailable-sensor handling and temperature highlighting |
| [src/MySystem.Rendering/build.mjs](../src/MySystem.Rendering/build.mjs) | Offline renderer bundling |
| [src/MySystem.Rendering/package.json](../src/MySystem.Rendering/package.json) + [package-lock.json](../src/MySystem.Rendering/package-lock.json) | Exact JavaScript dependencies |
| [src/MySystem.Rendering/tests](../src/MySystem.Rendering/tests) | Automated 3D scene/telemetry tests |
| [assets/3d-models/hardware.js](../assets/3d-models/hardware.js) | Original procedural CPU, GPU, RAM, cooling and storage meshes |
| [assets/materials/palette.json](../assets/materials/palette.json) | Shared materials and accent palette |
| [assets/icons/MySystem.png](../assets/icons/MySystem.png) | App icon source used to generate ICNS |

The native macOS bundle, compiled renderer, icon, and dependencies are assembled by `bash macos/build.sh`. Generated `dist/` and `artifacts/` outputs are intentionally not committed to `main`; this prevents tracked build cache/binary duplication.

## Downloadable files in this repository's GitHub Releases

Verified macOS prerelease: [v0.2.0-macos-build.2](https://github.com/saaeiddev/My-System-/releases/tag/v0.2.0-macos-build.2).

- [MySystem-macOS-arm64.dmg](https://github.com/saaeiddev/My-System-/releases/download/v0.2.0-macos-build.2/MySystem-macOS-arm64.dmg): drag-and-drop install image with the compiled app
- [MySystem-macOS-arm64.zip](https://github.com/saaeiddev/My-System-/releases/download/v0.2.0-macos-build.2/MySystem-macOS-arm64.zip): archived native `.app` bundle
- [SHA256SUMS-macOS.txt](https://github.com/saaeiddev/My-System-/releases/download/v0.2.0-macos-build.2/SHA256SUMS-macOS.txt): hashes for downloaded artifacts

**Build verification vs hardware verification:** The macOS CI run successfully compiled the native ARM64 app and generated ZIP/DMG. It did not independently qualify physical M1 Pro GPU telemetry, thermal sensors, game FPS or interactive UI on the target MacBook. Unavailable metrics display N/A. The release is ad-hoc signed but not Apple Developer ID signed/notarized.

## Rebuild

On a suitable Apple Silicon Mac with Xcode command-line tools and Node.js 22:

```bash
git clone https://github.com/saaeiddev/My-System-.git
cd My-System-
bash macos/build.sh
```

All sources necessary for the current preview and the full release build instructions are stored in this repository. The Windows implementation is preserved in its original directories.
