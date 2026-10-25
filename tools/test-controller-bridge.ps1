<# Parse the complete bridge and exercise input decisions without starting it.
   Supply a trusted AutoHotkey 1.1 interpreter; no installation is performed here.
#>
param([Parameter(Mandatory = $true)][string]$AutoHotkeyExe)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$bridge = Join-Path $repo 'ControllerBridge'
$main = Join-Path $bridge 'vpx_vr_controls.ahk'
$fixture = Join-Path $repo 'tests\controller-bridge.ahk'
$interpreter = (Get-Item -LiteralPath $AutoHotkeyExe).FullName
$version = [Diagnostics.FileVersionInfo]::GetVersionInfo($interpreter)
if ($version.FileMajorPart -ne 1 -or $version.FileMinorPart -ne 1) {
    throw 'Controller bridge validation requires AutoHotkey 1.1.'
}
Push-Location $bridge
try {
    # /iLib validates without running any auto-execute statements. /include
    # runs the fixture before the real script; it exits before DLLs/input load.
    & $interpreter /ErrorStdOut /iLib '*' $main | Write-Output
    if ($LASTEXITCODE -ne 0) { throw 'AutoHotkey bridge syntax validation failed.' }
    & $interpreter /ErrorStdOut /include $fixture $main | Write-Output
    if ($LASTEXITCODE -ne 0) { throw 'Controller input regression checks failed.' }
}
finally { Pop-Location }
