# Support boundaries and remaining qualification

This initial preview implements native monitoring and a real interactive scene. It is not hardware-qualified production software yet.

- Real CPU/RAM/process/network/volume data use Windows APIs. Counter names are English; unavailable localized or restricted counters are reported explicitly. Multi-group machines above 64 logical processors need dedicated qualification.
- WDDM GPU load is busiest engine across adapters. Dedicated memory counter usage is summed across adapters until optional vendor sensor aliases are available; source descriptions distinguish the semantics. Raw per-adapter readings are listed. There is no interactive primary-adapter picker yet.
- LibreHardwareMonitor 0.9.6 sensors are opt-in. CPU low-level access may need a separately installed PawnIO component and suitable permissions. My System does not install it or elevate. GPU sensor coverage differs across NVIDIA, AMD and Intel. No physical RTX 4060/AMD/Intel qualification has been completed in this development environment.
- SMART health percentages are intentionally absent. Storage sensors, if available from LibreHardwareMonitor, are shown as named measurements rather than inferred overall health.
- Frame capture is a real bundled PresentMon ETW process. Games and ETW permissions can restrict access. No real game was available on the macOS development host; a gaming capture must be validated on a suitable Windows computer. See GAMING.md.
- The desktop overlay is a noninjected topmost window. Exclusive fullscreen and protected games may cover it. The global hotkey may conflict with other software. Presets and opacity/position are supported; per-metric overlay selection is under development.
- The procedural model depicts a representative ATX system, not a scanned model of the detected hardware. Fans are stationary because individual scene fans cannot be reliably mapped to specific real sensors. Hardware changes and sensor reset recovery require physical tests.
- Charts select time ranges and allow hover inspection and pause. In-app session comparison/import and arbitrary drag-to-zoom are not yet implemented; CSV exports permit external comparison. Longer disk history is readable in the source API and JSONL files; a historical-session browser is not yet integrated.
- Memory history is bounded to 14,400 samples, not a guaranteed duration at every refresh interval. Long-term records are bounded and off by default. Detailed sensor counts can increase memory/CPU costs.
- Native high-DPI/resizing, keyboard navigation, tray, local notification banners and appearance settings are implemented. Screen reader, multiple displays, sleep/resume, high-DPI and long-running stability require interactive Windows validation.
- No fixed performance overhead guarantee is made. CI records launch working set and CPU time on its own runner. Gaming impact and hardware sensor accuracy must be measured on a real target machine against Task Manager and established tools.
- Installer is unsigned. It bundles .NET and Windows App SDK, but uses the official online WebView2 bootstrapper if Windows lacks that runtime. This is disclosed during setup.
