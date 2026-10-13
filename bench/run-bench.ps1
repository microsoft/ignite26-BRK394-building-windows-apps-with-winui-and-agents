param(
    [int]$Runs = 5,
    [int]$CpuSeconds = 10,
    [string[]]$Kinds = @('winui', 'aot', 'electron'),
    [string]$WinuiProject = (Join-Path (Split-Path -Parent $PSScriptRoot) 'src\Hangar.WinUI')
)
# Benchmarks HANGAR builds: winui (Release, R2R, untrimmed), aot (Native AOT, trimmed) and electron (packaged).
# Startup = root process creation -> first frame ready (two frames after root Loaded / rAF after first paint).
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$tmp = Join-Path $PSScriptRoot 'tmp'
New-Item -ItemType Directory -Force -Path $tmp | Out-Null
$electronExe = Join-Path $root 'src\Hangar.Electron\dist\Hangar-win32-arm64\Hangar.exe'
$winuiProj = $WinuiProject
$r2rPublish = Join-Path $winuiProj 'bin\r2r\publish'
$winuiAppx = $r2rPublish
$aotPublish = Join-Path $winuiProj 'bin\aot\publish'
$cores = [Environment]::ProcessorCount

function Stop-AllHangar {
    # Only stop bench-owned instances; leave other HANGAR sessions (e.g. Debug runs) alone.
    $owned = @([IO.Path]::GetFullPath($winuiProj), (Split-Path -Parent $electronExe))
    Get-CimInstance Win32_Process -Filter "Name='Hangar.exe'" | Where-Object {
        $path = $_.ExecutablePath
        $path -and @($owned | Where-Object { $path.StartsWith($_, [StringComparison]::OrdinalIgnoreCase) }).Count
    } | ForEach-Object {
        Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue
    }
    Start-Sleep -Milliseconds 800
}

function Get-Tree([int]$rootPid) {
    $all = @(Get-CimInstance Win32_Process)
    $ids = [Collections.Generic.HashSet[int]]::new(); [void]$ids.Add($rootPid)
    do {
        $added = $false
        foreach ($p in $all) {
            if ($ids.Contains([int]$p.ParentProcessId) -and -not $ids.Contains([int]$p.ProcessId) -and $p.Name -ne 'conhost.exe') {
                [void]$ids.Add([int]$p.ProcessId); $added = $true
            }
        }
    } while ($added)
    @($all | Where-Object { $ids.Contains([int]$_.ProcessId) })
}

function Get-Mem([int]$rootPid) {
    $tree = Get-Tree $rootPid
    $ws = 0; $priv = 0
    foreach ($p in $tree) {
        $gp = Get-Process -Id $p.ProcessId -ErrorAction SilentlyContinue
        if ($gp) { $ws += $gp.WorkingSet64; $priv += $gp.PrivateMemorySize64 }
    }
    [ordered]@{ processes = $tree.Count; workingSetMB = [math]::Round($ws / 1MB, 1); privateMB = [math]::Round($priv / 1MB, 1) }
}

function Get-CpuPct([int]$rootPid, [int]$seconds) {
    $snap = {
        $h = @{}
        foreach ($p in (Get-Tree $rootPid)) {
            $gp = Get-Process -Id $p.ProcessId -ErrorAction SilentlyContinue
            if ($gp) { $h[$gp.Id] = $gp.TotalProcessorTime.TotalMilliseconds }
        }
        $h
    }
    $a = & $snap; $sw = [Diagnostics.Stopwatch]::StartNew()
    Start-Sleep -Seconds $seconds
    $b = & $snap; $elapsed = $sw.Elapsed.TotalMilliseconds
    $used = 0
    foreach ($k in $b.Keys) { $used += $b[$k] - ($(if ($a.ContainsKey($k)) { $a[$k] } else { 0 })) }
    # % of one core, and % of whole machine
    [ordered]@{ pctOneCore = [math]::Round(100 * $used / $elapsed, 1); pctMachine = [math]::Round(100 * $used / $elapsed / $cores, 2) }
}

function Wait-File([string]$path, [int]$timeoutMs, [string]$contains) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.ElapsedMilliseconds -lt $timeoutMs) {
        if (Test-Path -LiteralPath $path) {
            $t = Get-Content -LiteralPath $path -Raw -ErrorAction SilentlyContinue
            if ($t -and (-not $contains -or $t.Contains($contains))) { return $t }
        }
        Start-Sleep -Milliseconds 100
    }
    throw "Timed out waiting for $path"
}

function Start-App([string]$kind, [string]$out, [string[]]$extra) {
    if ($kind -eq 'electron') {
        $p = Start-Process -FilePath $electronExe -ArgumentList (@("--bench-out=$out") + $extra) -PassThru
        return $p.Id
    }
    Push-Location $winuiProj
    try {
        if ($kind -eq 'aot') {
            $args2 = @('run', $aotPublish, '--manifest', (Join-Path $winuiProj 'Package.appxmanifest'), '--exe', 'Hangar.exe',
                '--output-appx-directory', (Join-Path $winuiProj 'bin\aot\AppX'), '--detach', '--json', '--', "--bench-out=$out") + $extra
        } else {
            $args2 = @('run', $r2rPublish, '--manifest', (Join-Path $winuiProj 'Package.appxmanifest'), '--exe', 'Hangar.exe',
                '--output-appx-directory', (Join-Path $winuiProj 'bin\r2r\AppX'), '--detach', '--json', '--', "--bench-out=$out") + $extra
        }
        $json = (& winapp @args2) -join "`n"
    } finally { Pop-Location }
    $m = [regex]::Match($json, '"ProcessId"\s*:\s*(\d+)')
    if (-not $m.Success) { throw "winapp run returned no ProcessId: $json" }
    [int]$m.Groups[1].Value
}

function Get-CreationEpochMs([int]$procId) {
    $p = Get-Process -Id $procId
    [DateTimeOffset]::new($p.StartTime.ToUniversalTime()).ToUnixTimeMilliseconds()
}

function Invoke-Run([string]$kind, [int]$i) {
    Stop-AllHangar
    $out = Join-Path $tmp "$kind-run$i.json"
    Remove-Item "$out*" -Force -ErrorAction SilentlyContinue
    $navArg = if ($kind -eq 'electron') { '--bench-nav' } else { '--autonav' }
    $appPid = Start-App $kind $out @($navArg)
    $created = Get-CreationEpochMs $appPid
    $ready = Wait-File $out 20000 'readyEpochMs' | ConvertFrom-Json
    $startup = [int64]$ready.readyEpochMs - $created
    Start-Sleep -Seconds 4
    $memStart = Get-Mem $appPid
    $nav = Wait-File "$out.nav.jsonl" 60000 '"done"'
    $memAfterNav = Get-Mem $appPid
    $rows = @($nav -split "`n" | Where-Object { $_.Trim() } | ForEach-Object { $_ | ConvertFrom-Json } | Where-Object { $_.page })
    Stop-AllHangar
    [ordered]@{
        kind = $kind; run = $i; appPid = $appPid; startupMs = $startup
        internalStartupMs = $(if ($kind -eq 'electron') { [int64]$ready.readyEpochMs - [int64]$ready.mainStartEpochMs } else { [int64]$ready.readyEpochMs - [int64]$ready.processStartEpochMs })
        memAt5s = $memStart; memAfterNav = $memAfterNav
        nav = @($rows | ForEach-Object { [ordered]@{ page = "$($_.page)".ToLower(); round = [int]$_.round; ms = [double]$_.ms } })
    }
}

function Invoke-CpuRun([string]$kind, [string]$page) {
    Stop-AllHangar
    $out = Join-Path $tmp "$kind-cpu-$page.json"
    Remove-Item "$out*" -Force -ErrorAction SilentlyContinue
    $pg = if ($kind -eq 'electron') { $page.ToLower() } else { $page }
    $appPid = Start-App $kind $out @("--page=$pg")
    Wait-File $out 20000 'readyEpochMs' | Out-Null
    Start-Sleep -Seconds 4
    $cpu = Get-CpuPct $appPid $CpuSeconds
    $mem = Get-Mem $appPid
    Stop-AllHangar
    [ordered]@{ kind = $kind; page = $page; cpu = $cpu; mem = $mem }
}

function Median([double[]]$v) {
    $s = @($v | Sort-Object); if (-not $s.Count) { return $null }
    if ($s.Count % 2) { $s[[int][math]::Floor($s.Count / 2)] } else { ($s[$s.Count / 2 - 1] + $s[$s.Count / 2]) / 2 }
}

function Get-DirMB([string]$p) { [math]::Round(((Get-ChildItem -LiteralPath $p -Recurse -File | Measure-Object Length -Sum).Sum) / 1MB, 1) }

# --- runs ---
$runList = [Collections.Generic.List[object]]::new()
# Warm-up launch of each (not counted) so both start from a warm disk cache.
foreach ($k in $Kinds) { [void](Invoke-CpuRun $k 'Fleet') }
for ($i = 0; $i -lt $Runs; $i++) {
    $order = if ($i % 2) { [array]::Reverse(($o = @($Kinds))); $o } else { @($Kinds) }
    foreach ($k in $order) {
        Write-Host "run $i $k"
        $runList.Add((Invoke-Run $k $i))
    }
}
$cpuRuns = [Collections.Generic.List[object]]::new()
foreach ($page in 'Fleet', 'Aircraft') {
    foreach ($k in $Kinds) {
        Write-Host "cpu $page $k"
        $cpuRuns.Add((Invoke-CpuRun $k $page))
    }
}

# --- summary ---
$summary = [ordered]@{}
foreach ($k in $Kinds) {
    $r = @($runList | Where-Object { $_.kind -eq $k })
    $allNav = @($r | ForEach-Object { $_.nav })
    $navSummary = [ordered]@{}
    foreach ($pg in 'aircraft', 'workorders', 'inspections', 'fleet') {
        $navSummary[$pg] = [ordered]@{
            coldMs = Median @($allNav | Where-Object { $_.page -eq $pg -and $_.round -eq 0 } | ForEach-Object { $_.ms })
            warmMs = Median @($allNav | Where-Object { $_.page -eq $pg -and $_.round -gt 0 } | ForEach-Object { $_.ms })
        }
    }
    $summary[$k] = [ordered]@{
        startupMs = Median @($r | ForEach-Object { $_.startupMs })
        internalStartupMs = Median @($r | ForEach-Object { $_.internalStartupMs })
        workingSetMB = Median @($r | ForEach-Object { $_.memAt5s.workingSetMB })
        privateMB = Median @($r | ForEach-Object { $_.memAt5s.privateMB })
        workingSetAfterNavMB = Median @($r | ForEach-Object { $_.memAfterNav.workingSetMB })
        privateAfterNavMB = Median @($r | ForEach-Object { $_.memAfterNav.privateMB })
        processes = Median @($r | ForEach-Object { $_.memAt5s.processes })
        navColdAllMs = Median @($allNav | Where-Object { $_.round -eq 0 } | ForEach-Object { $_.ms })
        navWarmAllMs = Median @($allNav | Where-Object { $_.round -gt 0 } | ForEach-Object { $_.ms })
        nav = $navSummary
        cpuFleetIdle = ($cpuRuns | Where-Object { $_.kind -eq $k -and $_.page -eq 'Fleet' }).cpu
        cpuAircraft = ($cpuRuns | Where-Object { $_.kind -eq $k -and $_.page -eq 'Aircraft' }).cpu
    }
}
if ($summary.Contains('winui')) { $summary.winui.diskMB = [math]::Round(((Get-ChildItem $r2rPublish -Recurse -File | Where-Object Extension -ne '.pdb' | Measure-Object Length -Sum).Sum) / 1MB, 1) }
if ($summary.Contains('aot')) { $summary.aot.diskMB = [math]::Round(((Get-ChildItem $aotPublish -Recurse -File | Where-Object Extension -ne '.pdb' | Measure-Object Length -Sum).Sum) / 1MB, 1) }
if ($summary.Contains('electron')) { $summary.electron.diskMB = Get-DirMB (Split-Path -Parent $electronExe) }

$result = [ordered]@{
    machine = [ordered]@{
        cpu = (Get-CimInstance Win32_Processor | Select-Object -First 1).Name
        cores = $cores
        ramGB = [math]::Round((Get-CimInstance Win32_ComputerSystem).TotalPhysicalMemory / 1GB, 1)
        os = (Get-CimInstance Win32_OperatingSystem).Caption + ' ' + [Environment]::OSVersion.Version
    }
    timestamp = (Get-Date).ToString('o'); runsPerApp = $Runs; cpuSampleSeconds = $CpuSeconds
    summary = $summary; runs = @($runList.ToArray()); cpuRuns = @($cpuRuns.ToArray())
}
$result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'results.json') -Encoding utf8
$summary | ConvertTo-Json -Depth 6
