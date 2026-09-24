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
. (Join-Path $PSScriptRoot "tools\UnityCommon.ps1")

$projectPath = $PSScriptRoot
$output      = Join-Path $BuildDir "vr-launch.exe"
# Kept out of $BuildDir: the installer ships everything under it verbatim.
$logDir      = Join-Path $PSScriptRoot "Logs"
$logFile     = Join-Path $logDir "unity-build.log"

# --- Locate the editor -------------------------------------------------------
$unity = Find-UnityExe -ProjectPath $projectPath -Explicit $UnityExe
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
Assert-NoOpenEditor -LogFile $logFile

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
