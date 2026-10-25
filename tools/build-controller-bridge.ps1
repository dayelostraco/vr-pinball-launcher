<# Package the experimental Touch add-on separately; never enables or installs it.
   Only the explicit source payload and its notices enter the ZIP. Unlisted
   files are not copied; extra DLLs or modified native bytes fail validation.
#>
param(
    [string]$SourceDirectory = (Split-Path $PSScriptRoot -Parent),
    [string]$OutputPath = (Join-Path (Split-Path $PSScriptRoot -Parent) 'dist\VRPinballLauncher-TouchBridge-Experimental.zip')
)
$ErrorActionPreference = 'Stop'
$lock = Get-Content -LiteralPath (Join-Path $SourceDirectory 'tools\native-lock.json') -Raw | ConvertFrom-Json
$dlls = @(Get-ChildItem -LiteralPath (Join-Path $SourceDirectory 'ControllerBridge') -Recurse -File | Where-Object Extension -eq '.dll')
if ($dlls.Count -ne $lock.files.Count) { throw 'Unreviewed or missing bridge DLL; run tools/check-native.py.' }
foreach ($entry in $lock.files) {
    if ((Get-FileHash -LiteralPath (Join-Path $SourceDirectory $entry.path)).Hash -ine $entry.sha256) {
        throw "Native dependency integrity check failed: $($entry.path)"
    }
}
$staging = Join-Path ([IO.Path]::GetTempPath()) ('vrl-touch-addon-' + [Guid]::NewGuid().ToString('N'))
$bridge = Join-Path $staging 'ControllerBridge'
$licenses = Join-Path $bridge 'licenses'
New-Item -ItemType Directory -Force -Path $licenses | Out-Null
foreach ($file in @('vpx_vr_controls.ahk', 'auto_oculus_touch.ahk', 'auto_oculus_touch.dll',
    'vJoyInterface.dll', 'auto_oculus_touch_README.txt', 'README.md')) {
    Copy-Item -LiteralPath (Join-Path $SourceDirectory ('ControllerBridge\' + $file)) -Destination $bridge
}
foreach ($file in @('AutoOculusTouch-MIT.txt', 'vJoy-MIT.txt')) {
    Copy-Item -LiteralPath (Join-Path $SourceDirectory ('licenses\' + $file)) -Destination $licenses
}
Copy-Item -LiteralPath (Join-Path $SourceDirectory 'LICENSE') -Destination $bridge
Copy-Item -LiteralPath (Join-Path $SourceDirectory 'tools\native-lock.json') -Destination $bridge
$destination = [IO.Path]::GetFullPath($OutputPath)
New-Item -ItemType Directory -Force -Path (Split-Path $destination -Parent) | Out-Null
Compress-Archive -LiteralPath $bridge -DestinationPath $destination -Force
Write-Output "Experimental Touch add-on: $destination (not installed or enabled). Staging: $staging"
