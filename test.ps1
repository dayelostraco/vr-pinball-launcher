<#
  test.ps1 -- run the Unity EditMode tests headlessly and summarise the results.

  Usage:
      powershell -ExecutionPolicy Bypass -File test.ps1
      powershell -ExecutionPolicy Bypass -File test.ps1 -Filter TableNamingTests
#>

param(
    [string]$UnityExe = "",
    [string]$Filter   = "",
    [switch]$Coverage
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
if ($Coverage) {
    # Keep each instrumented run separate so stale data cannot inflate results.
    $coverageDirectory = Join-Path $logDir ('unity-coverage-' + [Guid]::NewGuid().ToString('N'))
    $unityArgs += @('-enableCodeCoverage', '-coverageResultsPath', $coverageDirectory,
        '-coverageOptions', 'generateAdditionalMetrics;generateHtmlReport;assemblyFilters:+VRLauncher.*,-VRLauncher.Tests*,-VRLauncher.Editor*')
    Write-Host "Coverage output: $coverageDirectory"
}

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
if ($Coverage) {
    $summaryFile = Join-Path $coverageDirectory 'Report\Summary.json'
    if (!(Test-Path -LiteralPath $summaryFile)) { throw 'Unity did not produce a coverage summary.' }
    $summary = Get-Content -LiteralPath $summaryFile -Raw | ConvertFrom-Json
    $classes = @($summary.coverage.assemblies.classesinassembly)
    if (!$classes.Count) { throw 'Unity coverage report contains no production classes.' }
    foreach ($class in $classes) {
        if ($class.coverablelines -gt 0 -and (100 * $class.coveredlines / $class.coverablelines) -lt 80) {
            throw "Unity line coverage is below 80% for $($class.name). See $summaryFile"
        }
    }
    Write-Host "Unity lines: $($summary.summary.linecoverage)%; all measured classes meet 80%. Branches unsupported."
}
exit 0
