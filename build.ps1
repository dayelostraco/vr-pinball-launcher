<#
  build.ps1 -- build the Unity player, then optionally the installer.

  Produces Build\vr-launch.exe, then (unless -SkipInstaller) chains into
  build-installer.ps1 to produce dist\VRPinballLauncher-Setup.exe.

  Usage:
      powershell -ExecutionPolicy Bypass -File build.ps1
      powershell -ExecutionPolicy Bypass -File build.ps1 -Version 1.2.0
      powershell -ExecutionPolicy Bypass -File build.ps1 -SkipInstaller
      powershell -ExecutionPolicy Bypass -File build.ps1 -Deploy
#>

param(
    [string]$BuildDir      = (Join-Path $PSScriptRoot "Build"),
    [string]$Version       = "1.0.0",
    [string]$UnityExe      = "",
    [switch]$SkipInstaller,
    [switch]$Deploy
)

$ErrorActionPreference = "Stop"

$projectPath = $PSScriptRoot
$output      = Join-Path $BuildDir "vr-launch.exe"
# Kept out of $BuildDir: the installer ships everything under it verbatim.
$logDir      = Join-Path $PSScriptRoot "Logs"
$logFile     = Join-Path $logDir "unity-build.log"

# --- Locate the editor -------------------------------------------------------
# Prefer the version pinned in ProjectVersion.txt; fall back to the newest
# installed editor, warning about the mismatch rather than failing silently.
function Find-UnityExe {
    param([string]$Explicit)

    if ($Explicit) {
        if (-not (Test-Path $Explicit)) { throw "Unity.exe not found at '$Explicit'." }
        return $Explicit
    }

    $versionFile = Join-Path $projectPath "ProjectSettings\ProjectVersion.txt"
    $pinned = $null
    if (Test-Path $versionFile) {
        $line = Select-String -Path $versionFile -Pattern '^m_EditorVersion:\s*(.+)$'
        if ($line) { $pinned = $line.Matches[0].Groups[1].Value.Trim() }
    }

    $root = "C:\Program Files\Unity\Hub\Editor"
    if (-not (Test-Path $root)) { throw "No Unity editors found under '$root'. Install one via Unity Hub." }

    if ($pinned) {
        $exact = Join-Path $root "$pinned\Editor\Unity.exe"
        if (Test-Path $exact) { return $exact }
        Write-Warning "Pinned editor $pinned is not installed; falling back to the newest available."
    }

    $candidate = Get-ChildItem $root -Directory |
        Sort-Object Name -Descending |
        ForEach-Object { Join-Path $_.FullName "Editor\Unity.exe" } |
        Where-Object { Test-Path $_ } |
        Select-Object -First 1

    if (-not $candidate) { throw "No Unity.exe found under '$root'." }
    return $candidate
}

# Unity.exe relaunches itself and the launcher process returns 0 immediately,
# so waiting on it proves nothing. Wait for the real editor process instead.
function Wait-UnityExit {
    param([string]$ExePath, [datetime]$Since)

    while ($true) {
        $running = Get-Process -Name 'Unity' -ErrorAction SilentlyContinue |
            Where-Object {
                $_.StartTime -ge $Since.AddSeconds(-5) -and
                ($_.Path -eq $ExePath -or -not $_.Path)
            }
        if (-not $running) { break }
        Start-Sleep -Seconds 3
    }
}

# A Personal licence is served by Unity Hub's licensing client. With the Hub
# closed, batchmode exits 198 with "No valid Unity Editor license found", so
# make sure it is running before building.
function Ensure-UnityHub {
    if (Get-Process -Name 'Unity Hub' -ErrorAction SilentlyContinue) { return }

    $appId = 'shell:AppsFolder\UnityTechnologies.UnityHub_2vrhnee42bhxm!UnityHub'
    Write-Host "Unity Hub is not running; starting it for licence validation..."
    try { Start-Process $appId } catch {
        Write-Warning "Could not start Unity Hub automatically. Start it manually if the build reports a licence error."
        return
    }

    for ($i = 0; $i -lt 20; $i++) {
        Start-Sleep -Seconds 2
        if (Get-Process -Name 'Unity.Licensing.Client' -ErrorAction SilentlyContinue) { break }
    }
}

$unity = Find-UnityExe -Explicit $UnityExe
Write-Host "Editor : $unity"
Write-Host "Output : $output"

Ensure-UnityHub

if (Get-Process -Name 'vr-launch' -ErrorAction SilentlyContinue) {
    throw "vr-launch.exe is running -- close it before building."
}

New-Item -ItemType Directory -Force -Path $BuildDir | Out-Null
New-Item -ItemType Directory -Force -Path $logDir | Out-Null
if (Test-Path $logFile) { Remove-Item $logFile -Force }

# --- Build -------------------------------------------------------------------
$started = Get-Date
Write-Host "Building (first import can take several minutes)..."

# No -nographics: it requires the com.unity.editor.headless entitlement
# (Pro/Enterprise) and a Personal licence exits with code 198.
& $unity -quit -batchmode `
    -projectPath $projectPath `
    -executeMethod VRLauncher.EditorTools.BuildScript.BuildWindows64 `
    -buildOutput $output `
    -logFile $logFile | Out-Null

Wait-UnityExit -ExePath $unity -Since $started

# Trust the log, not the exit code.
$log = if (Test-Path $logFile) { Get-Content $logFile -Raw } else { "" }

$compileErrors = Select-String -Path $logFile -Pattern 'error CS\d+' -ErrorAction SilentlyContinue
if ($compileErrors) {
    $compileErrors | Select-Object -First 10 | ForEach-Object { Write-Host $_.Line -ForegroundColor Red }
    throw "Compilation failed -- see $logFile"
}

if ($log -notmatch 'Build result:\s*Succeeded') {
    throw "Build did not report success -- see $logFile"
}

if (-not (Test-Path $output)) {
    throw "Build reported success but '$output' is missing -- see $logFile"
}

Write-Host "Build OK: $output ($((Get-Item $output).Length) bytes)" -ForegroundColor Green

# --- Installer ---------------------------------------------------------------
if (-not $SkipInstaller) {
    $installer = Join-Path $PSScriptRoot "build-installer.ps1"
    if (-not (Test-Path $installer)) { throw "build-installer.ps1 not found." }
    Write-Host "Building installer..."
    & $installer -BuildDir $BuildDir -Version $Version
}

# --- Optional deploy ---------------------------------------------------------
# Copies the player over an existing install, preserving launcher-config.json,
# ControllerBridge\ and Media\ -- the installed config is the working one.
if ($Deploy) {
    $dst = Join-Path $env:LOCALAPPDATA "Programs\VR Pinball Launcher"
    if (-not (Test-Path $dst)) { throw "No install found at '$dst'." }

    Write-Host "Deploying to $dst"
    foreach ($dir in @('vr-launch_Data', 'MonoBleedingEdge', 'D3D12')) {
        $target = Join-Path $dst $dir
        if (Test-Path $target) { Remove-Item $target -Recurse -Force }
        $source = Join-Path $BuildDir $dir
        if (Test-Path $source) { Copy-Item $source -Destination $dst -Recurse -Force }
    }
    foreach ($file in @('vr-launch.exe', 'UnityPlayer.dll', 'UnityCrashHandler64.exe', 'dstorage.dll', 'dstoragecore.dll')) {
        $source = Join-Path $BuildDir $file
        if (Test-Path $source) { Copy-Item $source -Destination $dst -Force }
    }
    Write-Host "Deployed (launcher-config.json, ControllerBridge\, Media\ preserved)" -ForegroundColor Green
}
