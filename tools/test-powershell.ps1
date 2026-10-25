<# Run Windows fixture tests with reproducible Pester command coverage.
   Pester does not measure branches. Unexecuted production scripts remain in scope.
#>
param()
$ErrorActionPreference = 'Stop'
Import-Module Pester -RequiredVersion 5.9.1
$repo = Split-Path $PSScriptRoot -Parent
$reportDirectory = Join-Path $repo 'Logs'
New-Item -ItemType Directory -Force -Path $reportDirectory | Out-Null
$configuration = New-PesterConfiguration
$configuration.Run.Path = Join-Path $repo 'tests\powershell'
$configuration.Run.PassThru = $true
$configuration.Output.Verbosity = 'Normal'
$configuration.CodeCoverage.Enabled = $true
$configuration.CodeCoverage.Path = @(
    'tools\SetupCore.psm1', 'tools\SetupPaths.ps1', 'tools\SetupBackup.ps1',
    'tools\SetupBackupPage.ps1', 'tools\setup.ps1', 'tools\TrustedDownloads.ps1',
    'tools\UnityCommon.ps1', 'build.ps1', 'build-installer.ps1', 'tools\unity-batch.ps1',
    'tools\write-release-inventory.ps1', 'tools\build-controller-bridge.ps1', 'tools\install-vpx-background-input.ps1', 'install.ps1'
) | ForEach-Object { Join-Path $repo $_ }
$configuration.CodeCoverage.OutputPath = Join-Path $reportDirectory 'powershell-coverage.xml'
$configuration.CodeCoverage.OutputFormat = 'JaCoCo'
$configuration.CodeCoverage.CoveragePercentTarget = 80
$configuration.TestResult.Enabled = $true
$configuration.TestResult.OutputPath = Join-Path $reportDirectory 'powershell-tests.xml'
$result = Invoke-Pester -Configuration $configuration
if ($result.FailedCount -gt 0 -or $result.Result -ne 'Passed') { throw 'PowerShell fixture checks failed.' }
$settings = [Xml.XmlReaderSettings]::new()
$settings.DtdProcessing = [Xml.DtdProcessing]::Ignore
$settings.XmlResolver = $null
$reader = [Xml.XmlReader]::Create($configuration.CodeCoverage.OutputPath.Value, $settings)
try {
    $report = [Xml.XmlDocument]::new()
    $report.XmlResolver = $null
    $report.Load($reader)
}
finally { $reader.Dispose() }
$failures = @()
foreach ($file in $report.report.package.sourcefile) {
    $counter = $file.counter | Where-Object type -eq 'LINE'
    $total = [int]$counter.covered + [int]$counter.missed
    if ($total -le 0) { $failures += "No line measurement: $($file.name)"; continue }
    $percent = 100 * [int]$counter.covered / $total
    Write-Output ('{0}: {1:F1}% lines; branches unsupported' -f $file.name, $percent)
    if ($percent -lt 80) { $failures += "$($file.name) is below 80% line coverage" }
}
if (!$report.report.package.sourcefile) { throw 'PowerShell coverage report contains no source files.' }
if ($failures.Count) { throw ($failures -join '; ') }
