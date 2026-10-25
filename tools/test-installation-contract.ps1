$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'SetupCore.psm1') -Force -DisableNameChecking
$root = Join-Path ([IO.Path]::GetTempPath()) ('Launcher contract ' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $root | Out-Null
$fixture = Get-Content (Join-Path $PSScriptRoot '..\tests\fixtures\launch-contract.json') -Raw | ConvertFrom-Json
foreach ($case in $fixture.cases) {
    foreach ($relative in @($case.runner, $case.table, $case.profileRunner)) {
        if (!$relative) { continue }
        $path = Join-Path $root $relative
        New-Item -ItemType Directory -Force ([IO.Path]::GetDirectoryName($path)) | Out-Null
        [IO.File]::WriteAllText($path, 'Fixture - never executed')
    }
    $config = [pscustomobject]@{
        vpinballExecutable = $case.runner
        vpxProfiles = @([pscustomobject]@{name=$case.profileName;executable=$case.profileRunner;iniFile=$case.ini;workingDirectory=$case.working})
        tableProfiles = @([pscustomobject]@{table='sAMPLE';profile=$case.assignment})
    }
    if (!$case.assignment) { $config.tableProfiles = @() }
    $command = Get-SetupTestCommand $config $case.table $root
    if ($command.Executable -ne (Join-Path $root $case.expectedRunner)) { throw "Runner mismatch: $($case.name)" }
    if ($command.WorkingDirectory -ne (Join-Path $root $case.expectedWorking)) { throw "Working directory mismatch: $($case.name)" }
    $launchProfile = if ($case.expectedProfile) {$case.expectedProfile} else {'Default'}
    if ($command.Profile -ne $launchProfile) { throw "Profile mismatch: $($case.name)" }
    $arguments = '-Play ' + (Quote-SetupArgument (Join-Path $root $case.table))
    if ($case.expectedIni) { $arguments = '-Ini ' + (Quote-SetupArgument (Join-Path $root $case.expectedIni)) + ' ' + $arguments }
    if ($command.Arguments -cne $arguments) { throw "Arguments mismatch: $($case.name)" }
}
$roots = @(Get-SetupRomDirectories 'C:\Tables\Sample.vpx' 'C:\VPX\VPinballX_BGFX64.exe' 'D:\roms;E:\roms;relative')
$expected = @('C:\Tables\pinmame\roms','D:\roms','E:\roms','C:\VPX\VPinMAME\roms','C:\VPX\plugins\pinmame\pinmame\roms')
if (($roots -join '|') -cne ($expected -join '|')) { throw 'ROM directory contract mismatch' }
Write-Output "Installation contract: $($fixture.cases.Count) launch cases and ROM roots passed. Fixtures: $root"
