# Embedded hardware atlas

This directory builds the **3D visualization component** of the native Windows
application. It is not the system monitoring UI or a browser-based monitor.
The native application loads `dist/index.html` through a WebView2 virtual host
mapping. Every JavaScript dependency and model is bundled locally.

```sh
npm ci
npm test
npm run build
```

The Windows build copies `dist/` into `MySystem.App/Scene/`. Build tooling requires
Node.js 24 and network access for package restore; runtime uses no network.

## Native bridge

The only inbound receiver is `window.chrome.webview`'s `message` event. There is
no global `window.postMessage` receiver and no exposed native host object.

```json
{"type":"telemetry","metrics":[{"id":"cpu.usage","value":31.4,"unit":"%","availability":"Available"}],"thresholds":{"cpu":85,"gpu":83}}
```

`Available` is case-insensitive; numeric enum `0` is supported. Unsupported,
non-finite, null, or absent values display N/A. Recognized domains include
`cpu.usage`, `cpu.temperature`, `gpu.usage`, `gpu.temperature`, `memory.used`,
`disk.activity`, and `motherboard.temperature`. Thermal indicators only use a
supported temperature with Celsius units. Thresholds are configurable display
preferences, not universal hardware safety limits. A 15-second bridge silence
clears values to avoid showing an old measurement as live.

```json
{"type":"config","quality":"medium","fpsCap":30,"reducedMotion":true,"paused":false,"appearance":"dark","accent":"#50d9c3"}
{"type":"select","component":"gpu"}
```

Components are `overview`, `cpu`, `gpu`, `memory`, `storage`, `motherboard`, and
`cooling`. Clicking geometry/buttons sends `{type:"select",component:"gpu"}`.
Initial setup sends `{type:"ready",version:1}`; failures send `rendererError`.
Native code should validate message type/component and disallow navigation
away from its local virtual host. Render settings are type checked and bounded.

## Rendering and accessibility

Orbit: pointer drag or arrow keys on canvas. Pan: right-drag. Zoom: wheel,
toolbar, or +/- keys. Reset: R. Isolation: I after component selection. All
toolbar controls are keyboard accessible; live selection announcements are
available to assistive technologies. The native monitoring UI remains the
primary accessible route to the measurements.

Frames are rendered only after an interaction, telemetry change, or resize.
The FPS cap controls **visualization** rendering and has no relation to game
FPS. High quality enables shadows and up to 2× pixel ratio; medium uses 1.5×;
low uses 1×. Damping is disabled for reduced motion. Hidden documents and the
native `paused` signal cancel rendering. Local Pause is tracked independently,
so native resume does not undo a user's explicit pause. WebGL context loss has
a clear empty state while the native UI continues. Geometry, materials,
environment texture, controls, timers, and renderer are disposed on page hide.

The procedural assembly is described in `../../assets/3d-models/README.md`.
Fans are stationary; no unsupported sensor is represented by invented motion.

## Validation boundaries

Node tests verify unavailable-data handling, Celsius threshold mapping, native
metric routing, configuration validation, model structure/bounds and CSP. They
do not establish actual GPU overhead, Windows WebView2 compatibility, touch
behavior, or screen-reader quality. Those require the Windows hardware QA pass.

Upstream documentation: [Three.js](https://threejs.org/docs/),
[WebView2 web messaging](https://learn.microsoft.com/microsoft-edge/webview2/how-to/communicate-btwn-web-native).
