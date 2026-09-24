<#
  test.ps1 -- run the Unity EditMode tests headlessly and summarise the results.

  Usage:
      powershell -ExecutionPolicy Bypass -File test.ps1
      powershell -ExecutionPolicy Bypass -File test.ps1 -Filter TableNamingTests
#>

param(
    [string]$UnityExe = "",
    [string]$Filter   = ""
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "tools\UnityCommon.ps1")

$projectPath = $PSScriptRoot
$logDir      = Join-Path $projectPath "Logs"
$logFile     = Join-Path $logDir "unity-tests.log"
$results     = Join-Path $logDir "editmode-results.xml"

New-Item -ItemType Directory -Force -Path $logDir | Out-Null
foreach ($file in @($logFile, $results)) {
    if (Test-Path $file) { Remove-Item $file -Force }
}

$unity = Find-UnityExe -ProjectPath $projectPath -Explicit $UnityExe
Ensure-UnityHub

$unityArgs = @('-batchmode', '-projectPath', $projectPath,
               '-runTests', '-testPlatform', 'EditMode',
               '-testResults', $results, '-logFile', $logFile)
if ($Filter) { $unityArgs += @('-testFilter', $Filter) }

$started = Get-Date
& $unity @unityArgs | Out-Null
Wait-UnityExit -ExePath $unity -Since $started
Assert-NoOpenEditor -LogFile $logFile

$compileErrors = Select-String -Path $logFile -Pattern 'error CS\d+' -ErrorAction SilentlyContinue
if ($compileErrors) {
    $compileErrors | Select-Object -First 10 | ForEach-Object { Write-Host $_.Line -ForegroundColor Red }
    throw "Compilation failed -- see $logFile"
}

if (-not (Test-Path $results)) { throw "No test results were written -- see $logFile" }

[xml]$xml = Get-Content $results -Raw
$run = $xml.'test-run'
Write-Host "EditMode tests: $($run.passed) passed, $($run.failed) failed, $($run.skipped) skipped (total $($run.total))"

foreach ($case in $xml.SelectNodes('//test-case[@result="Failed"]')) {
    Write-Host "FAIL $($case.fullname)" -ForegroundColor Red
    $message = $case.SelectSingleNode('failure/message')
    if ($message) { Write-Host "     $($message.InnerText.Trim())" }
}

if ($run.result -notlike 'Passed*') { exit 1 }
exit 0
