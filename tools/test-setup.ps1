$ErrorActionPreference='Stop'
Import-Module (Join-Path $PSScriptRoot 'SetupCore.psm1') -Force -DisableNameChecking
if(Get-Process -Name 'vr-launch','VPinballX_BGFX64','VPinballX_GL64','VPinballX64' -ErrorAction SilentlyContinue){throw 'Close the launcher and VPX before testing setup writes in temporary folders.'}
$root=Join-Path ([IO.Path]::GetTempPath()) ('vpx-setup-tests-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $root | Out-Null
$script:passed=0
function Assert($Condition,[string]$Message){if(!$Condition){throw $Message};$script:passed++}
function Assert-Throws([scriptblock]$Action,[string]$Message){$thrown=$false;try{&$Action|Out-Null}catch{$thrown=$true};Assert $thrown $Message}
function Bytes-Equal([byte[]]$A,[byte[]]$B){return [Convert]::ToBase64String($A) -ceq [Convert]::ToBase64String($B)}
$app=Join-Path $root 'Launcher';$vp=Join-Path $root 'Existing VPX';$tables=Join-Path $root 'Tables';$tools=Join-Path $app 'tools'
New-Item -ItemType Directory -Force $app,$vp,$tables,$tools | Out-Null
$exe=Join-Path $vp 'VPinballX_BGFX64.exe';[IO.File]::WriteAllText($exe,'Presence fixture - never executed')
$cfgPath=Join-Path $app 'launcher-config.json'
$unicodeName='Caf'+[char]0xE9
$original=[ordered]@{vpinballExecutable=$exe;tablesDirectory=$tables;searchSubdirectories=$true;vpxProfiles=@([ordered]@{name='Special cleanup';executable='C:\custom\host.exe';iniFile='C:\custom\profile.ini';workingDirectory='table'});tableProfiles=@([ordered]@{table='Favorite table';profile='Special cleanup'});customFutureSetting=[ordered]@{name=$unicodeName;list=@(1,2,3)};wheelDirectory='Custom art'}
[IO.File]::WriteAllText($cfgPath,($original|ConvertTo-Json -Depth 20),[Text.UTF8Encoding]::new($false))
$originalBytes=[IO.File]::ReadAllBytes($cfgPath)
$config=Read-SetupConfig $app
$target=Get-SetupVpxTarget $exe
Assert ($target.Kind -eq 'VPX directly' -and $target.Target -eq $exe) 'Native VPX discovery failed'
$helper=Join-Path $vp 'VpxBackgroundInput.exe';[IO.File]::WriteAllText($helper,'Legacy helper fixture')
[IO.File]::WriteAllText([IO.Path]::ChangeExtension($helper,'.cs'),('private const string VpxPath = @"'+$exe+'";'))
$legacy=Get-SetupVpxTarget $helper
Assert ($legacy.Target -eq $exe -and $legacy.Kind -eq 'Launch helper') 'Legacy helper target detection failed'
$pathFile=Join-Path $vp 'VpxBackgroundInput.vpx-path.txt';[IO.File]::WriteAllText($pathFile,$exe)
Assert ((Get-SetupVpxTarget $helper).Evidence -eq $pathFile) 'Configured helper sidecar was not preferred'
$config.vpinballExecutable=$helper
$other=Join-Path $root 'Other VPX';New-Item -ItemType Directory $other | Out-Null
$otherExe=Join-Path $other 'VPinballX_BGFX64.exe';[IO.File]::WriteAllText($otherExe,'Other fixture')
Assert-Throws {Get-SetupPlan $app $config $otherExe $tables 'Keep'} 'Helper target mismatch should block keep mode'
Assert-Throws {Get-SetupPlan $app $config $helper $tables 'Direct'} 'A helper must not be accepted as actual VPX'
$config=Read-SetupConfig $app
$plan=Get-SetupPlan $app $config $exe $tables 'Direct'
Assert (Bytes-Equal $originalBytes ([IO.File]::ReadAllBytes($cfgPath))) 'Planning modified the configuration'
$backup=Save-SetupPlan $plan
$saved=Read-SetupConfig $app
Assert ($saved.realVpinballExecutable -eq $exe) 'Actual executable was not saved'
Assert ($saved.customFutureSetting.list.Count -eq 3 -and $saved.customFutureSetting.name -eq $unicodeName) 'Unknown or Unicode settings were lost'
Assert ($saved.vpxProfiles.Count -eq 1 -and $saved.vpxProfiles[0].iniFile -eq 'C:\custom\profile.ini') 'Profile changed'
Assert ($saved.tableProfiles[0].profile -eq 'Special cleanup' -and $saved.wheelDirectory -eq 'Custom art') 'Assignments or media settings changed'
Assert (Bytes-Equal $originalBytes ([IO.File]::ReadAllBytes((Join-Path $backup 'launcher-config.json')))) 'Original config backup was not exact'
$stale=Get-SetupPlan $app $saved $exe $tables 'Keep'
$saved | Add-Member -NotePropertyName changedExternally -NotePropertyValue $true -Force
[IO.File]::WriteAllText($cfgPath,($saved|ConvertTo-Json -Depth 30))
Assert-Throws {Save-SetupPlan $stale} 'Stale configuration should not overwrite external edits'
$portable=Join-Path $vp 'VPinballX.ini';$appData=Join-Path $root 'AppData';$versioned=Join-Path $appData 'VPinballX\10.8'
New-Item -ItemType Directory -Force $versioned | Out-Null
[IO.File]::WriteAllText($portable,"[PlayerVR]`r`nAskToTurnOn = 2`r`n")
$active=Join-Path $versioned 'VPinballX.ini';[IO.File]::WriteAllText($active,"[PlayerVR]`nAskToTurnOn = 0`n")
$log=Join-Path $versioned 'vpinball.log'
[IO.File]::WriteAllText($log,"Starting VPX - test`nSettings file was loaded from $active`nApplication path: $vp\`n")
$ini=Get-SetupIniLocation $exe $appData
Assert ($ini.Verified -and $ini.Path -eq $active) 'Active versioned INI was not confirmed from log'
[IO.File]::AppendAllText($log,"Starting VPX - another install`nSettings file was loaded from $active`nApplication path: C:\Elsewhere\`n")
$ini=Get-SetupIniLocation $exe $appData
Assert (!$ini.Verified -and $ini.Path -eq $portable) 'Unrelated latest run must not confirm this install'
foreach($encoding in @([Text.Encoding]::ASCII,[Text.UTF8Encoding]::new($true),[Text.Encoding]::Unicode,[Text.Encoding]::BigEndianUnicode)){
    $content="[PlayerVR]`r`n; preserve comments`r`nAskToTurnOn = 2`r`n[Input]`r`nMapping.Flip = unchanged`r`n"
    $file=Join-Path $root ('ini-'+$encoding.CodePage+'.ini');$bytes=[byte[]]($encoding.GetPreamble()+$encoding.GetBytes($content));[IO.File]::WriteAllBytes($file,$bytes)
    Set-SetupVrIni $file
    $expected=[byte[]]($encoding.GetPreamble()+$encoding.GetBytes($content.Replace('AskToTurnOn = 2','AskToTurnOn = 0')))
    Assert (Bytes-Equal $expected ([IO.File]::ReadAllBytes($file))) ('INI preservation failed for '+$encoding.WebName)
}
$missingKey=Join-Path $root 'missing-key.ini';[IO.File]::WriteAllText($missingKey,"[PlayerVR]`nScaleRelative = 1`n[Input]`nKeep=1`n")
Set-SetupVrIni $missingKey
Assert ((Read-SetupIni $missingKey)['PlayerVR/AskToTurnOn'] -eq '0') 'Missing VR key was not inserted'
Assert ((Read-SetupIni $missingKey)['Input/Keep'] -eq '1') 'Unrelated section was damaged'
$mixed=Join-Path $root 'mixed-case.ini';[IO.File]::WriteAllText($mixed,"[playervr]`naSkToTurnOn=2`n[Input]`nKeep=1")
Set-SetupVrIni $mixed
Assert ([IO.File]::ReadAllText($mixed) -ceq "[playervr]`naSkToTurnOn= 0`n[Input]`nKeep=1") 'Mixed-case section/key was not preserved'
$headerOnly=Join-Path $root 'header-only.ini';[IO.File]::WriteAllText($headerOnly,'[PlayerVR]');Set-SetupVrIni $headerOnly
Assert ((Read-SetupIni $headerOnly)['PlayerVR/AskToTurnOn'] -eq '0') 'Header without final newline was not handled'
$saved=Read-SetupConfig $app;$rollback=Get-SetupPlan $app $saved $exe $tables 'Keep';$rollback.EnableVR=$true;$rollback.IniPath=$portable
$iniBefore=[IO.File]::ReadAllBytes($portable);$configBefore=[IO.File]::ReadAllBytes($cfgPath)
New-Item -ItemType Directory -Path ($cfgPath+'.setup-tmp') | Out-Null
Assert-Throws {Save-SetupPlan $rollback} 'A blocked config write should fail'
Assert (Bytes-Equal $iniBefore ([IO.File]::ReadAllBytes($portable))) 'Failed apply did not restore INI'
Assert (Bytes-Equal $configBefore ([IO.File]::ReadAllBytes($cfgPath))) 'Failed apply did not restore config'
# Remove only this explicitly named, generated empty directory; no recursive delete.
Remove-Item -LiteralPath ($cfgPath+'.setup-tmp')
$table=Join-Path $tables 'Favorite table.vpx';[IO.File]::WriteAllText($table,'Table fixture')
$profileExe=Join-Path $root 'SpecialHost.exe';[IO.File]::WriteAllText($profileExe,'Host fixture')
$saved.vpxProfiles[0].executable=$profileExe
$command=Get-SetupTestCommand $saved $table $app
Assert ($command.Profile -eq 'Special cleanup' -and $command.Executable -eq $profileExe) 'Test did not respect per-table profile'
Assert ($command.WorkingDirectory -eq $tables -and $command.Arguments.Contains('-Ini "C:\custom\profile.ini"')) 'Profile working directory or INI was lost'
$unassigned=Join-Path $tables 'Another.vpx';[IO.File]::WriteAllText($unassigned,'Table fixture')
Assert ((Get-SetupTestCommand $saved $unassigned $app).Executable -eq $exe) 'Unassigned table did not use default runner'
$badConfig=Join-Path $root 'Bad config';New-Item -ItemType Directory $badConfig | Out-Null
$badPath=Join-Path $badConfig 'launcher-config.json';[IO.File]::WriteAllText($badPath,'{bad')
Assert-Throws {Read-SetupConfig $badConfig} 'Malformed config should fail without overwriting'
Assert ([IO.File]::ReadAllText($badPath) -eq '{bad') 'Malformed config was overwritten'
$health=@(Get-SetupHealth $app $saved $exe $tables)
Assert (@($health|Where-Object {$_.Component -eq 'Native Serum' -and $_.Status -eq 'Needs testing'}).Count -eq 1) 'Absent optional plugin should require testing, not fail setup'
Assert (@($health|Where-Object {$_.Blocking}).Count -eq 0) 'Optional component blocked an existing install'

# Exercise Windows argument quoting through a real native process, not by comparing implementation strings.
$argSource=Join-Path $root 'ArgumentEcho.cs';$argExe=Join-Path $root 'ArgumentEcho.exe'
[IO.File]::WriteAllText($argSource,'using System; using System.Text; class Echo { static void Main(string[] args) { foreach(var a in args) Console.WriteLine(Convert.ToBase64String(Encoding.UTF8.GetBytes(a))); } }')
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:exe "/out:$argExe" $argSource
if($LASTEXITCODE -ne 0){throw 'Argument fixture compilation failed'}
$arguments=@('','table path with spaces.vpx','C:\ending\','embedded"quote','literal $(no command) `tick')
$start=[Diagnostics.ProcessStartInfo]::new();$start.FileName=$argExe;$start.Arguments=($arguments|ForEach-Object {Quote-SetupArgument $_}) -join ' ';$start.UseShellExecute=$false;$start.CreateNoWindow=$true;$start.RedirectStandardOutput=$true
$process=[Diagnostics.Process]::Start($start);$output=$process.StandardOutput.ReadToEnd();$process.WaitForExit();$process.Dispose()
$lines=$output.TrimEnd("`r","`n").Split([string[]]@("`r`n","`n"),[StringSplitOptions]::None)
Assert ($lines.Count -eq $arguments.Count) 'Argument count changed'
for($i=0;$i -lt $arguments.Count;$i++){Assert ([Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($lines[$i])) -ceq $arguments[$i]) ('Argument did not round-trip at '+$i)}

foreach($name in @('VpxBackgroundInput.cs','install-vpx-background-input.ps1')){Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination $tools}
$saved=Read-SetupConfig $app;$helperPlan=Get-SetupPlan $app $saved $exe $tables 'Helper';$null=Save-SetupPlan $helperPlan
$helperSaved=Read-SetupConfig $app
Assert ($helperSaved.vpinballExecutable -eq (Join-Path $tools 'VpxBackgroundInput.exe')) 'Helper not wired into config'
Assert ([IO.File]::ReadAllText((Join-Path $tools 'VpxBackgroundInput.vpx-path.txt')) -eq $exe) 'Helper target was not set'
Assert ($helperSaved.tableProfiles[0].profile -eq 'Special cleanup') 'Installing helper changed special profiles'
$beforeHelper=[IO.File]::ReadAllBytes((Join-Path $tools 'VpxBackgroundInput.exe'))
$beforeTarget=[IO.File]::ReadAllBytes((Join-Path $tools 'VpxBackgroundInput.vpx-path.txt'))
$failedHelperPlan=Get-SetupPlan $app $helperSaved $otherExe $tables 'Helper'
New-Item -ItemType Directory -Path ($cfgPath+'.setup-tmp') | Out-Null
Assert-Throws {Save-SetupPlan $failedHelperPlan} 'Helper/config transaction should fail when config cannot be written'
Assert (Bytes-Equal $beforeHelper ([IO.File]::ReadAllBytes((Join-Path $tools 'VpxBackgroundInput.exe')))) 'Failed apply did not restore existing helper'
Assert (Bytes-Equal $beforeTarget ([IO.File]::ReadAllBytes((Join-Path $tools 'VpxBackgroundInput.vpx-path.txt')))) 'Failed apply did not restore helper target'
$installer=Join-Path (Split-Path $PSScriptRoot -Parent) 'install.ps1'
$tokens=$null;$parseErrors=$null;$ast=[Management.Automation.Language.Parser]::ParseFile($installer,[ref]$tokens,[ref]$parseErrors)
Assert ($parseErrors.Count -eq 0) 'Installer parse failed'
$findFunction=$ast.Find({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Find-Vpx'},$true)
. ([scriptblock]::Create($findFunction.Extent.Text))
$rootDir=$app
Assert ((Find-Vpx -PreferDir $vp) -eq $exe) 'Legacy installer did not detect an existing BGFX executable'
Assert ((Find-Vpx -PreferDir '') -eq $exe) 'Legacy installer did not resolve the saved helper target'
foreach($name in @('Get-LauncherConfigPath','Set-LauncherConfig','Set-IniValue','Get-RomPath','Get-RomShortcutPath','Install-VpxVR')){
    $definition=$ast.Find({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name},$true)
    . ([scriptblock]::Create($definition.Extent.Text))
}
$bridgeDir=Join-Path $app 'ControllerBridge';$VpxDir=$vp;$TablesDir='';$Yes=$true
function New-DesktopFolderShortcut { param($Name,$TargetDir) } # Do not create real desktop shortcuts in tests.
New-Item -ItemType Directory -Force (Join-Path $vp 'Tables') | Out-Null
Install-VpxVR | Out-Null
$legacySaved=Read-SetupConfig $app
Assert ($legacySaved.vpinballExecutable -eq (Join-Path $tools 'VpxBackgroundInput.exe')) 'Legacy VR setup replaced a working helper'
Assert ($legacySaved.tablesDirectory -eq $tables) 'Legacy VR setup replaced an existing custom tables folder'
Assert ($legacySaved.customFutureSetting.name -eq $unicodeName) 'Legacy VR setup damaged Unicode custom settings'
Write-Output "Setup checks: $script:passed passed. Fixtures and backups: $root"
