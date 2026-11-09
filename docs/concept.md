# HANGAR — by Contoso Aero
### A WinUI 3 line-of-business showcase for Ignite 2026

> **"Every hour a plane sits on the ground costs $10,000. Hangar gets it back in the air."**

---

## 1. The story

It's 02:40 at Contoso Aero's maintenance base. Flight **CA 2207** lands with a cockpit alert: *Engine 2 – vibration exceedance*. The aircraft is due out again at 06:15 with 180 passengers.

**Maya Okafor**, shift lead and licensed engineer, has three and a half hours. She has a Surface Pro, a hangar full of noise, and one app: **Hangar**.

In under three minutes on stage, we follow Maya from *alert → diagnosis → inspection → sign-off*. Every step is a native WinUI moment the audience hasn't seen before:

| Beat | What Maya does | WinUI hero |
|---|---|---|
| **1. The Fleet** | Opens Hangar. 42 aircraft, one is pulsing amber. | **Chart** (area + line), Mica, connected animation |
| **2. The Aircraft** | Taps CA 2207. The A321neo spins up in 3D; Engine 2 glows. She pulls it apart into an expanded view. | **Composition 3D** (SceneVisual + glTF), implicit animations |
| **3. The Evidence** | Engine 2's vibration trend overlays the fleet average — the spike is obvious. She scrubs the timeline. | **Chart** (line, markers, annotations) |
| **4. The Paper Trail** | 2,400 historic work orders. She groups by component, filters "fan blade", sorts by date — finds the same fault 11 months ago. | **TableView** (group / filter / sort / edit) |
| **5. The Inspection** | Borescope photo of blade 14. She circles a nick with the pen, writes "0.4mm – within limits", measures it. | **InkCanvas + InkToolbar**, Surface Pen |
| **6. The Sign-off** | Ink becomes a structured work-order row; she signs with the pen. The aircraft turns from amber to green. Out at 05:52. | TableView edit, ink signature, celebratory composition moment |

**Why this story wins on stage:** real stakes (a clock), a human hero, a pen in hand, and a 3D object you can *touch*. It's LOB, but it feels like a product launch.

---

## 2. Design direction — "Night Shift"

The hangar at 3am: dark, precise, lit by task lights and safety amber. Calm, confident, engineering-grade. Think *Flighty* meets *Windows 11*.

### Palette
| Role | Token | Value (dark) | Notes |
|---|---|---|---|
| Canvas | `AppCanvasBrush` | Mica Alt, tinted `#0B0F14` | Deep blue-black, never pure black |
| Surface | `CardBackgroundBrush` | Layer on Mica, 4% white | Standard WinUI card layering |
| Accent | `SystemAccentColor` override | **Runway Cyan `#3AD1F0`** | Selection, primary actions, healthy trends |
| Warning | `AttentionBrush` | **Safety Amber `#FFB020`** | The *only* warm color — reserved for "needs you" |
| Critical | `CriticalBrush` | `#FF5A5F` | Used sparingly; AOG (aircraft on ground) only |
| Healthy | `HealthyBrush` | `#4ADE80` | Sign-off / airworthy |

Light theme is fully supported ("Day Shift": soft grey Mica, same accents tuned for contrast). High Contrast maps to system colors — no exceptions.

### Typography
- **Segoe UI Variable Display** at 68px semibold for hero numbers (*"3h 34m to departure"*).
- **Segoe UI Variable Text** for everything else; tabular numerals in TableView and Chart axes.
- Tail numbers and part numbers in **Cascadia Mono** — it reads as "engineering truth".

### Motion principles
1. **Things have mass.** The 3D aircraft eases with spring physics; nothing snaps.
2. **Continuity over cuts.** Fleet tile → aircraft hero uses connected animation; the tail number literally flies into the header.
3. **Amber breathes.** Attention states pulse slowly (2.4s) — urgent, not alarming.
4. **One celebration.** Sign-off gets a single, earned moment: the amber ring resolves to green and the aircraft does a gentle 15° turntable "taxi out". Nowhere else.

### Silhouette
- `NavigationView` **LeftCompact** with 4 destinations: **Fleet · Aircraft · Work Orders · Inspections**.
- Extended title bar with an `AutoSuggestBox` ("Search tail, part, or work order") and a live **departure countdown** chip.
- Aircraft page is a **hero/canvas** layout: full-bleed 3D, floating command bar, glass side panels — no boxed-in chrome.

---

## 3. Screens

### 3.1 Fleet (dashboard)
- **Top strip:** four KPI cards — *Fleet availability 97.6%*, *AOG 1*, *Open work orders 128*, *On-time departure 94%*. Each with a sparkline **Chart** (area, no axes).
- **Hero chart:** 30-day fleet availability (area) vs. target (dashed line), with today's dip annotated.
- **Fleet grid:** `ItemsRepeater` of aircraft tiles — silhouette icon, tail number in mono, status ring. CA 2207 breathes amber.

### 3.2 Aircraft — CA 2207 (the "wow" page)
- **Center:** the A321neo in 3D on a subtle reflective floor with soft ambient occlusion. Drag to orbit, pinch to zoom, pen to point.
- **"Explore" toggle:** Engine 2 separates into nacelle → fan → compressor → turbine, each part a selectable node. Selecting a part highlights it in cyan and filters everything else in the app to it.
- **Left glass panel:** aircraft vitals (cycles, hours, next check).
- **Right glass panel:** live **Chart** — Engine 2 vibration (N1) over the last 50 flights with fleet-average band, threshold line, and the spike marked in amber.
- **Bottom timeline scrubber:** scrubbing rewinds the vibration chart and the 3D engine's heat overlay together.

### 3.3 Work Orders
- **TableView**, ~2,400 rows, virtualized. Columns: WO#, Tail, Component, ATA chapter, Finding, Technician, Status, Opened, Hours.
- Grouped by **Component** with templated group headers showing count + mini bar chart of hours.
- Filter chips above: *Fan blade · Last 12 months · Closed*. Column-header sort with indicators.
- Custom cell templates: status pills, technician avatars (`PersonPicture`), mono part numbers.
- Inline editing with validation (e.g. hours must be > 0).
- Selecting a row with a 3D-mappable component offers **"Show on aircraft"** → jumps to the 3D view with that part isolated.

### 3.4 Inspections
- Borescope image full-bleed, **InkCanvas** overlaid, **InkToolbar** floating (pen, highlighter, ruler, eraser).
- Ruler stencil used to measure the nick — the "measure with the pen" moment lands with every Surface user in the room.
- Handwritten note is converted to text (stretch: Windows AI text recognition) and becomes a structured finding.
- **Sign-off sheet:** ink signature captured, saved with the work order, rendered as a thumbnail in the TableView row.

---

## 4. The 3-minute stage script

| Time | On screen | Presenter line |
|---|---|---|
| 0:00 | Fleet dashboard, one amber tile breathing | *"It's 2:40am. One of 42 aircraft just landed with a problem — and it leaves at 6:15."* |
| 0:20 | Tap tile → connected animation → 3D aircraft | *"This is WinUI. Native composition, real 3D, on a Surface."* |
| 0:45 | Orbit, tap Engine 2, **Explore** | *"Let's look inside the engine."* (applause beat) |
| 1:05 | Vibration chart, scrub timeline | *"The new WinUI Chart control — line, area, markers, all first-party."* |
| 1:25 | Work Orders: group, filter, sort to find last year's fault | *"And yes — WinUI has a TableView now. 2,400 rows, grouped, filtered, editable."* (biggest applause beat for devs) |
| 1:55 | Inspection: pen circles nick, ruler measures, handwrites note | *"Ink is back in WinUI 3. The pen is still the best tool in the hangar."* |
| 2:30 | Signature → row appears → aircraft turns green, taxis | *"Signed, logged, airworthy. Wheels up at 5:52."* |
| 2:50 | Pull back to fleet: all green | *"Built entirely with WinUI and the Windows App SDK. Open source — go build yours."* |

---

## 5. Technical blueprint

| Area | Approach |
|---|---|
| Framework | WinUI 3, **Windows App SDK 2.5 Experimental** (TableView + Chart), .NET 10, C#, MVVM via CommunityToolkit.Mvvm |
| Data | Local SQLite seeded with realistic synthetic data (42 aircraft, 2,400 WOs, 50 flights of telemetry per engine). No network dependency on stage. |
| TableView | Grouping, sort, filter, edit+validation, templated cells. Bound to `ObservableCollection` view-models. |
| Chart | Area (fleet availability, sparklines), Line (vibration vs. band), Bar (hours by component in group headers). |
| Ink | `InkCanvas` + `InkToolbar` with ruler stencil; strokes serialized (ISF) per inspection; signature capture. |
| 3D | `Microsoft.UI.Composition` Scenes — `SceneVisual` hosting a glTF model of an A321neo + expanded engine (separate mesh nodes). Spring animations for expand/orbit. Fallback: pre-rendered turntable if Scenes isn't viable — *validate early*. |
| Shell | Mica Alt backdrop, extended title bar, NavigationView LeftCompact, connected animations, Light/Dark/HC theme dictionaries. |
| Stage mode | Hidden `Ctrl+Shift+D` "demo reset" that restores the 02:40 state; deterministic data; 2560×1600 at 150% tuned layout. |

### Risks to burn down first
1. **3D pipeline** — confirm Composition Scenes + glTF loading on WinAppSDK 2.5; source/licence a clean aircraft model (or commission a stylized low-poly one, which also looks more "designed").
2. **Experimental controls** — pin the exact 2.5-experimental package; track API churn weekly.
3. **Ink → text** — stretch goal; demo works without it.

---

## 6. Name & tagline options
- **Hangar** — *"Turnaround, reimagined."* ← recommended
- **Aerolith** — *"Engineering-grade, Windows-native."*
- **Turnaround** — *"From alert to airworthy."*

---

## 7. New beats (v2)

### 7.1 "The Route" — a fleet globe that explains the spike
The vibration chart shows *when* Engine 2 went bad. The globe shows *where* and *why*.

- **Where:** On the Aircraft page, a **globe card** sits under the Vitals panel (about 340×280). It's a Win2D dot-matrix Earth drawn in the same Tron-cyan hologram language as the plane: land as dots, a faint graticule, and a rim glow.
- **Live fleet:** It's 02:40 Pacific, so about 12 Contoso red-eyes are airborne. They show as small cyan comets moving along great-circle arcs, with fading trails. The parked aircraft are dim dots at their stations. **CA 2207 is the only amber point**, pulsing at SEA.
- **Scrub link:** Moving the flight scrubber (FLT 1–50) draws CA 2207's leg for that flight as an arc, with a plane glyph and the airport codes. The arc takes its colour from that flight's N1 vibration: cyan when normal, amber near the limit, Critical red above 1.8 ips. The globe springs round to centre on the leg, and the vibration chart's marker moves in sync.
- **The reveal:** Scrub to **flight 44, DEN → SEA**. That arc turns red, and a callout appears: *"Crew report: hail encounter on climb, FL190."* Every flight after 44 trends up. That's the cause of the fan blade 14 damage, which leads straight into the borescope.
- **Expand:** Tap the card, or press **G**, and the hologram plane shrinks into its dot at SEA while the camera pulls back to orbit. The globe fills the stage, and you can drag to spin it. Esc dives back into the aircraft.
- **Presenter line:** *"The same Win2D pipeline draws the whole network. Forty-two aircraft, live, and the one leg that hurt us."*

### 7.2 "Ask Hangar" — Phi Silica, on the NPU, offline
Maya can't send engine data to the cloud, and the hangar Wi-Fi drops out anyway. **Ask Hangar** is a chat pane backed by **Phi Silica**, the Windows on-device language model (`Microsoft.Windows.AI.Text.LanguageModel`). It runs on the Surface's NPU.

- **Open it:** a ✦ button next to search in the title bar, or **Ctrl+Space**. A 420px acrylic pane slides in from the right. Its header badge reads *On-device · Phi Silica · NPU*. Typing a question in the global search box also offers "Ask Hangar: …".
- **Grounding:** Each prompt carries a compact context block built from app data: the current page, the selected part, the vibration series summary, the flight 44 crew report, open WOs for Engine 2, and the AMM references. Answers stream token by token.
- **Citation chips are actions:** `Flight 44` scrubs and spins the globe, `Fan blade 14` expands the engine and selects it, and `WO 2207-011` opens Work Orders filtered to it.
- **Three scripted moments, with page-aware suggestion chips so it always works on stage:**
  1. **Aircraft:** *"Why is 2207 held, and what changed?"* The answer names the flight 44 hail, the climb from 0.9 to 2.31 ips, likely fan FOD or hail damage, and the AMM 72-00 borescope requirement. It shows chips for Flight 44, Fan blade 14, and Borescope.
  2. **Work Orders:** *"Have we seen this before?"* The answer summarises the related fan-blade WOs, including last year's same fault on CA 1916, with chips that filter the TableView.
  3. **Inspections:** *"Draft the logbook entry."* Maya's shorthand note (*"bl 14 LE nick 0.4mm wl AMM 72-21 blend"*) becomes a compliant release entry and pre-fills the sign-off sheet. **AI drafts; the licensed engineer signs.** That's the responsible-AI beat.
- **Stage moment:** the presenter turns on airplane mode, asks the question, and the answer still streams. *"No cloud. That's Phi Silica on the NPU."*
- **Robustness:**
  - The app checks `LanguageModel.GetReadyState()` and calls `EnsureReadyAsync()` at startup.
  - If the model is unavailable, for example on a non-Copilot+ PC, the pane says so honestly and falls back to deterministic scripted answers with the same streaming UX. Those scripted answers are labelled "Demo responses".

### Updated stage script (3:30)
| Time | Beat |
|---|---|
| 1:05 | Vibration chart + scrub → globe arc turns red on flight 44 (DEN → SEA hail) |
| 1:20 | Expand the globe: 42 aircraft live, then dive back into Engine 2 |
| 1:35 | Airplane mode → **Ask Hangar** "why is 2207 held?" → chip expands Engine 2 to fan blade 14 |
| 2:45 | Inspections: "Draft the logbook entry" → Phi Silica writes it → Maya signs |

### 7.3 Aion Instruct instead of Phi Silica (v3, in progress)
[Aion Instruct Preview](https://aka.ms/tryaion) is the next on-device model after Phi Silica.
- **API:** `AionInstructPreview.Text.LanguageModel` mirrors the WinAppSDK `LanguageModel` contract. `GenerateResponseAsync` takes options, which must not be null, and progress streams deltas. No LAF token is needed.
- **Delivery:** a framework MSIX (1.0.0.3) and a local-feed NuGet (1.0.1), plus a direct reference to CsWinRT 2.1.5. The build injects package dependencies on the framework and on Windows App Runtime 2 into the manifest.
- **Hardware:** NPU only, ARM64 Snapdragon (QNN) or Lunar Lake (OpenVINO). There is no CPU fallback.
- **Plan:** a backend chain of Aion → Phi Silica → scripted. Aion is enabled by an MSBuild switch only when its nupkg is present, so the repo still builds on machines without it. The badge reads *On-device · Aion Instruct (preview) · NPU*. Warm up the NPU cache before going on stage.
- **Constraints:** developer validation only. Never commit the nupkg or MSIX. Output is watermarked and non-deterministic, so keep the scripted chips as the safety net.
- **Main risk:** whether stable Windows App Runtime 2 can coexist with WinAppSDK 2.5.4-experimental, and whether the CsWinRT versions conflict. A scratch spike is checking this first.

### 7.4 Repo structure (v3)
`src/Hangar.WinUI`, `src/Hangar.Reactor` and `src/Hangar.Electron` share one root README with a table of all three implementations. `bench/` and `docs/` (benchmark, concept, screenshots per implementation) sit alongside them.

---

## 8. Next steps
1. Lock the story + name.
2. Figma hi-fi of the 4 screens (dark first), including the 3D hero frame.
3. Spike: 3D Scenes + glTF on WinAppSDK 2.5 (1–2 days).
4. Scaffold app: shell, theming, seeded data, TableView + Chart pages.
5. Ink inspection + sign-off flow.
6. Polish pass: motion, connected animations, stage mode, rehearsal on target hardware.
