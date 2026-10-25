BeforeAll {
    # Dot-source the production file: functions retain their source locations for
    # coverage, while its public loading contract skips the interactive dispatcher.
    $installer = Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'install.ps1'
    . $installer
}

Describe 'Legacy VPinMAME setup preservation' {
    BeforeEach {
        $script:actualPlayer = Join-Path $TestDrive 'Actual VPX\VPinballX_BGFX64.exe'
        $script:vpmDirectory = Join-Path (Split-Path $actualPlayer -Parent) 'VPinMAME'
        New-Item -ItemType Directory -Force -Path $vpmDirectory | Out-Null
        Set-Content -LiteralPath (Join-Path $vpmDirectory 'VPinMAME64.dll') -Value 'fixture'
        Mock Find-Vpx { $actualPlayer }
        Mock Register-VPinMAME { $true }
        Mock Get-VPinMAMERegisteredDll { $null }
        Mock New-DesktopFolderShortcut {}
        Mock Set-ItemProperty {}
        Mock Test-Path { $true } -ParameterFilter { $Path -like 'HKCU:*' }
        Mock Get-ItemProperty { [pscustomobject]@{} } -ParameterFilter { $LiteralPath -like 'HKCU:*' }
        Mock Get-ItemProperty { $null } -ParameterFilter { $LiteralPath -like 'HKLM:*' }
    }
    It 'preserves existing ROM and sample paths including multi-library configuration' {
        Mock Get-ItemProperty { [pscustomobject]@{ rompath = '"C:\Custom ROMs";D:\Other ROMs'; samplepath = 'D:\Samples' } } -ParameterFilter { $LiteralPath -like 'HKCU:*' }
        Install-VPinMAME
        Should -Invoke Set-ItemProperty -Times 0
        Should -Invoke Register-VPinMAME -Times 1 -ParameterFilter { $Dll -eq (Join-Path $vpmDirectory 'VPinMAME64.dll') }
        Should -Invoke New-DesktopFolderShortcut -Times 1 -ParameterFilter { $TargetDir -eq 'C:\Custom ROMs' }
    }
    It 'does not claim readiness after registration failed or was declined' {
        Mock Register-VPinMAME { $false }
        { Install-VPinMAME } | Should -Throw '*registration did not complete*'
        Should -Invoke New-DesktopFolderShortcut -Times 0
    }
    It 'fills only missing paths beside the resolved actual VPX installation' {
        Install-VPinMAME
        Should -Invoke Set-ItemProperty -Times 1 -ParameterFilter { $Name -eq 'rompath' -and $Value -eq (Join-Path $vpmDirectory 'roms') }
        Should -Invoke Set-ItemProperty -Times 1 -ParameterFilter { $Name -eq 'samplepath' -and $Value -eq (Join-Path $vpmDirectory 'samples') }
        Should -Invoke Register-VPinMAME -Times 1 -ParameterFilter { $Dll -eq (Join-Path $vpmDirectory 'VPinMAME64.dll') }
    }
    It 'inherits machine paths without replacing per-user overrides' {
        Mock Get-ItemProperty { [pscustomobject]@{ samplepath = 'D:\User Samples' } } -ParameterFilter { $LiteralPath -like 'HKCU:*' }
        Mock Get-ItemProperty { [pscustomobject]@{ rompath = 'D:\Machine ROMs'; samplepath = 'D:\Machine Samples' } } -ParameterFilter { $LiteralPath -like 'HKLM:*' }
        Install-VPinMAME
        Should -Invoke Set-ItemProperty -Times 1 -ParameterFilter { $Name -eq 'rompath' -and $Value -eq 'D:\Machine ROMs' }
        Should -Invoke Set-ItemProperty -Times 0 -ParameterFilter { $Name -eq 'samplepath' }
        Should -Invoke New-DesktopFolderShortcut -Times 1 -ParameterFilter { $TargetDir -eq 'D:\Machine ROMs' }
    }
    It 'keeps a working VPinMAME installation registered in another directory' {
        $script:existingDll = Join-Path $TestDrive 'Other Library\VPinMAME64.dll'
        New-Item -ItemType Directory -Force -Path (Split-Path $existingDll -Parent) | Out-Null
        Set-Content -LiteralPath $existingDll -Value 'registered fixture'
        Mock Get-VPinMAMERegisteredDll { $existingDll }
        Install-VPinMAME
        Should -Invoke Register-VPinMAME -Times 1 -ParameterFilter { $Dll -eq $existingDll }
        Should -Invoke Set-ItemProperty -Times 1 -ParameterFilter { $Name -eq 'rompath' -and $Value -eq (Join-Path (Split-Path $existingDll -Parent) 'roms') }
    }
}
