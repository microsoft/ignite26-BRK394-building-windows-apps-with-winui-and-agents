<#
.SYNOPSIS
    One-shot, idempotent setup for Ask Hangar's Aion Instruct (preview) backend.

.DESCRIPTION
    Checks and (unless -CheckOnly) fixes everything HANGAR needs to run Ask Hangar on
    Aion Instruct (preview) - https://aka.ms/tryaion:
      1. Supported architecture (ARM64 Snapdragon with QNN for the NPU; x64 is best-effort).
      2. Developer Mode (needed for loose-layout registration via `winapp run`).
      3. Windows App Runtime 2 (Microsoft.WindowsAppRuntime.2 >= 2.0.1.0).
      4. Aion framework MSIX 1.0.0.3 (Microsoft.AionInstructPreview.Framework.1.0).
      5. The SDK nupkg AionInstructPreview.Text.Framework.1.0.1.nupkg in <repo>\nuget-local\
         (git-ignored). Its presence auto-enables the EnableAion MSBuild switch.
      6. No stale copy of that package in the NuGet global cache.
      7. QNN execution provider status (informational, ARM64 only).
      8. Optionally builds src\Hangar.WinUI.

    It never edits tracked files. The preview packages must never be committed.

.PARAMETER CheckOnly
    Report status only; install/download nothing.

.PARAMETER SkipBuild
    Don't run `dotnet build` at the end.

.PARAMETER Force
    Re-download the nupkg and reinstall the framework MSIX even if present.

.EXAMPLE
    pwsh -File scripts\Setup-Aion.ps1
.EXAMPLE
    pwsh -File scripts\Setup-Aion.ps1 -CheckOnly -SkipBuild
#>
[CmdletBinding()]
param(
    [switch]$CheckOnly,
    [switch]$SkipBuild,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$ReleaseTag       = 'v1.0.0.3'
$ReleaseBase      = "https://github.com/microsoft/Aion-Instruct-Preview-Sample/releases/download/$ReleaseTag"
$FrameworkName    = 'Microsoft.AionInstructPreview.Framework.1.0'
$FrameworkVersion = [version]'1.0.0.3'
$WarName          = 'Microsoft.WindowsAppRuntime.2'
$WarMinVersion    = [version]'2.0.1.0'
$NupkgId          = 'AionInstructPreview.Text.Framework'
$NupkgVersion     = '1.0.1'
$NupkgFile        = "$NupkgId.$NupkgVersion.nupkg"
$QnnName          = 'MicrosoftCorporationII.WinML.Qualcomm.QNN.EP.2'
$QnnMinVersion    = [version]'2.2480.49.0'

$RepoRoot   = Split-Path -Parent $PSScriptRoot
$FeedDir    = Join-Path $RepoRoot 'nuget-local'
$ProjectDir = Join-Path $RepoRoot 'src\Hangar.WinUI'

$script:Problems = 0
function Write-Ok   ([string]$m) { Write-Host "  [ OK ] $m" -ForegroundColor Green }
function Write-Fix  ([string]$m) { Write-Host "  [FIX ] $m" -ForegroundColor Cyan }
function Write-Info ([string]$m) { Write-Host "  [INFO] $m" -ForegroundColor DarkGray }
function Write-Bad  ([string]$m) { Write-Host "  [FAIL] $m" -ForegroundColor Red; $script:Problems++ }
function Write-Warn2([string]$m) { Write-Host "  [WARN] $m" -ForegroundColor Yellow }
function Write-Step ([string]$m) { Write-Host "`n$m" -ForegroundColor White }

function Test-IsAdmin {
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    ([Security.Principal.WindowsPrincipal]$id).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Get-ExactAppx([string]$Name) {
    Get-AppxPackage -Name $Name -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -eq $Name } |
        Sort-Object { [version]$_.Version } -Descending
}

Write-Host "HANGAR - Aion Instruct (preview) setup" -ForegroundColor White
Write-Host "Preview only: don't ship it, don't commit the nupkg or MSIX. Output is watermarked and non-deterministic." -ForegroundColor DarkYellow
if ($CheckOnly) { Write-Host "(check-only mode: nothing will be installed)" -ForegroundColor DarkGray }

# 1. Architecture -------------------------------------------------------------
Write-Step '1. Architecture'
$rawArch = if ($env:PROCESSOR_ARCHITEW6432) { $env:PROCESSOR_ARCHITEW6432 } else { $env:PROCESSOR_ARCHITECTURE }
$Arch = switch ($rawArch) { 'ARM64' { 'ARM64' } 'AMD64' { 'x64' } default { $null } }
if (-not $Arch) {
    Write-Bad "Unsupported architecture '$rawArch'. Aion needs ARM64 (or x64). HANGAR will fall back to Phi Silica / Demo responses."
    exit 1
}
if ($Arch -eq 'ARM64') { Write-Ok "ARM64 - QNN (Snapdragon NPU) path" }
else { Write-Warn2 "x64 - Aion uses OpenVINO here; this demo was validated on ARM64 Snapdragon only." }

# 2. Developer Mode -----------------------------------------------------------
Write-Step '2. Developer Mode'
$devKey = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock'
function Test-DevMode { (Get-ItemProperty -Path $devKey -Name AllowDevelopmentWithoutDevLicense -ErrorAction SilentlyContinue).AllowDevelopmentWithoutDevLicense -eq 1 }
$regFix = 'reg add "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock" /t REG_DWORD /f /v AllowDevelopmentWithoutDevLicense /d 1'
if (Test-DevMode) { Write-Ok 'Developer Mode is on' }
elseif ($CheckOnly) { Write-Bad "Developer Mode is off. Fix (admin): $regFix" }
else {
    Write-Fix 'Turning on Developer Mode (UAC prompt)...'
    try {
        if (Test-IsAdmin) { & cmd.exe /c $regFix | Out-Null }
        else { Start-Process -FilePath cmd.exe -ArgumentList "/c $regFix" -Verb RunAs -Wait }
        if (Test-DevMode) { Write-Ok 'Developer Mode is on' } else { Write-Bad "Could not enable Developer Mode. Run as admin: $regFix" }
    } catch { Write-Bad "Could not enable Developer Mode ($($_.Exception.Message)). Run as admin: $regFix" }
}

# 3. Windows App Runtime 2 ----------------------------------------------------
Write-Step '3. Windows App Runtime 2'
function Get-War { Get-ExactAppx $WarName | Where-Object { [version]$_.Version -ge $WarMinVersion } | Select-Object -First 1 }
$war = Get-War
if ($war) { Write-Ok "$WarName $($war.Version)" }
elseif ($CheckOnly) { Write-Bad "$WarName >= $WarMinVersion missing. Fix: winget install --id Microsoft.WindowsAppRuntime.2.0 --exact" }
else {
    Write-Fix 'Installing Windows App Runtime 2 via winget...'
    & winget install --id Microsoft.WindowsAppRuntime.2.0 --exact --accept-package-agreements --accept-source-agreements --disable-interactivity | Out-Host
    $war = Get-War
    if ($war) { Write-Ok "$WarName $($war.Version)" } else { Write-Bad "$WarName still missing after winget. Install it manually and re-run." }
}

# 4. Aion framework MSIX ------------------------------------------------------
Write-Step '4. Aion framework MSIX'
$fw = Get-ExactAppx $FrameworkName | Where-Object { [version]$_.Version -ge $FrameworkVersion } | Select-Object -First 1
if ($fw -and -not $Force) { Write-Ok "$FrameworkName $($fw.Version) ($($fw.Architecture))" }
elseif ($CheckOnly) { Write-Bad "$FrameworkName $FrameworkVersion missing." }
else {
    $msixName = "AionInstructPreview.LanguageModel.Framework_$($FrameworkVersion)_$Arch.msix"
    $msixPath = Join-Path $env:TEMP $msixName
    Write-Fix "Downloading $msixName..."
    Invoke-WebRequest -Uri "$ReleaseBase/$msixName" -OutFile $msixPath -UseBasicParsing
    Write-Fix 'Installing the framework (Add-AppxPackage)...'
    try {
        Add-AppxPackage -Path $msixPath -ForceUpdateFromAnyVersion
        $fw = Get-ExactAppx $FrameworkName | Select-Object -First 1
        if ($fw) { Write-Ok "$FrameworkName $($fw.Version)" } else { Write-Bad 'Framework install did not register.' }
    } catch { Write-Bad "Add-AppxPackage failed: $($_.Exception.Message)" }
    finally { Remove-Item $msixPath -ErrorAction SilentlyContinue }
}

# 5. SDK nupkg in nuget-local\ ------------------------------------------------
Write-Step '5. SDK nupkg (nuget-local\, git-ignored)'
$nupkgPath = Join-Path $FeedDir $NupkgFile
if ((Test-Path $nupkgPath) -and -not $Force) { Write-Ok "nuget-local\$NupkgFile" }
elseif ($CheckOnly) { Write-Bad "nuget-local\$NupkgFile missing (EnableAion stays off; the app falls back to Phi Silica / Demo responses)." }
else {
    New-Item -ItemType Directory -Path $FeedDir -Force | Out-Null
    Write-Fix "Downloading $NupkgFile from release $ReleaseTag..."
    Invoke-WebRequest -Uri "$ReleaseBase/$NupkgFile" -OutFile $nupkgPath -UseBasicParsing
    if (Test-Path $nupkgPath) { Write-Ok "nuget-local\$NupkgFile" } else { Write-Bad 'Download failed.' }
}
Push-Location $RepoRoot
try { & git check-ignore -q "nuget-local/$NupkgFile" 2>$null; $isIgnored = ($LASTEXITCODE -eq 0) } finally { Pop-Location }
if ($isIgnored) { Write-Ok 'nuget-local/ is git-ignored' }
else { Write-Bad 'nuget-local/ is NOT git-ignored. Never commit the preview nupkg; restore .gitignore.' }

# 6. Stale NuGet cache --------------------------------------------------------
Write-Step '6. NuGet global cache'
$nugetRoot = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE '.nuget\packages' }
$cached = Join-Path $nugetRoot ("{0}\{1}" -f $NupkgId.ToLowerInvariant(), $NupkgVersion)
if (-not (Test-Path $cached)) { Write-Info 'Not cached yet (restore will extract it).' }
else {
    $stale = $false
    $cachedHashFile = Get-ChildItem $cached -Filter '*.nupkg.sha512' -ErrorAction SilentlyContinue | Select-Object -First 1
    if ((Test-Path $nupkgPath) -and $cachedHashFile) {
        $sha = [Security.Cryptography.SHA512]::Create()
        try { $localHash = [Convert]::ToBase64String($sha.ComputeHash([IO.File]::ReadAllBytes($nupkgPath))) } finally { $sha.Dispose() }
        $stale = ((Get-Content $cachedHashFile.FullName -Raw).Trim() -ne $localHash)
    }
    if (-not $stale) { Write-Ok "Cache entry matches ($cached)" }
    elseif ($CheckOnly) { Write-Bad "Stale cache entry: $cached (delete it and rebuild)." }
    else { Write-Fix "Removing stale cache entry $cached"; Remove-Item $cached -Recurse -Force; Write-Ok 'Cache cleared' }
}

# 7. QNN execution provider (informational) -----------------------------------
if ($Arch -eq 'ARM64') {
    Write-Step '7. QNN execution provider (informational)'
    $qnn = Get-ExactAppx $QnnName | Select-Object -First 1
    if ($qnn -and [version]$qnn.Version -ge $QnnMinVersion) { Write-Ok "$QnnName $($qnn.Version)" }
    elseif ($qnn) { Write-Warn2 "$QnnName $($qnn.Version) is older than $QnnMinVersion; Windows updates it on first model load (needs network)." }
    else { Write-Info 'QNN EP not installed yet; Windows acquires it on the first model load (needs network once).' }
}

# 8. Build --------------------------------------------------------------------
if (-not $SkipBuild -and -not $CheckOnly) {
    Write-Step '8. Build src\Hangar.WinUI'
    Push-Location $ProjectDir
    try {
        & dotnet build -c Debug "-p:Platform=$Arch" -nologo -v:minimal | Out-Host
        if ($LASTEXITCODE -eq 0) { Write-Ok "Built Debug $Arch (EnableAion auto-on)" } else { Write-Bad "dotnet build failed (exit $LASTEXITCODE)" }
    } finally { Pop-Location }
    Push-Location $RepoRoot
    try {
        $dirtyManifest = & git status --porcelain -- 'src/Hangar.WinUI/Package.appxmanifest'
        if ($dirtyManifest) { Write-Bad 'Package.appxmanifest changed; Aion deps must only be injected into obj\. Do not commit it.' }
    } finally { Pop-Location }
}

# Summary ---------------------------------------------------------------------
Write-Host ''
if ($script:Problems -eq 0) {
    Write-Host 'All set.' -ForegroundColor Green
    Write-Host @"
Next:
  cd src\Hangar.WinUI
  winapp run .                       # Ctrl+Space opens Ask Hangar
The badge should read "On-device · Aion Instruct (preview) · NPU".
The FIRST launch after installing the framework compiles the model for the NPU
(~4-5 min on a Snapdragon X Elite; the pane shows "Warming up the NPU..."). Later starts take ~10 s.
Build without Aion: dotnet build -p:EnableAion=false
"@
    exit 0
}
Write-Host "$($script:Problems) problem(s) found. Re-run without -CheckOnly to fix them, or follow the hints above." -ForegroundColor Red
exit 1
