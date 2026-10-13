# HANGAR — by Contoso Aero

> *"It's 2:40am. One of 42 aircraft just landed with a problem — and it leaves at 6:15."*

HANGAR is a Windows line-of-business showcase built for an Ignite 2026 keynote demo. It shows a night-shift maintenance controller taking aircraft **CA 2207 (N2207C, A321neo)** from an Engine 2 vibration alert to release-to-service in about three minutes.

The full product and design spec is in [`docs/concept.md`](docs/concept.md), which covers the story, the "Night Shift" design language, the screens and the stage script.

The app exists in three implementations of the same UI, so this repo doubles as a framework comparison: the original **WinUI 3** app, a port written fully in **Reactor** (C# function components on WinUI), and an **Electron** port used as the benchmark baseline.

| WinUI 3 | Reactor | Electron |
|---|---|---|
| ![WinUI](docs/screenshots/winui/fleet.png) | ![Reactor](docs/screenshots/reactor/fleet-dark.png) | ![Electron](docs/screenshots/electron/fleet-dark.png) |

## Implementations

| | Folder | Stack | Run |
|---|---|---|---|
| **WinUI 3** (reference) | [`src/Hangar.WinUI`](src/Hangar.WinUI) | XAML + MVVM, WinAppSDK 2.5.4-experimental, Win2D, Native AOT | `cd src\Hangar.WinUI; winapp run .` |
| **Reactor** | [`src/Hangar.Reactor`](src/Hangar.Reactor) | Microsoft.UI.Reactor function components (no XAML); shares models, services and the Win2D stage with the WinUI project. Notes: [`PORTING.md`](src/Hangar.Reactor/PORTING.md) | `cd src\Hangar.Reactor; winapp run .` |
| **Electron** | [`src/Hangar.Electron`](src/Hangar.Electron) | Electron 41, plain HTML/CSS/JS + Canvas 2D | `cd src\Hangar.Electron; npm ci; npm start` |

Both .NET projects are in [`Hangar.slnx`](Hangar.slnx). The WinUI and Reactor builds are packaged (MSIX loose layout) and use different package identities, so they can be installed side by side.

## Benchmark summary

Cold start to first frame, working set after a scripted page tour, and on-disk size (Surface Pro, Snapdragon X, ARM64). Full method and raw numbers: [`docs/benchmark.md`](docs/benchmark.md); harness in [`bench/`](bench).

| Build | Startup | Memory | Size on disk |
|---|---|---|---|
| WinUI, Native AOT | **461 ms** | **413 MB** | **59.5 MB** |
| WinUI, ReadyToRun | 531 ms | 476 MB | 236 MB |
| Electron | 596 ms | 1,077 MB | 346 MB |

## Repo layout

```
Hangar.slnx              WinUI + Reactor projects
src/
  Hangar.WinUI/          the reference WinUI 3 app (package identity CFF58EE2-…)
  Hangar.Reactor/        Reactor port (links shared code from Hangar.WinUI)
  Hangar.Electron/       Electron port (npm project)
bench/                   benchmark script and results JSON
docs/
  concept.md             product + design spec
  demo-script.md         3-minute stage script
  benchmark.md           WinUI (AOT / R2R) vs Electron
  screenshots/{winui,reactor,electron}/
```

## The WinUI app

| Fleet | Aircraft (hologram) |
|---|---|
| ![Fleet](docs/screenshots/winui/fleet.png) | ![Aircraft](docs/screenshots/winui/aircraft.png) |
| **Work Orders (TableView)** | **Inspections (InkCanvas)** |
| ![Work Orders](docs/screenshots/winui/workorders.png) | ![Inspections](docs/screenshots/winui/inspections.png) |

Other screenshots in [`docs/screenshots/winui`](docs/screenshots/winui):
- **Engine 2 exploded:** `aircraft-exploded.png`, `aircraft-exploded-light.png`
- **Part zoom on the aircraft page:** `aircraft-fan.png`, `aircraft-turbine.png`
- **Fleet globe:** `aircraft-globe.png` (compact card, scrubbed to flight 44), `aircraft-globe-expanded.png`, `aircraft-globe-light.png`
- **After sign-off:** `inspections-signed.png`
- **Day-shift (Light) versions of each page:** `*-light.png`

### What's inside

| Page | Highlights |
|---|---|
| **Shell** | <ul><li>Mica Alt backdrop and an extended title bar with search (Ctrl+K) and a live departure-countdown chip</li><li>Store-style rail **NavigationView** (72px icon-over-label rail with a sliding selection pill, adapted from AI Dev Gallery's custom template; `Themes/NavigationView.xaml`)</li><li>Dark-first, with full Light and HighContrast dictionaries</li><li>`SystemAccentColor` is overridden to Runway Cyan `#3AD1F0`; Safety Amber `#FFB020` is reserved for attention states</li></ul> |
| **Ask Hangar** | <ul><li>✦ title-bar button or **Ctrl+Space**: a 420px acrylic pane slides in from the right. It is an on-device AI assistant on the NPU, so it works with no network, which suits Maya's no-cloud, flaky-Wi-Fi hangar. Backends are tried in order: **Aion Instruct (preview)** (opt-in, see below) → **Phi Silica** (`Microsoft.Windows.AI.Text.LanguageModel`) → scripted answers</li><li>Grounded in live app data (`Services/AskContextBuilder`): current page and selection, the Engine 2 vibration story (flight 44 hail at FL190 → 1.32 ips, 2.31 ips at flight 50 vs the 1.8 limit), open Engine 2 WOs, AMM 72-00 / 72-21, and last year's same fault on CA 1916</li><li>Tokens stream in with a blinking caret, and a "Thinking on NPU…" shimmer shows before the first token</li><li>Page-aware suggestion chips; typing in search offers **Ask Hangar: &lt;query&gt;**</li><li>Answers end in **action chips**: *Flight 44* scrubs and expands the globe, *Fan blade 14* explodes Engine 2 and selects the blade, *WO* chips filter the TableView, and *Borescope* opens Inspections. In live mode the chips are chosen deterministically from keywords, never trusted from model output</li><li>On Inspections, **Draft the logbook entry** turns the shorthand into a release entry and **Use in sign-off** pre-fills the notes. It never signs: *AI drafted · Engineer signs*</li><li>The badge is honest about the mode: **On-device · Aion Instruct (preview) · NPU**, **On-device · Phi Silica · NPU**, or **Demo responses** with a tooltip giving the reason</li></ul> |
| **Fleet** | <ul><li>4 KPI cards with sparklines</li><li>Hero 30-day availability **area Chart** with a dashed target line</li><li>ItemsRepeater grid of 42 aircraft, where CA 2207 breathes amber</li><li>Connected animation from a tile to the Aircraft page</li></ul> |
| **Aircraft** | <ul><li>Canvas-style hero layout with a **Tron-style holographic wireframe A321neo** (Win2D)</li><li>Drag to orbit, wheel to zoom, idle auto-rotate</li><li>Live **airflow** streaks through both engines: bypass streams through the fan duct, and core streams that swirl through the compressor and heat up past the combustor (they fade out when exploded)</li><li>**Tap Engine 2** (or press **Explode**) to fly the camera to a 3/4 technical-illustration view. Engine 2 then bursts into 50+ LEAP-1A parts: spinner, 22 fan blades, OGVs, booster, HPC stages, combustor, HPT/LPT stages, N1/N2 shafts, clamshell thrust reversers, exhaust and gearbox. The burst uses a staggered spring cascade with motion trails and an energy pulse; collapsing reverses the cascade</li><li>Once settled, the exploded view shows module-level callouts. Fan blade 14 turns **Critical red** (amber while collapsed = attention) with a red glow and finding callout; the rest of the aircraft fades back. Tap empty space or press Esc to collapse</li><li>**Tap the red blade tip** (or **Borescope blade 14**, or Enter) to dive the camera into the fault with a red flash; the stage then drills in to Inspections</li><li>Chips (Nacelle, Fan, Compressor, Turbine) isolate a module, fly the camera to it and switch to per-part labels with a finding callout</li><li>Glass side panels hold vitals and an Engine 2 N1 vibration **line Chart** with a fleet-average band, a limit line and an amber spike, plus a flight scrubber</li><li>**Fleet globe** (Win2D, `Controls/FleetGlobe`): an orthographic dot-matrix Earth in the same hologram language. It shows great-circle red-eye arcs, 12 airborne aircraft drawn as comets with trails, parked aircraft as dim dots, and CA 2207 as the only amber point, breathing at SEA. The flight scrubber drives it: CA 2207's leg for the scrubbed flight is drawn as an arc coloured by that flight's N1 vibration (cyan, then amber at ≥ 1.4 ips, then red above the 1.8 limit), and the globe springs to centre on that leg. **Flight 44 (DEN → SEA)** shows the crew report *"Hail encounter on climb, FL190"* as a callout chip and as a diamond marker on the vibration chart. Tap the card or press **G** to expand the globe to the full stage; the hologram shrinks toward SEA. Drag to spin and wheel to zoom; Esc or **Back to aircraft** dives back in. `AircraftPage.ScrubToFlight(int)` and `ExpandGlobe()` let other features (for example, a future *Ask Hangar* citation chip) drive it</li></ul> |
| **Work Orders** | <ul><li>First-party **TableView** with 2,400 seeded rows</li><li>Grouped by component, with templated group headers showing open count, a progress bar and hours</li><li>Filter chips, sortable headers, status pills, PersonPicture technicians and Cascadia Mono part numbers</li><li>Inline Hours editing with validation (> 0)</li><li>A **Show on aircraft** action</li></ul> |
| **Inspections** | <ul><li>Framed borescope viewport: ink is confined to the image, and the **InkToolbar** (pen, highlighter, eraser, ruler) and sign-off panel sit outside it</li><li>Arriving from the hologram, the image settles in and a red reticle lands on the blade 14 nick; a breadcrumb leads back to the exploded engine</li><li>Strokes can be saved and restored</li><li>Signing the ink signature pad and choosing **Sign & release** adds or updates WO 2207-014, flips CA 2207 to green across the app, and plays one celebratory composition moment</li></ul> |

### Tech stack

- **.NET 10**, `net10.0-windows10.0.26100.0`, packaged (MSIX), MVVM with **CommunityToolkit.Mvvm**
- **Microsoft.WindowsAppSDK 2.5.4-experimental**, which provides the first-party `TableView`, `Chart` (line/area/bar) and `InkCanvas`/`InkToolbar`
- **Microsoft.Graphics.Win2D 1.4.0** for the hologram renderer (Aircraft page)
- Local deterministic seeded data (`Services/`), with no network access
- The window is sized for **2560×1600 @ 150%**

```
src/Hangar.WinUI/
  Views/        Fleet, Aircraft, WorkOrders, Inspections pages
  ViewModels/   CommunityToolkit.Mvvm view models
  Services/     FleetService (seeded data, demo state, reset)
  Controls/     AircraftStage (Win2D hologram),
                KPI/sparkline controls
  Helpers/      WireMesh (procedural wireframe), ThemeRes, converters
  Themes/       Semantic brushes: Dark (Default) / Light / HighContrast
```

### Build & run

Prerequisites:
- Windows 11 24H2 or later, with Developer Mode on
- .NET 10 SDK
- WinApp CLI 0.7+, installed with `winget install Microsoft.WinAppCli`
- Node.js 20+ (Electron port only)

```powershell
cd src\Hangar.WinUI
dotnet build -c Debug -p:Platform=ARM64   # or x64
winapp run .                              # registers the loose layout and launches
```

You can also open `Hangar.slnx` in Visual Studio 2026 and press F5 with the packaged profile.

If a redeploy fails with `0x80073CF9`, pass `--output-appx-directory "$env:LOCALAPPDATA\winapp-layout\Hangar-Debug"` to `winapp run`.

### Keyboard

| Keys | Action |
|---|---|
| **Ctrl+Shift+D** | Hidden **demo reset**: restores the 02:40 state (CA 2207 on hold, signature cleared, ink cleared, filters reset) |
| Ctrl+Shift+L | Toggle Night shift / Day shift (Dark / Light) |
| Ctrl+K | Focus search |
| **Ctrl+Space** | Toggle the Ask Hangar pane (Enter sends, Esc closes) |
| Ctrl+Shift+A | Hidden: force Ask Hangar into scripted (Demo responses) mode, or back to live |
| Ctrl+E | Explode / collapse Engine 2 |
| Arrows / Home / Esc | Orbit, reset camera, deselect part (Aircraft stage focused) |
| G / Esc | Expand the fleet globe / dive back into the aircraft (Aircraft page) |
| Arrows / +/− / Home | Spin, zoom, re-centre the expanded globe |

The 3-minute stage script is in [`docs/demo-script.md`](docs/demo-script.md).

### Known limitations & risks (WinUI)

#### 3D (Composition Scenes)
The spec called for `Microsoft.UI.Composition.Scenes` (SceneVisual with mesh nodes). A spike showed that **procedural meshes do not render on WinAppSDK 2.5.4-experimental**: the SceneVisual is created, but no geometry appears. The spike is in commit `9033415`.

The documented fallback is a **Win2D wireframe hologram** (`Controls/AircraftStage`, `Helpers/WireMesh.cs`). It has these properties:
- Its geometry is procedural, with no glTF asset.
- It uses perspective projection and dark-theme bloom with scanlines.
- It provides per-part hit-testing, camera fly-to and an exploded view driven by springs.
- It honours `UISettings.AnimationsEnabled` and draws plain lines in Light and HighContrast.

The result is "real 3D" in the sense of a projected 3D wireframe; it is not a lit or shaded mesh.

#### Experimental controls (WinAppSDK 2.5.4-experimental)

**TableView**
- Template cells need `Margin="10,0,0,0"` to align with text columns.
- There is no `ScrollIntoView`.
- `SelectedItem` is get-only, so selection is driven via the view model.

**Chart**
- The Y axis must have `IsVisible="True"` for `Minimum`/`Maximum` to apply.
- Tick `Spacing` must be ≤ the axis range.
- Gradient fills are ignored, so a solid fill with opacity is used instead.
- An `AreaSeries` stroke outlines the whole polygon, so a separate `LineSeries` draws the top edge.
- Theme resources are only read from the PRI.
- KPI **sparklines are XAML `Path`s** rather than Charts, to keep 4 extra chart instances off the Fleet page.

**Packaging**
- The experimental Charts/Tabular `.pri` resources are not merged automatically.
- The `HangarMergeExperimentalControlPri` target in `Hangar.csproj` injects them from `microsoft.windowsappsdk.winui\2.3.10-experimental`.
- If the package version changes, update that path.

**Release builds**
- Trimming may break the few `{Binding}` usages inside TableView templates.
- Demo from **Debug**, or validate Release before stage day.
- `WUI2011` analyzer warnings are expected.

#### Fleet globe
- The land mask is baked at dev time from **Natural Earth 1:110m land** (public domain, [naturalearthdata.com](https://www.naturalearthdata.com/)) into a 1.5° lat/lon dot grid (`Assets/Globe/land-dots.bin`, about 5,200 dots, embedded resource). Nothing is downloaded at runtime.
- Network data (airports, CA 2207's 50 legs and the 02:40 PT red-eyes) is deterministic and seeded in `FleetDataService`. Airborne progress advances slowly in real time from the demo-reset moment.
- The globe draws with `CanvasAnimatedControl`. It pauses when not visible and drops to 30 fps while idle in compact mode.

#### Ink
- There is no handwriting recognition: the note is kept as ink.
- `RenderTargetBitmap` does not capture InkCanvas strokes, so the signature preview on the work order is re-drawn from stroke points as `Path`s.

#### Ask Hangar (on-device models)
![Ask Hangar on the Aircraft page](docs/screenshots/winui/ask-hangar.png)
![Ask Hangar drafting the logbook entry](docs/screenshots/winui/ask-hangar-logbook.png)
![Ask Hangar in light theme](docs/screenshots/winui/ask-hangar-light.png)

`Services/LocalModelService` walks a chain of `IAskModel` backends and uses the first one that initializes: **Aion Instruct (preview)** → **Phi Silica** → `ScriptedResponder`. Grounding (`AskContextBuilder`: a system prompt with the FACTS, and a user prompt with the quoted question and the answer-only instruction last) and the `[[chip]]` contract are the same for every backend.

##### Aion Instruct (preview), opt-in
![Ask Hangar answering with Aion Instruct (preview)](docs/screenshots/winui/ask-hangar-aion.png)
![Ask Hangar with Aion on Work Orders](docs/screenshots/winui/ask-hangar-aion-workorders.png)

[Aion Instruct](https://aka.ms/tryaion) is the on-device successor to Phi Silica. It ships as a **developer-validation preview**.

> [!WARNING]
> Preview only. **Don't ship it, and don't commit the nupkg or the framework MSIX.** Output is watermarked and non-deterministic, so live answers vary between runs (the chips and the logbook draft stay deterministic). It needs an ARM64 Snapdragon (QNN) NPU.

**Run it on your machine (once per machine).** From the repo root, either:
- run `powershell -ExecutionPolicy Bypass -File scripts\Setup-Aion.ps1` (add `-CheckOnly` to only diagnose), or
- ask GitHub Copilot: *"set up Aion for Hangar"*. The [`hangar-aion-setup`](.github/skills/hangar-aion-setup/SKILL.md) skill makes Copilot run the script, fix what it reports, build and verify.

The script is idempotent. It checks and fixes Developer Mode, Windows App Runtime 2, the Aion framework MSIX 1.0.0.3, the SDK nupkg in the git-ignored `nuget-local\`, a stale NuGet cache entry and the QNN execution provider. It then builds and confirms that `Package.appxmanifest` stays clean. Then run `winapp run .` from `src\Hangar.WinUI`, press **Ctrl+Space**, and allow ~5 min for the first NPU compile.

What it automates (manual equivalent):
1. Install Windows App Runtime 2 and the Aion framework MSIX from the [v1.0.0.3 release](https://github.com/microsoft/Aion-Instruct-Preview-Sample/releases/tag/v1.0.0.3) (or run the sample's `Bootstrap.ps1`).
2. Put `AionInstructPreview.Text.Framework.1.0.1.nupkg` into `nuget-local\` at the root of this repo. The folder is git-ignored.
3. Build as usual. `Hangar.csproj` sees the nupkg and sets `EnableAion=true`, which:
   - defines `HANGAR_AION`, so `Services/AionModel.cs` compiles;
   - references the Aion SDK and `Microsoft.Windows.CsWinRT` 2.1.5 (it resolves to 2.2.0; NU1603 is suppressed);
   - adds the Aion framework and Windows App Runtime 2 package dependencies to the **generated** manifest only. The `HangarAionRedirectManifest` target points the SDK's manifest injection at a copy in `obj\…\aion\`, so the committed `Package.appxmanifest` stays clean.

   Force it either way with `-p:EnableAion=true|false`. Without the nupkg, the app builds and runs exactly as before (Phi Silica → scripted) on machines without the Aion framework.

Runtime behaviour:
- Streaming progress delivers **deltas** (Phi Silica sends the running total). Each backend normalises this to deltas for the pane.
- Until the model is ready, the badge reads **"Warming up the NPU…"** and a question waits (up to 25 s) under the same label rather than erroring.
- After `CreateAsync`, the app runs one short **warm-up generation** in the background. A **cold** NPU cache (first run after installing the framework) compiles the model for minutes (about 4.5 min measured on a Snapdragon X Elite). Warm, `CreateAsync` takes about 8–14 s and the first token arrives in about 150 ms. See the pre-stage step in [`docs/demo-script.md`](docs/demo-script.md).
- If the first generation fails with `E_FAIL` after a cold compile, it is retried once automatically.
- If the framework isn't installed (`REGDB_E_CLASSNOTREG`), or creation fails, the chain moves on to Phi Silica.

##### Phi Silica
- Live mode requires a **Copilot+ PC** with an NPU, the Phi Silica model installed via Windows Update, and the `systemAIModels` restricted capability (already in `Package.appxmanifest`).
- **Limited Access Feature unlock** (`com.microsoft.windows.ai.languagemodel`) is needed on some builds. Credentials come from one of:
  - the environment variables `HANGAR_LAF_TOKEN` and `HANGAR_LAF_ATTESTATION`;
  - an `ask-hangar.local.json` file (`{ "lafToken": "...", "lafAttestation": "..." }`) in the package `LocalState` folder or next to the exe.

  The file is git-ignored. **Never commit the token.** Without credentials the app still tries `GetReadyState()` and falls back if the feature is locked.
- **Fallback**: if the device is not supported, the model is not ready, the LAF is locked, content moderation blocks the answer, or an exception occurs, `ScriptedResponder` streams deterministic grounded answers at about 25–40 ms per token.
  - The badge then reads **"Demo responses"**, with the reason in its tooltip. When live, it reads **"On-device · Aion Instruct (preview) · NPU"** or **"On-device · Phi Silica · NPU"**.
  - **Ctrl+Shift+A** forces scripted mode for stage safety.
- Citation chips (`[[flight:44]]`, `[[part:fanblade14]]`, `[[wo:…]]`, `[[nav:…]]`) are always appended deterministically from a keyword match on the answer and grounding context. Model-emitted markers are never trusted.
- The logbook draft only **pre-fills** the Inspections release entry ("AI drafted · Engineer signs"). The engineer still signs.
- Diagnostics are logged to the debug output with the `[AskHangar]` prefix.

#### Testing
- Pointer flows were verified manually and through the debug start hook, not with UI automation; the dev desktop was shared. These flows are drag-orbit, ink, signature and connected animation.
- Run the full script on the stage device before the keynote.
- **Debug start hook** (Debug builds only): put a `startpage.txt` in the package `LocalState` folder containing, for example, `Aircraft light fan` or `Inspections dark sign`. The app then opens directly to that state. It was used to capture the screenshots. Delete the file afterwards.
