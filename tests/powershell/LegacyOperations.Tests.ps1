BeforeAll {
    $repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
}

Describe 'Legacy component operations in an isolated installation' {
    BeforeEach {
        . (Join-Path $repo 'install.ps1')
        $rootDir = Join-Path $TestDrive ([Guid]::NewGuid().ToString('N'))
        $bridgeDir = Join-Path $rootDir 'ControllerBridge'
        $scriptPath = Join-Path $bridgeDir 'vpx_vr_controls.ahk'
        $VpxDir = Join-Path $rootDir 'VPX'
        $TablesDir = ''
        $Yes = $true
        $oldAppData = $env:APPDATA
        $env:APPDATA = Join-Path $rootDir 'AppData'
        New-Item -ItemType Directory -Force $rootDir,$bridgeDir,$VpxDir,$env:APPDATA | Out-Null
        Mock Write-Host {}
        Mock Start-Process { throw 'Unexpected external process in fixture' }
        Mock Get-ItemProperty { $null }
        Mock Test-Path { $false } -ParameterFilter { $Path -like 'HK*:*' -or $Path -like 'Registry::*' }
        Mock Get-TrustedDownload { throw 'Unexpected download in fixture' }
    }
    AfterEach { $env:APPDATA = $oldAppData }

    It 'creates, finds and updates configuration without dropping unrelated values' {
        $path = Get-LauncherConfigPath
        $path | Should -Be (Join-Path $rootDir 'launcher-config.json')
        Set-Content -LiteralPath $path -Value '{"custom":{"keep":42}}'
        Set-LauncherConfig @{ tablesDirectory = 'D:\Tables' }
        $config = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
        $config.custom.keep | Should -Be 42
        $config.tablesDirectory | Should -Be 'D:\Tables'
        @(Get-ChildItem -LiteralPath $rootDir -Filter '*.bak-setup-*').Count | Should -Be 1
        Remove-Item -LiteralPath $path
        $bridgeConfig = Join-Path $bridgeDir 'launcher-config.json'
        Set-Content -LiteralPath $bridgeConfig -Value '{}'
        Get-LauncherConfigPath | Should -Be $bridgeConfig
    }

    It 'changes only the requested INI key and removes duplicate keys' {
        $ini = Join-Path $rootDir 'nested\player.ini'
        Set-IniValue $ini PlayerVR AskToTurnOn 0
        [IO.File]::ReadAllText($ini) | Should -Match 'AskToTurnOn = 0'
        [IO.File]::WriteAllLines($ini, @('[Other]', 'Keep=1', '[PlayerVR]', 'AskToTurnOn=2', 'AskToTurnOn=1', '[Last]', 'Keep=2'))
        Set-IniValue $ini PlayerVR AskToTurnOn 0
        $content = [IO.File]::ReadAllText($ini)
        ([regex]::Matches($content, 'AskToTurnOn')).Count | Should -Be 1
        $content | Should -Match 'Keep=1'
        $content | Should -Match 'Keep=2'
        [IO.File]::WriteAllLines($ini, @('[PlayerVR]', 'Other=1', '[Last]'))
        Set-IniValue $ini PlayerVR AskToTurnOn 0
        [IO.File]::ReadAllText($ini) | Should -Match 'AskToTurnOn = 0'
        [IO.File]::WriteAllLines($ini, @('[PlayerVR]', 'Other=1'))
        Set-IniValue $ini PlayerVR AskToTurnOn 0
        [IO.File]::WriteAllLines($ini, @('[Other]', 'Keep=1'))
        Set-IniValue $ini PlayerVR AskToTurnOn 0
        [IO.File]::ReadAllText($ini) | Should -Match '\[PlayerVR\]'
    }

    It 'selects preferred, configured and helper-resolved players, ignoring malformed JSON' {
        Mock Test-Path { $false } -ParameterFilter { ($Path -like 'C:\*' -and $Path -notlike ($rootDir + '*')) -or $Path -like 'D:\Visual Pinball*' }
        $exe = Join-Path $VpxDir 'VPinballX_BGFX64.exe'; Set-Content $exe 'fixture'
        Find-Vpx $VpxDir | Should -Be $exe
        @{vpinballExecutable=$exe} | ConvertTo-Json | Set-Content (Join-Path $rootDir 'launcher-config.json')
        Find-Vpx | Should -Be $exe
        @{vpinballExecutable=(Join-Path $rootDir 'Helper.exe')} | ConvertTo-Json | Set-Content (Join-Path $rootDir 'launcher-config.json')
        Mock Read-SetupConfig { [pscustomobject]@{} }
        Mock Get-SetupVpxTarget { [pscustomobject]@{Target=$exe} }
        Find-Vpx | Should -Be $exe
        Set-Content (Join-Path $rootDir 'launcher-config.json') 'bad JSON'
        Find-Vpx | Should -BeNullOrEmpty
    }

    It 'extracts verified VPX and rejects an archive without the expected program' {
        $archive = Join-Path $rootDir 'vpx.zip'; Set-Content $archive 'fixture'
        Mock Get-TrustedDownload { $archive }
        Mock Expand-Archive { New-Item -ItemType Directory -Force $DestinationPath | Out-Null; Set-Content (Join-Path $DestinationPath 'VPinballX_GL64.exe') 'fixture' }
        $destination = Join-Path $rootDir 'Downloaded'
        Install-VpxFromGitHub $destination | Should -Be (Join-Path $destination 'VPinballX_GL64.exe')
        Should -Invoke Get-TrustedDownload -Times 1 -ParameterFilter { $Name -eq 'vpx' }
        Set-Content $archive 'fixture'
        Mock Expand-Archive {}
        { Install-VpxFromGitHub (Join-Path $rootDir 'Empty') } | Should -Throw '*not found*'
    }

    It 'extracts verified VPinMAME to its chosen destination' {
        $archive = Join-Path $rootDir 'vpm.zip'; Set-Content $archive 'fixture'
        Mock Get-TrustedDownload { $archive }
        Mock Expand-Archive {}
        Install-VPinMAMEFromGitHub (Join-Path $rootDir 'New VPM')
        Should -Invoke Get-TrustedDownload -Times 1 -ParameterFilter { $Name -eq 'vpinmame' }
        Should -Invoke Expand-Archive -Times 1 -ParameterFilter { $Force }
    }

    It 'uses registry ROM paths or the adjacent fallback' {
        Get-RomShortcutPath 'relative;"D:\Custom ROMs";E:\Other' | Should -Be 'D:\Custom ROMs'
        Get-RomShortcutPath 'relative;;' | Should -BeNullOrEmpty
        Get-RomPath '' | Should -BeNullOrEmpty
        Get-RomPath (Join-Path $VpxDir 'player.exe') | Should -Be (Join-Path $VpxDir 'VPinMAME\roms')
        Mock Test-Path { $true } -ParameterFilter { $Path -like 'HKCU:*' }
        Mock Get-ItemProperty { [pscustomobject]@{rompath='D:\Custom ROMs'} }
        Get-RomPath '' | Should -Be 'D:\Custom ROMs'
    }

    It 'creates shortcuts through the shell without writing a real desktop shortcut' {
        $script:shortcut = [pscustomobject]@{TargetPath='';WorkingDirectory='';Description='';Saved=$false}
        $shortcut | Add-Member ScriptMethod Save { $this.Saved = $true }
        $shell = [pscustomobject]@{}
        $shell | Add-Member ScriptMethod CreateShortcut { param($path) return $script:shortcut }
        Mock New-Object { $shell } -ParameterFilter { $ComObject -eq 'WScript.Shell' }
        New-DesktopFolderShortcut Unknown ''
        $folder = Join-Path $rootDir 'New Tables'
        New-DesktopFolderShortcut Tables $folder
        $shortcut.TargetPath | Should -Be $folder
        $shortcut.Saved | Should -BeTrue
    }

    It 'resolves an existing COM registration and handles an absent one' {
        Get-VPinMAMERegisteredDll | Should -BeNullOrEmpty
        Mock Get-ItemProperty { [pscustomobject]@{'(default)'='{fixture}'} } -ParameterFilter { $Path -like '*VPinMAME.Controller*' }
        Get-VPinMAMERegisteredDll | Should -BeNullOrEmpty
        Mock Test-Path { $true } -ParameterFilter { $Path -like '*InprocServer32' }
        Mock Get-ItemProperty { [pscustomobject]@{'(default)'='D:\Fixture.dll'} } -ParameterFilter { $Path -like '*InprocServer32' }
        Get-VPinMAMERegisteredDll | Should -Be 'D:\Fixture.dll'
    }

    It 'skips existing registration, accepts success and reports declined or failed elevation' {
        $dll = Join-Path $rootDir 'vpm.dll'; Set-Content $dll 'fixture'
        Mock Get-VPinMAMERegisteredDll { $dll }
        Register-VPinMAME $dll | Should -BeTrue
        Should -Invoke Start-Process -Times 0
        Mock Get-VPinMAMERegisteredDll { $null }
        Mock Start-Process { [pscustomobject]@{ExitCode=0} }
        Register-VPinMAME $dll | Should -BeTrue
        Mock Start-Process { [pscustomobject]@{ExitCode=1} } -ParameterFilter { !$Verb }
        Register-VPinMAME $dll | Should -BeTrue
        Mock Start-Process { [pscustomobject]@{ExitCode=1} } -ParameterFilter { $Verb -eq 'RunAs' }
        Register-VPinMAME $dll | Should -BeFalse
        Mock Start-Process { throw 'Elevation declined' } -ParameterFilter { $Verb -eq 'RunAs' }
        Register-VPinMAME $dll | Should -BeFalse
    }

    It 'configures an existing VPX using the verified INI and preserves its helper and tables' {
        $exe=Join-Path $VpxDir 'VPinballX_BGFX64.exe'; Set-Content $exe 'fixture'
        $helper=Join-Path $rootDir 'Helper.exe'; Set-Content $helper 'fixture'
        $tables=Join-Path $rootDir 'Tables'; New-Item -ItemType Directory $tables | Out-Null
        @{vpinballExecutable=$helper; tablesDirectory=$tables} | ConvertTo-Json | Set-Content (Join-Path $rootDir 'launcher-config.json')
        $ini=Join-Path $VpxDir 'VPinballX.ini'; Set-Content $ini '[PlayerVR]'
        Mock Find-Vpx { $exe }
        Mock Get-SetupIniLocation { [pscustomobject]@{Verified=$true;Path=$ini} }
        Mock Get-SetupVpxTarget { [pscustomobject]@{Kind='Launch helper';Target=$exe} }
        Mock New-DesktopFolderShortcut {}
        Install-VpxVR
        $cfg=Get-Content (Join-Path $rootDir 'launcher-config.json') -Raw | ConvertFrom-Json
        $cfg.vpinballExecutable | Should -Be $helper
        $cfg.tablesDirectory | Should -Be $tables
        @(Get-ChildItem $VpxDir -Filter '*.bak-setup-*').Count | Should -Be 1
    }

    It 'honors declined VPX download and resolves tables after an accepted download' {
        Mock Find-Vpx { $null }
        Mock Read-Host { 'n' }
        $Yes=$false
        Install-VpxVR
        Should -Invoke Get-TrustedDownload -Times 0
        $exe=Join-Path $VpxDir 'VPinballX_GL64.exe'
        $tables=Join-Path $VpxDir 'Tables'; New-Item -ItemType Directory $tables | Out-Null
        Mock Read-Host { 'yes' }
        Mock Install-VpxFromGitHub { $exe }
        Mock Get-SetupIniLocation { [pscustomobject]@{Verified=$false} }
        Mock Get-SetupVpxTarget { [pscustomobject]@{} }
        Mock New-DesktopFolderShortcut {}
        Install-VpxVR
        $cfg=Get-Content (Join-Path $rootDir 'launcher-config.json') -Raw | ConvertFrom-Json
        $cfg.tablesDirectory | Should -Be $tables
        $Yes=$true
        Install-VpxVR
        Should -Invoke Install-VpxFromGitHub -Times 2
    }

    It 'validates bridge prerequisites and records an existing interpreter' {
        { Install-ControllerBridge } | Should -Throw '*Experimental Touch add-on is not installed*'
        Set-Content $scriptPath ';fixture'
        Mock Test-Path { $true } -ParameterFilter { $Path -eq 'C:\Program Files\AutoHotkey\AutoHotkeyU64.exe' }
        Install-ControllerBridge
        $cfg=Get-Content (Join-Path $rootDir 'launcher-config.json') -Raw | ConvertFrom-Json
        $cfg.enableControllerBridge | Should -BeTrue
        $cfg.controllerBridgeArgs | Should -Be ('"' + $scriptPath + '"')
    }

    It 'skips an absent experimental add-on for Everything but honors explicit setup' {
        Mock Install-ControllerBridge {}
        Invoke-BridgeSetup 'vpx'
        Invoke-BridgeSetup 'both'
        Should -Invoke Install-ControllerBridge -Times 0 -Exactly
        Invoke-BridgeSetup 'bridge'
        Set-Content -LiteralPath $scriptPath -Value '; fixture add-on'
        Invoke-BridgeSetup 'both'
        Should -Invoke Install-ControllerBridge -Times 2 -Exactly
    }

    It 'fails clearly when the optional interpreter installation does not finish' {
        Set-Content $scriptPath ';fixture'
        Mock Test-Path { $false } -ParameterFilter { $Path -like '*AutoHotkey*' }
        Mock Get-TrustedDownload { 'C:\Fixture\installer.exe' }
        Mock Start-Process {}
        { Install-ControllerBridge } | Should -Throw '*still not found*'
        Should -Invoke Start-Process -Times 1 -ParameterFilter { $FilePath -eq 'C:\Fixture\installer.exe' -and $Wait }
    }
}
