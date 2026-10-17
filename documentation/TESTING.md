# Testing and evidence

## Automated checks

The Windows workflow runs the exact source in the checked-out commit:

1. Node tests for real telemetry mapping, unavailability, clamping, model selection metadata and bounded configuration.
2. .NET unit tests for physical memory semantics, frame statistics/1% lows, metric availability, history CSV escaping/ranges/caps, alert persistence/cooldown and missing-sample handling, PresentMon CSV quoting/header parsing, settings round trip/validation and export formatting.
3. WinUI Release publish on the Windows x64 runner.
4. Inno Setup packaging.
5. Silent per-user installation; advancing real CPU/physical-memory samples; all twelve native pages; a successfully initialized local WebGL canvas; GPU scene selection opening native GPU details; overlay open/close; measured CPU/working-set snapshot; orderly shutdown; and uninstall. Results are in smoke/native-checks.json.

The release step runs only after these checks succeed. Logs, TRX tests and `smoke/launch.json` are stored as workflow artifacts. These integration checks run on the Windows Server 2025 hosted runner. They are not physical Windows 11, gaming stability or hardware accuracy tests.

## Local development results

2026-10-17: 22 portable .NET tests passed on the macOS ARM64 host. Gaming and infrastructure libraries compiled successfully. Initial hardware restore exposed a System.Management dependency downgrade; it was corrected to the current 10.0.13 servicing version. Full Windows build results must be read from the corresponding workflow; do not infer success from this document.

## Required hardware qualification matrix

Run and record OS/build, BIOS, GPU driver, app commit and permissions for every case. The following remain **untested** until evidence is recorded:

- Intel laptop + NVIDIA RTX 4060; AMD discrete; Intel integrated; AMD integrated; multi-adapter desktop.
- CPU total and per-core vs Task Manager under idle/load; process CPU sums, PID reuse, working sets vs physical/committed memory.
- Supported temperatures/fans/power vs LibreHardwareMonitor, with missing driver, standard user and supported privilege conditions.
- GPU adapter identification and VRAM >4 GiB, driver restart, disconnect/reconnect and sleep/resume.
- A supported DirectX game's PresentMon capture vs its native benchmark: 60-second mean FPS and documented slowest 1% frame intervals. Check staleness, process exit and restarts.
- Overlay in windowed/borderless/fullscreen, hotkey conflict, multi-monitor positioning and opacity.
- Installation/uninstallation under a clean standard Windows user, no WebView2 runtime, offline device, paths with spaces/non-ASCII.
- 100/150/200% DPI, resizing/Snap, keyboard-only, high contrast/screen reader, reduced motion, tray restart.
- 8-hour soak, settings persistence/corruption, history caps and cooldowns.
- Idle and game A/B overhead: app off/on, sensors off/on, scene visible/hidden, median/p95 process CPU, working set, GPU load and game frame times. Report hardware and uncertainty; do not present CI virtual-machine measurements as gaming overhead.
