# HANGAR demo script (3 minutes)

This script is for the WinUI build (`src/Hangar.WinUI`). See the [README](../README.md) for build and run steps.

Press **Ctrl+Shift+D** before you go on stage.

## Pre-stage: warm up the NPU (Ask Hangar)

Do this at least **10 minutes before** the session, on the stage machine, with the build you'll present.

1. Launch HANGAR and press **Ctrl+Space**. HANGAR runs one warm-up generation automatically after loading the model.
2. Wait until the badge reads **On-device · Aion Instruct (preview) · NPU**, or **On-device · Phi Silica · NPU** on builds without Aion. While it reads **Warming up the NPU…**, the model is still loading.
   - The first Aion launch after installing or updating its framework compiles the model for the NPU. That takes **several minutes** (about 4.5 min on a Snapdragon X Elite). The result is cached under `C:\ProgramData\Aion Instruct Preview\Cache`, so later launches take about 10 s.
3. Ask **Why is 2207 held, and what changed?** once to confirm that answers stream.
4. Press **Ctrl+Shift+D** to reset the demo and clear the chat. Leave HANGAR running.

If the badge reads **Demo responses**, the tooltip explains why. The scripted answers are stage-safe, so the beat still works. **Ctrl+Shift+A** forces scripted mode on purpose.

## Run of show

| Time | Do | Say |
|---|---|---|
| 0:00 | Fleet: the amber CA 2207 tile is breathing | *"It's 2:40am. One of 42 aircraft just landed with a problem — and it leaves at 6:15."* |
| 0:20 | Click the tile; it connects into the Aircraft page | *"This is WinUI. Native, GPU-drawn 3D on a Surface."* |
| 0:45 | Drag to orbit, then **tap Engine 2** (it explodes); blade 14 glows red | *"Let's look inside the engine. There's our problem."* |
| 1:05 | Vibration chart: scrub the flight slider | *"The new WinUI Chart control — line, area, markers, all first-party."* |
| 1:15 | Keep scrubbing to **flight 44**: the globe arc turns red DEN → SEA and the hail chip appears. Press **G** to expand the globe and spin it, then press **Esc** to dive back into the engine | *"The chart shows when Engine 2 went bad. The globe shows where — and why. Hail on climb out of Denver. That's blade 14."* |
| 1:20 | *(Optional AI beat)* Turn on **airplane mode**, press **Ctrl+Space**, and tap **Why is 2207 held, and what changed?** The answer streams from the NPU; tap the **Fan blade 14** chip and Engine 2 explodes to the blade | *"No cloud, no Wi-Fi. A small language model on the NPU knows our fleet — and its answers are buttons."* |
| 1:25 | **Engine 2 work orders**: group by component, toggle the **N2207C** and **Last 12 months** chips, sort by date | *"And yes — WinUI has a TableView now. 2,400 rows, grouped, filtered, editable."* |
| 1:55 | **Engine 2 work orders**: **Show on aircraft** on the blade 14 order, then tap the red tip; the camera dives into the borescope and the reticle lands on the nick. Circle it with the pen and measure with the ruler | *"Ink is back in WinUI 3. The pen is still the best tool in the hangar."* |
| 2:30 | Sign the pad, then click **Sign & release to service**; the aircraft turns green | *"Signed, logged, airworthy. Wheels up at 5:52."* |
| 2:50 | Go back to Fleet: everything is green | *"Built entirely with WinUI and the Windows App SDK. Open source — go build yours."* |

