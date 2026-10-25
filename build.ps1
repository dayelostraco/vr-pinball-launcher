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
    [string]$Version       = "1.2.0",
    [string]$UnityExe      = "",
    [string]$LogDirectory  = (Join-Path $PSScriptRoot "Logs"),
    [switch]$SkipInstaller,
    [switch]$Deploy
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "tools\UnityCommon.ps1")

$projectPath = $PSScriptRoot
$output      = Join-Path $BuildDir "vr-launch.exe"
# Kept out of $BuildDir: the installer ships everything under it verbatim.
$logDir      = $LogDirectory
$logFile     = Join-Path $logDir "unity-build.log"

# --- Locate the editor -------------------------------------------------------
$unity = Find-UnityExe -ProjectPath $projectPath -Explicit $UnityExe
Write-Host "Editor : $unity"
Write-Host "Output : $output"

Ensure-UnityHub

if (Get-Process -Name 'vr-launch' -ErrorAction SilentlyContinue) {
    throw "vr-launch.exe is running -- close it before building."
}

if ($Deploy) {
    $dst = Join-Path $env:LOCALAPPDATA "Programs\VR Pinball Launcher"
    if (-not (Test-Path $dst)) { throw "No install found at '$dst'." }
    # Reject overlap before Unity writes anything into the installed runtime.
    $resolvedBuild = [IO.Path]::GetFullPath($BuildDir).TrimEnd('\') + '\'
    $resolvedInstall = [IO.Path]::GetFullPath($dst).TrimEnd('\') + '\'
    if ($resolvedBuild.StartsWith($resolvedInstall, [StringComparison]::OrdinalIgnoreCase) -or
        $resolvedInstall.StartsWith($resolvedBuild, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Build and installation directories must not overlap.'
    }
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
    -buildVersion $Version `
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
    # Validate the complete payload before deleting any installed runtime files.
    foreach ($required in @('UnityPlayer.dll', 'vr-launch_Data\Managed\VRLauncher.Runtime.dll',
        'vr-launch_Data\Managed\VRLauncher.Arcade.dll', 'vr-launch_Data\Managed\VRLauncher.Core.dll')) {
        if (!(Test-Path -LiteralPath (Join-Path $BuildDir $required) -PathType Leaf)) {
            throw "Incomplete deployment payload: $required. Installed files are unchanged."
        }
    }

    Write-Host "Deploying to $dst"
    foreach ($dir in @('vr-launch_Data', 'MonoBleedingEdge', 'D3D12')) {
        $target = Join-Path $dst $dir
        $resolvedTarget = [IO.Path]::GetFullPath($target)
        $resolvedInstall = [IO.Path]::GetFullPath($dst).TrimEnd('\') + '\'
        if (-not $resolvedTarget.StartsWith($resolvedInstall, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Runtime target is outside the launcher installation: $resolvedTarget"
        }
        if (Test-Path $target) { Remove-Item $target -Recurse -Force }
        $source = Join-Path $BuildDir $dir
        if (Test-Path $source) { Copy-Item $source -Destination $dst -Recurse -Force }
    }
    foreach ($file in @('vr-launch.exe', 'UnityPlayer.dll', 'UnityCrashHandler64.exe', 'dstorage.dll', 'dstoragecore.dll')) {
        $source = Join-Path $BuildDir $file
        if (Test-Path $source) { Copy-Item $source -Destination $dst -Force }
    }
    foreach ($assembly in @('VRLauncher.Runtime.dll', 'VRLauncher.Arcade.dll', 'VRLauncher.Core.dll')) {
        $relative = Join-Path 'vr-launch_Data\Managed' $assembly
        $built = Join-Path $BuildDir $relative
        $installed = Join-Path $dst $relative
        if ((Get-FileHash -LiteralPath $built).Hash -ne (Get-FileHash -LiteralPath $installed).Hash) {
            throw "Installed runtime assembly mismatch: $assembly"
        }
    }
    $toolDestination = Join-Path $dst 'tools'
    New-Item -ItemType Directory -Force -Path $toolDestination | Out-Null
    foreach ($tool in @('TrustedDownloads.ps1', 'download-lock.json', 'setup.ps1', 'SetupCore.psm1', 'SetupPaths.ps1', 'SetupBackup.ps1', 'SetupBackupPage.ps1', 'VpxBackgroundInput.cs', 'install-vpx-background-input.ps1')) {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot ('tools\' + $tool)) -Destination $toolDestination -Force
    }
    $docsDestination = Join-Path $dst 'docs'
    New-Item -ItemType Directory -Force -Path $docsDestination | Out-Null
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'docs\setup.md') -Destination $docsDestination -Force
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'install.ps1') -Destination $dst -Force
    foreach ($notice in @('LICENSE', 'THIRD-PARTY-NOTICES.md')) {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot $notice) -Destination $dst -Force
    }
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'licenses') -Destination $dst -Recurse -Force
    Write-Host "Deployed and verified all launcher assemblies (launcher-config.json, ControllerBridge\, Media\ preserved)" -ForegroundColor Green
}
