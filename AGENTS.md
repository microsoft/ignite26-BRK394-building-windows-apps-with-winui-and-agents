# Agent notes

- **Running Ask Hangar with Aion Instruct (preview)**, or fixing its setup (NU1101 for `AionInstructPreview.Text.Framework`, `REGDB_E_CLASSNOTREG`, a "Demo responses" badge): follow [`.github/skills/hangar-aion-setup/SKILL.md`](.github/skills/hangar-aion-setup/SKILL.md). It runs `scripts\Setup-Aion.ps1`.
- Never commit `nuget-local\`, the Aion nupkg/MSIX, `ask-hangar.local.json`, or Aion/WAR2 dependencies in `Package.appxmanifest`.
- The WinUI app lives in `src/Hangar.WinUI`; see the root `README.md` for architecture and the stage script in `docs/demo-script.md`.
