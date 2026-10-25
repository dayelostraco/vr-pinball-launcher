<# Compile and exercise the optional host using a hidden dummy child, never VPX.
   Fixtures are retained under the printed temporary path for diagnosis.
#>
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'SetupCore.psm1') -Force -DisableNameChecking
$root = Join-Path ([IO.Path]::GetTempPath()) ('vrl-helper-tests-' + [Guid]::NewGuid().ToString('N'))
$childDirectory = Join-Path $root 'Child'
$hostDirectory = Join-Path $root 'Host'
$workingDirectory = Join-Path $root 'Working'
New-Item -ItemType Directory -Force -Path $childDirectory, $hostDirectory, $workingDirectory | Out-Null
$child = Join-Path $childDirectory 'Fixture.exe'
$source = Join-Path $childDirectory 'Fixture.cs'
[IO.File]::WriteAllText($source, @'
using System;
using System.IO;
using System.Text;
class Fixture {
    static int Main(string[] args) {
        string root = AppDomain.CurrentDomain.BaseDirectory;
        File.WriteAllLines(Path.Combine(root, "arguments.txt"), Array.ConvertAll(args, a => Convert.ToBase64String(Encoding.UTF8.GetBytes(a))));
        File.WriteAllText(Path.Combine(root, "hint.txt"), Environment.GetEnvironmentVariable("SDL_JOYSTICK_ALLOW_BACKGROUND_EVENTS") ?? "");
        File.WriteAllText(Path.Combine(root, "working.txt"), Environment.CurrentDirectory);
        return 42;
    }
}
'@)
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:winexe "/out:$child" $source
if ($LASTEXITCODE) { throw 'Fixture compiler failed.' }
& (Join-Path $PSScriptRoot 'install-vpx-background-input.ps1') -VpxExecutable $child -DestinationDirectory $hostDirectory | Out-Null
$hostFile = Join-Path $hostDirectory 'VpxBackgroundInput.exe'
$sidecar = Join-Path $hostDirectory 'VpxBackgroundInput.vpx-path.txt'
function Invoke-HostFixture([string[]]$Arguments) {
    $start = [Diagnostics.ProcessStartInfo]::new()
    $start.FileName = $hostFile
    $start.Arguments = ($Arguments | ForEach-Object { Quote-SetupArgument $_ }) -join ' '
    $start.WorkingDirectory = $workingDirectory
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $process = [Diagnostics.Process]::Start($start)
    try {
        if (!$process.WaitForExit(10000)) { $process.Kill(); throw 'Fixture host timed out.' }
        return $process.ExitCode
    }
    finally { $process.Dispose() }
}
$arguments = @('-Play', 'A table.vpx', 'embedded"quote', 'trailing\', '', ('Caf' + [char]0xE9), '& echo never-executed')
if ((Invoke-HostFixture $arguments) -ne 42) { throw 'Host did not forward the child exit code.' }
$received = [IO.File]::ReadAllLines((Join-Path $childDirectory 'arguments.txt'))
if ($received.Count -ne $arguments.Count) { throw 'Host changed argument count.' }
for ($i = 0; $i -lt $arguments.Count; $i++) {
    if ([Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($received[$i])) -cne $arguments[$i]) { throw "Argument changed at index $i" }
}
if ([IO.File]::ReadAllText((Join-Path $childDirectory 'hint.txt')) -ne '1') { throw 'SDL hint was not inherited.' }
if ([IO.File]::ReadAllText((Join-Path $childDirectory 'working.txt')) -ne $workingDirectory) { throw 'Working directory was not preserved.' }
[IO.File]::WriteAllText($sidecar, $hostFile)
if ((Invoke-HostFixture @()) -ne 2) { throw 'Recursive host target was accepted.' }
[IO.File]::WriteAllText($child, 'Invalid executable fixture')
[IO.File]::WriteAllText($sidecar, $child)
if ((Invoke-HostFixture @()) -ne 1) { throw 'Invalid executable did not report a launch error.' }
if ([IO.File]::ReadAllText((Join-Path $hostDirectory 'VpxBackgroundInput.log')) -notmatch 'Launch failed:') { throw 'Launch failure was not logged.' }
Remove-Item -LiteralPath $sidecar
if ((Invoke-HostFixture @()) -ne 2) { throw 'Missing target sidecar was accepted.' }
Write-Output "PASS: helper argument, working-directory, environment, exit-code and failure contracts. Fixtures: $root"
