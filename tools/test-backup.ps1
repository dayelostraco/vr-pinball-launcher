$ErrorActionPreference='Stop'
Import-Module (Join-Path $PSScriptRoot 'SetupCore.psm1') -Force -DisableNameChecking
Assert-SetupClosed
$root=Join-Path ([IO.Path]::GetTempPath()) ('launcher-backup-tests-'+[Guid]::NewGuid().ToString('N'))
$app=Join-Path $root 'Launcher';New-Item -ItemType Directory -Force $app|Out-Null
$configPath=Join-Path $app 'launcher-config.json';$statePath=Join-Path $root 'state.json';$zip=Join-Path $root 'backup.zip'
$script:passed=0
function Assert($Condition,$Message){if(!$Condition){throw $Message};$script:passed++}
function Throws([scriptblock]$Action,$Message){$thrown=$false;try{&$Action|Out-Null}catch{$thrown=$true};Assert $thrown $Message}
$original=[ordered]@{vpinballExecutable='D:\Old VPX\VPinballX_BGFX64.exe';tablesDirectory='D:\Old tables';custom=[ordered]@{name=('Caf'+[char]0xE9)};vpxProfiles=@(@{name='Old';executable='D:\Old.exe'});tableProfiles=@(@{table='A';profile='Old'})}
[IO.File]::WriteAllText($configPath,($original|ConvertTo-Json -Depth 20))
$history='{"version":1,"favorites":["A.vpx"],"plays":[{"path":"A.vpx","last":"2026-10-24T10:00:00Z","count":4}],"launches":[{"path":"A.vpx","startupConfirmed":true}]}'
[IO.File]::WriteAllText($statePath,$history)
$null=Export-SetupBackup $app $zip $statePath
$backup=Read-SetupBackup $zip
Assert ($backup.Files.Count -eq 3) 'Missing backup entries'
Assert ($backup.Config.custom.name -ceq $original.custom.name) 'Unicode settings lost'
Throws {Export-SetupBackup $app $zip $statePath} 'Existing archive overwritten'
$current=@{vpinballExecutable='C:\New VPX\VPinballX_BGFX64.exe';tablesDirectory='C:\New tables';enableControllerBridge=$false;custom='Current';vpxProfiles=@(@{name='New';executable='C:\New.exe'});tableProfiles=@(@{table='B';profile='New'})}
[IO.File]::WriteAllText($configPath,($current|ConvertTo-Json -Depth 20));[IO.File]::WriteAllText($statePath,'{"version":1,"favorites":[],"plays":[]}')
$beforeConfig=[IO.File]::ReadAllText($configPath);$beforeState=[IO.File]::ReadAllText($statePath)
$plan=New-SetupRestorePlan $app $zip $true $true $true $statePath
$null=Save-SetupRestorePlan $plan
$restored=Read-SetupConfig $app
Assert ($restored.vpinballExecutable -eq $current.vpinballExecutable) 'This PC path not preserved'
Assert ($restored.PSObject.Properties['enableControllerBridge'] -and !$restored.enableControllerBridge) 'Current controller enablement not preserved'
Assert ($restored.tableProfiles[0].table -eq 'B' -and $restored.vpxProfiles[0].name -eq 'New') 'Current profile assignments not preserved'
Assert ($restored.custom.name -ceq $original.custom.name) 'Preferences not restored'
Assert ([IO.File]::ReadAllText($statePath) -ceq $history) 'History not restored byte-for-byte'
$saved=Get-ChildItem (Join-Path $app 'SetupBackups') -Directory | Select-Object -Last 1
Assert ([IO.File]::ReadAllText((Join-Path $saved.FullName 'launcher-config.json')) -ceq $beforeConfig) 'Config safety backup wrong'
Assert ([IO.File]::ReadAllText((Join-Path $saved.FullName 'state.json')) -ceq $beforeState) 'History safety backup wrong'
$plan=New-SetupRestorePlan $app $zip $true $false $false $statePath
$null=Save-SetupRestorePlan $plan
Assert ((Read-SetupConfig $app).vpinballExecutable -eq $original.vpinballExecutable) 'Full configuration restore failed'
$plan=New-SetupRestorePlan $app $zip $false $true $true $statePath
[IO.File]::WriteAllText($statePath,'{"version":1,"favorites":["changed"],"plays":[]}')
Throws {Save-SetupRestorePlan $plan} 'Stale restore plan overwrote new history'
$beforeConfig=[IO.File]::ReadAllText($configPath);$beforeState=[IO.File]::ReadAllText($statePath)
$plan=New-SetupRestorePlan $app $zip $true $true $false $statePath
$lock=[IO.File]::Open($statePath,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
try{Throws {Save-SetupRestorePlan $plan} 'Locked history file did not fail restore'}finally{$lock.Dispose()}
Assert ([IO.File]::ReadAllText($configPath) -ceq $beforeConfig -and [IO.File]::ReadAllText($statePath) -ceq $beforeState) 'Failed restore did not roll back'
$noHistory=Join-Path $root 'settings-only.zip';$null=Export-SetupBackup $app $noHistory (Join-Path $root 'missing.json')
Throws {New-SetupRestorePlan $app $noHistory $true $true $false $statePath} 'Missing history silently accepted'
$null=New-SetupRestorePlan $app $noHistory $true $false $false $statePath
Throws {New-SetupRestorePlan $app $zip $false $false $true $statePath} 'Empty restore accepted'
function Write-TestZip($Path,$Entries){$z=[IO.Compression.ZipFile]::Open($Path,[IO.Compression.ZipArchiveMode]::Create);try{foreach($e in $Entries){$entry=$z.CreateEntry($e.Name);$stream=$entry.Open();try{$bytes=[Text.Encoding]::UTF8.GetBytes($e.Text);$stream.Write($bytes,0,$bytes.Length)}finally{$stream.Dispose()}}}finally{$z.Dispose()}}
foreach($name in @('../state.json','sub/launcher-config.json','evil.exe')){$bad=Join-Path $root ([Guid]::NewGuid().ToString()+'.zip');Write-TestZip $bad @(@{Name=$name;Text='{}'});Throws {Read-SetupBackup $bad} 'Unexpected archive entry accepted'}
$bad=Join-Path $root 'duplicate.zip';Write-TestZip $bad @(@{Name='state.json';Text='{}'},@{Name='state.json';Text='{}'});Throws {Read-SetupBackup $bad} 'Duplicate entry accepted'
$bad=Join-Path $root 'tampered.zip';Copy-Item $zip $bad
$archive=[IO.Compression.ZipFile]::Open($bad,[IO.Compression.ZipArchiveMode]::Update);try{$archive.GetEntry('state.json').Delete();$entry=$archive.CreateEntry('state.json');$writer=[IO.StreamWriter]::new($entry.Open());try{$writer.Write('{}')}finally{$writer.Dispose()}}finally{$archive.Dispose()}
Throws {Read-SetupBackup $bad} 'Tampered backup accepted'
[IO.File]::WriteAllText($configPath,'{bad')
Throws {Export-SetupBackup $app (Join-Path $root 'malformed.zip') $statePath} 'Malformed configuration exported'
Assert (!(Test-Path (Join-Path $root 'malformed.zip'))) 'Failed export left archive behind'
foreach($invalid in @('{"version":1,"favorites":"wrong","plays":[]}','{"version":1,"favorites":[],"plays":[{"path":"A","count":-1}]}','{"version":1,"favorites":[],"plays":[],"launches":"wrong"}')){Throws {Test-SetupBackupJson 'state.json' ([Text.Encoding]::UTF8.GetBytes($invalid))} 'Invalid history schema accepted'}
foreach ($count in @('true', '1.5', '2147483648', '"1"', 'null')) {
    $invalid = '{"version":1,"favorites":[],"plays":[{"path":"A","count":' + $count + '}]}'
    Throws {Test-SetupBackupJson 'state.json' ([Text.Encoding]::UTF8.GetBytes($invalid))} "Invalid count accepted: $count"
}
foreach ($invalid in @(
    '{"version":1,"favorites":[" "],"plays":[]}',
    '{"version":1,"favorites":[],"plays":[{"path":"","count":1}]}',
    '{"version":1,"favorites":[],"plays":[],"launches":[{"path":" "}]}'
)) {
    Throws {Test-SetupBackupJson 'state.json' ([Text.Encoding]::UTF8.GetBytes($invalid))} 'Empty history path accepted'
}
Write-Output "Backup checks: $script:passed passed. Fixtures: $root"
