---
name: hangar-aion-setup
description: Set up, repair and run HANGAR's "Ask Hangar" pane with the Aion Instruct (preview) on-device model. Use when someone wants to run/build Hangar (WinUI), Ask Hangar or Aion on their machine, or reports NU1101 for AionInstructPreview.Text.Framework, REGDB_E_CLASSNOTREG, a "Demo responses" or "Phi Silica" badge instead of "Aion Instruct (preview)", E_FAIL on the first answer, or a long "Warming up the NPU…" state.
---

# Hangar · Aion Instruct (preview) setup

Ask Hangar (`src/Hangar.WinUI`) tries backends in order: **Aion Instruct (preview)** → Phi Silica → scripted demo answers. Aion is opt-in: `Hangar.csproj` enables it (`EnableAion`) only when `nuget-local\AionInstructPreview.Text.Framework.1.0.1.nupkg` exists at the repo root. Everything machine-side is automated by `scripts\Setup-Aion.ps1`.

## Steps

1. **Diagnose** (read-only), from the repo root:
   ```powershell
   powershell -ExecutionPolicy Bypass -File scripts\Setup-Aion.ps1 -CheckOnly -SkipBuild
   ```
   Lines are tagged `[ OK ]`, `[FIX ]`, `[WARN]`, `[FAIL]`. Exit code 0 = ready.
2. **Fix and build**: run it again without `-CheckOnly`. It may raise a UAC prompt (Developer Mode) and uses `winget`; tell the user before it does. Re-run until exit 0. Use `-Force` to re-download the nupkg and reinstall the framework.
3. **Run**: `cd src\Hangar.WinUI; winapp run .` → press **Ctrl+Space**. The badge should read **"On-device · Aion Instruct (preview) · NPU"**.
4. **Warm up**: the first launch after installing the framework compiles the model for the NPU (~4–5 min on Snapdragon X Elite; the pane shows "Warming up the NPU…"). Later starts take ~10 s. Before a demo, follow the pre-stage warm-up in `docs/demo-script.md`.

## Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| `NU1101` AionInstructPreview.Text.Framework | nupkg missing from `nuget-local\` | Run the script (step 2) |
| Restore uses an old package after re-download | stale `%NUGET_PACKAGES%` cache entry | The script removes it; or delete `<cache>\aioninstructpreview.text.framework\1.0.1` |
| `REGDB_E_CLASSNOTREG`, badge falls back to Phi Silica / Demo responses | Aion framework MSIX not installed | Run the script; check `Get-AppxPackage Microsoft.AionInstructPreview.Framework.1.0` |
| Package deployment error about a missing dependency | Windows App Runtime 2 (≥ 2.0.1.0) missing | Run the script (installs `Microsoft.WindowsAppRuntime.2.0` via winget) |
| `E_FAIL` on the first answer | cold NPU compile just finished | Retried automatically; ask again |
| Long "Warming up the NPU…" | cold compile, or QNN EP still downloading (needs network once) | Wait ~5 min on first run |
| Not ARM64 / no Snapdragon | Aion preview needs QNN on ARM64 Snapdragon | Expected: the app falls back. Build without Aion: `dotnet build -p:EnableAion=false` |

Deeper diagnostics: the sample repo's `scripts\Diagnose-AionInstructPreview.ps1` (https://github.com/microsoft/Aion-Instruct-Preview-Sample). The app logs with the `[AskHangar]` prefix to the debug output.

## Rules

- **Never commit** `nuget-local\`, the nupkg or the framework MSIX. They are developer-validation preview bits.
- **Never commit Aion/WAR2 dependencies in `Package.appxmanifest`.** They're injected into `obj\` at build time; `git status` must show the manifest unchanged after an Aion build.
- Don't edit the `EnableAion` block in `Hangar.csproj` to "fix" setup; fix the machine instead.
- Preview output is watermarked and non-deterministic. Citation chips and the logbook draft stay deterministic.
- Don't ship it.
