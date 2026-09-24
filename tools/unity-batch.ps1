<#
  unity-batch.ps1 -- run a static editor method in batchmode (scene setup, preview renders).

  The method must log "[unity-batch] OK" when it succeeds and call EditorApplication.Exit.

  Usage: powershell -ExecutionPolicy Bypass -File tools\unity-batch.ps1 -Method VRLauncher.EditorTools.ArcadePreview.RenderCabinets
#>

param(
    [Parameter(Mandatory = $true)][string]$Method,
    [string]$UnityExe = ""
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "UnityCommon.ps1")

$projectPath = Split-Path $PSScriptRoot -Parent
$logDir      = Join-Path $projectPath "Logs"
$logFile     = Join-Path $logDir "unity-batch.log"
New-Item -ItemType Directory -Force -Path $logDir | Out-Null
if (Test-Path $logFile) { Remove-Item $logFile -Force }

$unity = Find-UnityExe -ProjectPath $projectPath -Explicit $UnityExe
Ensure-UnityHub

$started = Get-Date
& $unity -quit -batchmode -projectPath $projectPath -executeMethod $Method -logFile $logFile | Out-Null
Wait-UnityExit -ExePath $unity -Since $started
Assert-NoOpenEditor -LogFile $logFile

$compileErrors = Select-String -Path $logFile -Pattern 'error CS\d+' -ErrorAction SilentlyContinue
if ($compileErrors) {
    $compileErrors | Select-Object -First 10 | ForEach-Object { Write-Host $_.Line -ForegroundColor Red }
    throw "Compilation failed -- see $logFile"
}

if (-not (Select-String -Path $logFile -Pattern '\[unity-batch\] OK' -Quiet)) {
    Select-String -Path $logFile -Pattern 'Exception|Error' | Select-Object -First 15 | ForEach-Object { Write-Host $_.Line -ForegroundColor Red }
    throw "$Method did not report success -- see $logFile"
}

Write-Host "$Method OK" -ForegroundColor Green
