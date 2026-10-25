<#
Copied-player recovery test. See docs/testing.md before running.
Temporarily replaces shared player history and restores its original bytes.
#>
param(
    [string]$BuildDirectory = (Join-Path (Split-Path $PSScriptRoot -Parent) 'Build'),
    [string]$OutputDirectory = (Join-Path ([IO.Path]::GetTempPath()) ('launcher-runtime-' + [Guid]::NewGuid().ToString('N')))
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
Import-Module (Join-Path $PSScriptRoot 'SetupCore.psm1') -Force -DisableNameChecking
Assert-SetupClosed
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Choose a new output directory for test isolation.' }
$testRoot = [IO.Path]::GetFullPath($OutputDirectory)
Write-Output ('Runtime test artifacts: ' + $testRoot)
$testRuntime = Join-Path $testRoot 'runtime'
$testTables = Join-Path $testRoot 'tables'
$stateFile = Join-Path $env:USERPROFILE 'AppData\LocalLow\DefaultCompany\vr-launch\state.json'
if (!(Test-Path -LiteralPath (Join-Path $BuildDirectory 'vr-launch.exe'))) { throw 'Build the player first.' }
if (Get-Process -Name vr-launch -ErrorAction SilentlyContinue) { throw 'Close the launcher before the isolated recovery check.' }
New-Item -ItemType Directory -Force $testRuntime,$testTables | Out-Null
foreach ($name in @('vr-launch_Data','MonoBleedingEdge','D3D12','vr-launch.exe','UnityPlayer.dll','UnityCrashHandler64.exe','dstorage.dll','dstoragecore.dll')) {
 $source = Join-Path $BuildDirectory $name
 if (Test-Path -LiteralPath $source) { Copy-Item -LiteralPath $source -Destination $testRuntime -Recurse -Force }
}
New-Item -ItemType Directory -Force (Join-Path $testRuntime 'tools') | Out-Null
foreach ($name in @('setup.ps1','SetupCore.psm1','SetupPaths.ps1','SetupBackup.ps1','SetupBackupPage.ps1','VpxBackgroundInput.cs','install-vpx-background-input.ps1')) {
 Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination (Join-Path $testRuntime 'tools') -Force
}
$fakeExe = Join-Path $testRoot 'FakeVPX.exe'
$fakeSource = Join-Path $testRoot 'FakeVPX.cs'
[IO.File]::WriteAllText($fakeSource, @'
internal static class FakeVPX {
 static int Main(string[] args) {
  string folder = System.AppDomain.CurrentDomain.BaseDirectory;
  if (args.Length == 1 && args[0] == "--child") {
   System.Threading.Thread.Sleep(60000);
   return 0;
  }
  if (System.IO.File.Exists(System.IO.Path.Combine(folder, "hold-mode"))) {
   var start = new System.Diagnostics.ProcessStartInfo {
    FileName = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName,
    Arguments = "--child", UseShellExecute = false, CreateNoWindow = true
   };
   using (var child = System.Diagnostics.Process.Start(start)) {
    System.IO.File.WriteAllText(System.IO.Path.Combine(folder, "fixture-processes.txt"),
     System.Diagnostics.Process.GetCurrentProcess().Id + "," + child.Id);
    System.Threading.Thread.Sleep(60000);
   }
   return 0;
  }
  System.Threading.Thread.Sleep(300);
  return 42;
 }
}
'@)
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:winexe "/out:$fakeExe" $fakeSource
if ($LASTEXITCODE -ne 0) { throw 'Dummy compile failed.' }
$fakeOriginal = [IO.File]::ReadAllBytes($fakeExe)
$fakeTable = Join-Path $testTables 'Recovery Test (Original 2026).vpx'
[IO.File]::WriteAllText($fakeTable,'Dummy table used only by FakeVPX; never opened by VPX.')
$cfg = [pscustomobject]@{
 vpinballExecutable = $fakeExe
 tablesDirectory = $testTables
 searchSubdirectories = $true
 vpxProfiles = @()
 tableProfiles = @()
 enableControllerBridge = $false
}
$cfg | ConvertTo-Json -Depth 12 | Set-Content (Join-Path $testRuntime 'launcher-config.json') -Encoding UTF8
$testLog = Join-Path $testRoot ('recovery-' + (Get-Date -Format 'HHmmss') + '.log')
$originalState = if (Test-Path -LiteralPath $stateFile) { [IO.File]::ReadAllBytes($stateFile) } else { $null }
if ($null -ne $originalState) { [IO.File]::WriteAllBytes((Join-Path $testRoot 'state-before.json'), $originalState) }
New-Item -ItemType Directory -Force (Split-Path $stateFile) | Out-Null
$testProcess = $null
$setupChild = $null
$fixtureProcesses = @()
Add-Type -TypeDefinition 'using System; using System.Runtime.InteropServices; public static class RecoveryFocus { [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h); [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow(); [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, IntPtr pid); [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId(); [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint a, uint b, bool attach); [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h); [DllImport("user32.dll")] public static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra); }'
function Wait-LogCount([string]$needle, [int]$count) {
 $deadline = [DateTime]::UtcNow.AddSeconds(60)
 while ([DateTime]::UtcNow -lt $deadline) {
  if ($testProcess.HasExited) { throw 'Test launcher exited unexpectedly' }
  if (Test-Path -LiteralPath $testLog) {
   $reader = [IO.StreamReader]::new([IO.File]::Open($testLog,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::ReadWrite)); try { $content = $reader.ReadToEnd() } finally { $reader.Dispose() }
   if ([regex]::Matches($content,[regex]::Escape($needle)).Count -ge $count) { return }
  }
  Start-Sleep -Milliseconds 300
 }
 throw "Timed out waiting for $needle ($count)"
}
function Press-TestEnter([byte]$Key = 13) {
 $testProcess.Refresh()
 $handle = $testProcess.MainWindowHandle
 if ($handle -eq [IntPtr]::Zero) { throw 'Test launcher has no window' }
 $ws = New-Object -ComObject WScript.Shell
 $null = $ws.AppActivate($testProcess.Id)
 [RecoveryFocus]::keybd_event(18,0,0,[UIntPtr]::Zero); [RecoveryFocus]::keybd_event(18,0,2,[UIntPtr]::Zero); $foregroundThread = [RecoveryFocus]::GetWindowThreadProcessId([RecoveryFocus]::GetForegroundWindow(),[IntPtr]::Zero); $thisThread = [RecoveryFocus]::GetCurrentThreadId(); $attached = [RecoveryFocus]::AttachThreadInput($thisThread,$foregroundThread,$true); try { $null = [RecoveryFocus]::BringWindowToTop($handle); $null = [RecoveryFocus]::SetForegroundWindow($handle) } finally { if ($attached) { $null = [RecoveryFocus]::AttachThreadInput($thisThread,$foregroundThread,$false) } }; Start-Sleep -Milliseconds 150
 if ([RecoveryFocus]::GetForegroundWindow() -ne $handle) { throw 'Cannot focus isolated test window; no keys sent' }
 [RecoveryFocus]::keybd_event($Key,0,0,[UIntPtr]::Zero); Start-Sleep -Milliseconds 200; [RecoveryFocus]::keybd_event($Key,0,2,[UIntPtr]::Zero)
}
try {
 [IO.File]::WriteAllText($stateFile,'{"version":1,"favorites":[],"plays":[],"lastView":0}')
 $testProcess = Start-Process -FilePath (Join-Path $testRuntime 'vr-launch.exe') -WorkingDirectory $testRuntime -ArgumentList ('-logFile ' + [char]34 + $testLog + [char]34) -PassThru
 Wait-LogCount 'Launcher ready: navigation enabled' 1
 $configHash = (Get-FileHash (Join-Path $testRuntime 'launcher-config.json')).Hash
 Press-TestEnter 113
 Wait-LogCount 'Desktop setup window opened.' 1
 $deadline = [DateTime]::UtcNow.AddSeconds(20)
 while ([DateTime]::UtcNow -lt $deadline) {
  $child = Get-CimInstance Win32_Process -Filter ('ParentProcessId=' + $testProcess.Id) | Where-Object { $_.Name -ieq 'powershell.exe' -and $_.CommandLine -like '*setup.ps1*' } | Select-Object -First 1
  if ($child) {
   $setupChild = Get-Process -Id $child.ProcessId
   if ($setupChild.MainWindowHandle -ne [IntPtr]::Zero) { break }
  }
  Start-Sleep -Milliseconds 250
 }
 if (!$setupChild -or $setupChild.MainWindowHandle -eq [IntPtr]::Zero) { throw 'F2 did not open the setup window.' }
 $null = $setupChild.CloseMainWindow()
 if (!$setupChild.WaitForExit(5000)) { throw 'Setup did not close.' }
 if ((Get-FileHash (Join-Path $testRuntime 'launcher-config.json')).Hash -ne $configHash) { throw 'Opening setup changed configuration.' }
 Write-Output 'PASS: F2 opens/closes desktop setup without changing configuration.'
 Press-TestEnter
 Wait-LogCount 'Launch result:' 1
 Wait-LogCount 'Launcher ready: navigation enabled' 2
 Press-TestEnter
 Wait-LogCount 'Launch result:' 2
 Wait-LogCount 'Launcher ready: navigation enabled' 3
 Write-Output 'PASS: exit42 restores navigation, then retry also restores navigation.'
 # Exercise rescue against only this fixture's wrapper and child, never a real table.
 $holdMarker = Join-Path $testRoot 'hold-mode'
 [IO.File]::WriteAllText($holdMarker, 'fixture')
 Press-TestEnter
 $pidFile = Join-Path $testRoot 'fixture-processes.txt'
 $deadline = [DateTime]::UtcNow.AddSeconds(15)
 while (!(Test-Path -LiteralPath $pidFile) -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 100 }
 if (!(Test-Path -LiteralPath $pidFile)) { throw 'Dummy wrapper did not create its child.' }
 $fixtureProcesses = @(([IO.File]::ReadAllText($pidFile) -split ',') | ForEach-Object { Get-Process -Id ([int]$_) })
 if ($fixtureProcesses.Count -ne 2) { throw 'Expected two owned fixture processes.' }
 Press-TestEnter 8
 Wait-LogCount 'Launch result:' 3
 Wait-LogCount 'Launcher ready: navigation enabled' 4
 foreach ($fixture in $fixtureProcesses) {
  if (!$fixture.WaitForExit(5000)) { throw 'Emergency recovery left a fixture process running.' }
 }
 Remove-Item -LiteralPath $holdMarker
 Write-Output 'PASS: Backspace stops the owned wrapper and child and restores navigation.'
 [IO.File]::WriteAllText($fakeExe,'This intentionally is not a Windows executable.')
 Press-TestEnter
 Wait-LogCount 'Error launching table:' 1
 Wait-LogCount 'Launcher ready: navigation enabled' 5
 Write-Output 'PASS: process-start exception restores navigation/XR.'
 Move-Item -LiteralPath $fakeExe -Destination (Join-Path $testRoot 'InvalidVPX.fixture') -Force
 Press-TestEnter
 Wait-LogCount 'VPinballX executable not found:' 1
 Wait-LogCount 'Launcher ready: navigation enabled' 6
 Write-Output 'PASS: missing executable returns to navigation.'
 $testState = Get-Content $stateFile -Raw | ConvertFrom-Json
 if (@($testState.plays).Count -ne 0) { throw 'Failed launch increased play count' }
 if (@($testState.launches).Count -ne 1) { throw 'Launch outcome was not persisted per table' }
 if ($testState.launches[0].startupConfirmed) { throw 'Failed launch was marked startup-confirmed' }
 if ($testState.launches[0].result -notlike '*could not be found*') { throw 'Last launch error was not persisted' }
 if (!$testState.launches[0].when) { throw 'Launch timestamp missing' }
 Write-Output 'PASS: last launch error/timestamp persisted separately from play counts.'
 Write-Output "Recovery log: $testLog"
} finally {
 foreach ($fixture in $fixtureProcesses) {
  if (!$fixture.HasExited) { $fixture.Kill(); $null = $fixture.WaitForExit(3000) }
  $fixture.Dispose()
 }
 if ($setupChild -and !$setupChild.HasExited) { $setupChild.Kill(); $null = $setupChild.WaitForExit(3000) }
 if ($testProcess -and !$testProcess.HasExited) {
  $null = $testProcess.CloseMainWindow()
  if (!$testProcess.WaitForExit(8000)) { $testProcess.Kill(); $testProcess.WaitForExit(3000) | Out-Null }
 }
 if ($null -ne $originalState) { [IO.File]::WriteAllBytes($stateFile,$originalState) } elseif (Test-Path -LiteralPath $stateFile) { Remove-Item -LiteralPath $stateFile -Force }
 [IO.File]::WriteAllBytes($fakeExe,$fakeOriginal)
 Write-Output 'Original play history and browsing state restored byte-for-byte.'
}
