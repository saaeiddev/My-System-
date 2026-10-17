# Third-party notices

My System source is MIT licensed. Bundled dependencies retain their own licenses.

| Component | Version | License / source |
|---|---|---|
| .NET runtime | 10.0, SDK 10.0.402 | MIT, https://github.com/dotnet/runtime |
| Windows App SDK | 2.5.1 | MIT, https://github.com/microsoft/WindowsAppSDK |
| Microsoft WebView2 SDK/runtime | 1.0.4258.31 / Evergreen | Microsoft redistribution terms, https://www.nuget.org/packages/Microsoft.Web.WebView2 |
| LibreHardwareMonitorLib | 0.9.6 | MPL-2.0, https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/tree/v0.9.6 |
| PresentMon console | 2.6.0 | MIT, https://github.com/GameTechDev/PresentMon/tree/v2.6.0 |
| Three.js | 0.186.1 | MIT, https://github.com/mrdoob/three.js |
| esbuild (build only) | 0.28.2 | MIT, https://github.com/evanw/esbuild |
| xUnit, test SDK (test only) | pinned in tests project | Apache-2.0 / MIT, see NuGet metadata |
| Inno Setup (build tool) | runner package | https://jrsoftware.org/files/is/license.txt |

LibreHardwareMonitor is included unmodified. Its corresponding source is available at the exact tag linked above, and the MPL license and upstream third-party notices are included in `licenses/`. No PawnIO installer or driver is bundled or installed by My System. Refer to the upstream notices for sensor library transitive components.

The renderer bundle includes Three.js's MIT notice in `Scene/licenses/`. Windows and .NET dependency notices are retained in the self-contained publish output where supplied by their packages. Microsoft WebView2 remains subject to Microsoft's runtime redistribution and update terms; the installer runs its official signed bootstrapper only if the runtime is absent.

Original procedural 3D hardware geometry, material palettes, and icon artwork are included in this repository under the project MIT license. The app uses system Segoe UI and Consolas fonts; no proprietary font files are redistributed.
