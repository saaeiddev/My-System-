# Real frame capture

My System starts the bundled **PresentMon console application** against the process selected in Gaming. PresentMon observes Windows ETW frame presentation events. My System never injects code into the target application and does not derive FPS from GPU load.

## Measurement definition

- The collector requests the stable `--v1_metrics` CSV schema and reads `MsBetweenPresents` by header name, using invariant numeric parsing. Quoted fields and escaped quotation marks are supported. Invalid, nonfinite, and nonpositive intervals are discarded; a first-frame zero is not counted.
- Statistics use the last **60 seconds** of source frame events. Each swap chain has its own buffer; FPS from different swap chains is never summed. The swap chain with the most frames in the latest five seconds is shown. At most 16 chains and 100,000 frames per chain are retained.
- Average FPS is `1000 / mean(frame intervals in milliseconds)`.
- **1% low FPS** is `1000 / mean(the slowest ceil(N × 0.01) frame intervals)`. This is a tail-mean definition; it is not the inverse of the 99th percentile. With fewer than 100 samples, it uses the single slowest frame. Short sessions consequently have limited statistical meaning.
- The displayed frame-time readout is the latest measured presentation interval. The frame-time graph contains the individual buffered intervals.
- FPS represents application **presentation cadence**, which can differ from displayed FPS because of dropped frames, compositor behavior, frame generation, and display timing. It is not a measurement of input latency.
- When the latest source event is older than three seconds, readings become unavailable. Old values and delayed ETW batches are not shown as live measurements. `--qpc_time` supplies the source event's `QPCTime`; the retention window and staleness checks use that monotonic Windows performance-counter timebase. Chart timestamps map each event to a UTC clock sample taken at capture start, so later wall-clock changes do not distort frame spacing. UTC alignment is subject to the clock sample's scheduling precision; it is not an independent nanosecond-accuracy wall clock. Invalid or missing source timestamps are rejected.

The command line uses a unique session name and `--process_id`, `--output_stdout`, `--no_console_stats`, `--v1_metrics`, `--qpc_time`, `--terminate_on_proc_exit`, `--no_track_gpu`, `--no_track_input`, and `--no_track_display`. The disabled tracking paths reduce unnecessary overhead for this presentation-interval collector. GPU utilization and temperature are obtained independently from hardware providers.

## Permissions and limitations

An account needs permission to start the ETW trace session. PresentMon documents membership in **Performance Log Users**; administrator rights may otherwise be necessary. My System does not silently change group membership, elevate the main app, install a service, or disable any protection. If access is denied, Gaming reports the collector error and the rest of the application continues to function.

An administrator can explicitly grant an intended account membership in Performance Log Users using Windows Computer Management, followed by sign-out and sign-in. On Windows editions without that UI, consult the corresponding Windows account-management documentation. This is an optional operating-system configuration, not performed by the installer.

Some protected processes, anti-cheat configurations, and graphics presentation paths cannot be captured. Minimized or paused games may stop presenting. Process-name information can be inaccessible for another user's processes; selection uses PID. OpenGL/Vulkan typically expose less instrumentation than DXGI applications. PresentMon also documents HAGS limitations in GPU execution timing; My System does not use those GPU-time columns to calculate FPS.

The optional My System overlay is a separate topmost desktop window. Exclusive fullscreen games may cover it; borderless or windowed modes are recommended. It does not bypass game restrictions.

## Validation status

Parser, mathematical statistics, bounded buffers, and error handling can be exercised with isolated tests. An actual game trace requires a Windows graphics session. A successful build or parser test does **not** establish game compatibility, capture overhead, or RTX 4060 compatibility. See the main testing report for measured Windows smoke-test results and the hardware scenarios that remain untested.

## References

- [PresentMon console options and CSV definitions](https://github.com/GameTechDev/PresentMon/blob/v2.6.0/README-ConsoleApplication.md)
- [PresentMon permissions and measurement limitations](https://github.com/GameTechDev/PresentMon/blob/v2.6.0/README.md)
- [PresentMon v2.6.0 release](https://github.com/GameTechDev/PresentMon/releases/tag/v2.6.0)
