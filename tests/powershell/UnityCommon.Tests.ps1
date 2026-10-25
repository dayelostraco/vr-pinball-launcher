BeforeAll {
    . (Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'tools\UnityCommon.ps1')
}

Describe 'Unity editor discovery' {
    BeforeEach {
        Mock Test-Path { $false }
    }
    It 'accepts an explicitly installed editor and rejects a missing one' {
        Mock Test-Path { $true } -ParameterFilter { $Path -eq 'C:\fixture\Unity.exe' }
        Find-UnityExe -Explicit 'C:\fixture\Unity.exe' | Should -Be 'C:\fixture\Unity.exe'
        { Find-UnityExe -Explicit 'C:\missing.exe' } | Should -Throw '*not found*'
    }
    It 'reports a missing editor installation' {
        { Find-UnityExe -ProjectPath $TestDrive } | Should -Throw '*No Unity editors*'
    }
    It 'prefers the pinned editor over newer installations' {
        Mock Test-Path { $true }
        Mock Select-String { [pscustomobject]@{ Matches = @([regex]::Match('m_EditorVersion: 6000.6.2f1', '^m_EditorVersion:\s*(.+)$')) } }
        Find-UnityExe -ProjectPath $TestDrive | Should -Be 'C:\Program Files\Unity\Hub\Editor\6000.6.2f1\Editor\Unity.exe'
    }
    It 'warns before falling back when the pinned editor is unavailable' {
        Mock Test-Path { $Path -notlike '*6000.6.2f1*' }
        Mock Select-String { [pscustomobject]@{ Matches = @([regex]::Match('m_EditorVersion: 6000.6.2f1', '^m_EditorVersion:\s*(.+)$')) } }
        Mock Get-ChildItem { [pscustomobject]@{ Name = '6000.6.3f1'; FullName = 'C:\fixture\6000.6.3f1' } }
        Mock Write-Warning {}
        Find-UnityExe -ProjectPath $TestDrive | Should -Be 'C:\fixture\6000.6.3f1\Editor\Unity.exe'
        Should -Invoke Write-Warning -Times 1
    }
    It 'rejects an editor root with no usable executable' {
        Mock Test-Path { $Path -eq 'C:\Program Files\Unity\Hub\Editor' }
        Mock Get-ChildItem { @() }
        { Find-UnityExe -ProjectPath $TestDrive } | Should -Throw '*No Unity.exe*'
    }
}

Describe 'Unity process and license coordination' {
    BeforeEach {
        Mock Start-Sleep {}
        Mock Get-Process { @() }
        Mock Start-Process {}
    }
    It 'does not wait for an older editor process' {
        Mock Get-Process { [pscustomobject]@{ StartTime = [datetime]'2020-01-01'; Path = 'C:\fixture\Unity.exe' } }
        Wait-UnityExit -ExePath 'C:\fixture\Unity.exe' -Since ([datetime]'2026-01-01')
        Should -Invoke Start-Sleep -Times 0
    }
    It 'waits for the matching newly started editor but ignores other installs' {
        $script:polls = 0
        Mock Get-Process {
            $script:polls++
            if ($script:polls -eq 1) { [pscustomobject]@{ StartTime = [datetime]'2026-01-01'; Path = 'C:\fixture\Unity.exe' } }
            else { [pscustomobject]@{ StartTime = [datetime]'2026-01-01'; Path = 'C:\other\Unity.exe' } }
        }
        Wait-UnityExit -ExePath 'C:\fixture\Unity.exe' -Since ([datetime]'2026-01-01')
        Should -Invoke Start-Sleep -Times 1
    }
    It 'leaves an already running Hub alone' {
        Mock Get-Process { [pscustomobject]@{ Id = 123 } } -ParameterFilter { $Name -eq 'Unity Hub' }
        Ensure-UnityHub
        Should -Invoke Start-Process -Times 0
    }
    It 'starts Hub hidden and waits for the licensing client' {
        Mock Get-Process { [pscustomobject]@{ Id = 123 } } -ParameterFilter { $Name -eq 'Unity.Licensing.Client' }
        Ensure-UnityHub
        Should -Invoke Start-Process -Times 1 -ParameterFilter { $WindowStyle -eq 'Hidden' }
        Should -Invoke Start-Sleep -Times 1
    }
    It 'bounds the wait if the licensing client never starts' {
        Ensure-UnityHub
        Should -Invoke Start-Sleep -Times 20
    }
    It 'warns if Hub cannot be started' {
        Mock Start-Process { throw 'fixture startup failure' }
        Mock Write-Warning {}
        Ensure-UnityHub
        Should -Invoke Write-Warning -Times 1
        Should -Invoke Start-Sleep -Times 0
    }
}

Describe 'Editor lock diagnostics' {
    It 'rejects a logged project lock without mistaking ordinary output for a lock' {
        $log = Join-Path $TestDrive 'editor.log'
        Assert-NoOpenEditor -LogFile $log
        Set-Content -LiteralPath $log -Value 'Ordinary editor output'
        Assert-NoOpenEditor -LogFile $log
        Set-Content -LiteralPath $log -Value 'another Unity instance is running with this project open'
        { Assert-NoOpenEditor -LogFile $log } | Should -Throw '*Close it and retry*'
    }
}
