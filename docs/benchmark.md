# HANGAR benchmark: WinUI 3 (ReadyToRun / Native AOT) vs Electron

Same app, same data, same machine. The harness is `bench\run-bench.ps1`; raw numbers are in `bench\results.json` (this run) and `bench\results-baseline-r2r.json` (the earlier R2R-only run).

| Build | Project | Notes |
|---|---|---|
| WinUI R2R | `src\Hangar.WinUI\` | .NET 10, WindowsAppSDK 2.5.4-experimental, ReadyToRun, not trimmed |
| WinUI AOT | `src\Hangar.WinUI\` | Same source, `PublishAot=true` (Native AOT, trimmed) |
| Electron | `src\Hangar.Electron\` | Electron 41.10.7, vanilla JS + canvas, packaged with electron-packager |

Machine: Snapdragon X Elite X1E80100 (12 cores), 31.6 GB RAM, Windows 11 Insider 26340, ARM64. SDK 10.0.401, ILCompiler 10.0.12.

## Results (median of 5 runs)

| Metric | WinUI AOT | WinUI R2R | Electron | Winner |
|---|---|---|---|---|
| Startup to first frame | **461 ms** | 531 ms (baseline) / 824 ms (this run, noisy) | 596 ms (468 ms internal) | AOT |
| Working set after startup | **413 MB** | 471–476 MB | 1,077 MB | AOT |
| Private bytes after startup | **382 MB** | 401–412 MB | 812 MB | AOT |
| Working set / private after visiting every page | **463 / 399 MB** | 513–519 / 410–421 MB | 1,103 / ~836 MB | AOT |
| Navigation, cold (avg of 4 pages) | 122 ms | 115–136 ms | **43 ms** | Electron |
| Navigation, warm (avg of 4 pages) | 72 ms | 65–73 ms | **21 ms** | Electron |
| Idle CPU (% of one core) | **5.7** | 6.4–7.3 | 22.6 | AOT |
| CPU with the hologram animating (% of one core) | 68 | 64–67 | 118 | WinUI (both) |
| Size on disk | **59.5 MB** | 110.6 MB (publish folder) / 236 MB (AppX layout) | 346 MB | AOT |
| Processes | **1** | 1 | 4 | WinUI |

R2R ranges show the earlier baseline run and this run. This run's R2R startup samples were 657–925 ms, so treat the 531 ms baseline as representative.

### Navigation per page, cold / warm (ms)

| Page | WinUI AOT | WinUI R2R | Electron |
|---|---|---|---|
| Aircraft | 122 / 70 | 150 / 71 | 61 / 37 |
| Work Orders | 191 / 98 | 219 / 101 | 47 / 28 |
| Inspections | 116 / 50 | 87 / 49 | 17 / 15 |
| Fleet | 72 / 76 | 77 / 73 | 16 / 15 |

## What the AOT pass changed

`src\Hangar.WinUI\Hangar.csproj`:

- `AllowUnsafeBlocks=true`, which the CsWinRT AOT source generators require.
- Non-Debug builds: `PublishReadyToRun=true`, `InvariantGlobalization`, `UseSystemResourceKeys`, `ConcurrentGarbageCollection=false` and `TieredPGO`.
- `PublishTrimmed` is set only when `PublishAot=true`. Trimming without AOT and a RID causes NETSDK1102.
- AOT group: `PublishReadyToRun=false`, `OptimizationPreference=Speed` and `IlcGenerateStackTraceData=false`.

Code fixes needed for AOT and trimming:

- `{Binding}` sources must be AOT-bindable. `Models\WorkOrder.cs` and the search suggestion type in `ShellViewModel.cs` are now `partial` classes marked `[WinRT.GeneratedBindableCustomProperty]`.
  - `WorkOrder` implements `INotifyDataErrorInfo` by hand on `ObservableObject`, replacing the reflection-based `ObservableValidator`.
  - This is why the earlier trimmed build crashed at startup; it now runs.
- `ShellViewModel.Suggestions` is an `ObservableCollection` updated in place rather than a reassigned `List`.
- `Helpers\ChartFactory.cs` has a small `ToSolid` fix.

### Effect

- AOT starts about 13% faster than R2R (461 vs 531 ms).
- AOT uses about 13% less working set and 7% less private memory.
- AOT is 4x smaller on disk than the AppX layout (59.5 vs 236 MB).
- Navigation and animation CPU barely moved. XAML layout, the TableView and Composition dominate those costs, not JIT.

## Takeaways

- **WinUI AOT beats Electron on startup, memory, CPU and disk**: about 2.6x less working set, about 4x less idle CPU, 1.7x less hologram CPU, a sixth of the disk, and 1 process vs 4.
- **Electron still wins navigation latency**: its pages are lightweight DOM and canvas, while WinUI builds real XAML trees. Work Orders, with its TableView and 200+ rows, is the most expensive WinUI page.
- Further WinUI navigation wins would come from XAML work, not more compiler flags:
  - `NavigationCacheMode` on heavy pages
  - `x:Load` or deferred loading for off-screen sections
  - lighter item templates in the Work Orders table

## Caveats

- **Frame rate is uncapped** in every build. Both stacks render at the display's refresh rate.
- **Electron background throttling is disabled** (`backgroundThrottling: false` plus the Chromium switches in `main.js`) so it isn't penalised while unfocused. WinUI doesn't throttle either.
- The AOT and R2R runs used a clean export of the app with a separate bench package identity, so they don't touch the installed dev package. The code is identical to `src\Hangar.WinUI\`.
- Startup is measured from process launch to first rendered frame (`--bench-out`). Electron's "internal" number starts at its main-process start.
- R2R disk size is the publish folder in this run and the AppX layout in the baseline. AOT and Electron are publish or package folders without .pdb files.
- These are single-machine numbers on ARM64. The variance between runs is visible in the R2R startup samples.

## Reproduce

```powershell
# vswhere must be on PATH for WinAppSDK builds
$env:PATH = "C:\Program Files (x86)\Microsoft Visual Studio\Installer;$env:PATH"

# from the repo root
cd src\Hangar.WinUI
dotnet publish -c Release -r win-arm64 -o bin\r2r\publish
dotnet publish -c Release -r win-arm64 -p:PublishAot=true -o bin\aot\publish

cd ..\Hangar.Electron
npm ci
npm run package

cd ..\..
.\bench\run-bench.ps1 -Runs 5 -CpuSeconds 10   # optional: -WinuiProject <path> -Kinds r2r,aot,electron
```
