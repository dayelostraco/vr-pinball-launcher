BeforeAll {
    $script:toolsDirectory = Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'tools'
}

Describe 'Setup and backup integration contracts' {
    It 'guides a new user when executable or tables paths are blank' {
        Import-Module (Join-Path $toolsDirectory 'SetupCore.psm1') -Force -DisableNameChecking
        $exe = Join-Path $TestDrive 'VPinballX64.exe'
        Set-Content -LiteralPath $exe -Value 'Presence fixture, never executed'
        $config = [pscustomobject]@{ vpinballExecutable = $exe }
        foreach ($blank in @('', '   ')) {
            { Get-SetupPlan $TestDrive $config $blank $TestDrive 'Direct' } |
                Should -Throw '*Choose the actual VPinballX executable*'
            { Get-SetupPlan $TestDrive $config $exe $blank 'Direct' } |
                Should -Throw '*Choose an existing tables folder*'
        }
        Test-Path -LiteralPath (Join-Path $TestDrive 'launcher-config.json') | Should -BeFalse
    }
    It 'preserves existing settings and rolls back failed writes' {
        & (Join-Path $toolsDirectory 'test-setup.ps1')
    }
    It 'rejects unsafe backups and preserves unrelated files' {
        & (Join-Path $toolsDirectory 'test-backup.ps1')
    }
    It 'agrees with the shared launch contract' {
        & (Join-Path $toolsDirectory 'test-installation-contract.ps1')
    }
    It 'verifies download integrity before installation' {
        & (Join-Path $toolsDirectory 'test-downloads.ps1')
    }
    It 'forwards helper arguments, environment and child exit codes safely' {
        & (Join-Path $toolsDirectory 'test-vpx-helper.ps1')
    }
    It 'runs the setup form against a disposable table fixture' {
        & (Join-Path $toolsDirectory 'test-setup-ui.ps1')
    }
    It 'renders all setup pages and reviews paths without applying changes' {
        $app = Join-Path $TestDrive 'Preview'
        $tables = Join-Path $app 'Tables'
        $pictures = Join-Path $app 'Pictures'
        New-Item -ItemType Directory -Force -Path $tables | Out-Null
        $exe = Join-Path $app 'VPinballX_SETUP_TEST.exe'
        [IO.File]::WriteAllText($exe, 'Presence fixture, never executed')
        $config = Join-Path $app 'launcher-config.json'
        # An empty table path should suggest the adjacent Tables folder in the
        # form, while leaving the saved configuration unchanged until Apply.
        @{ vpinballExecutable = $exe; tablesDirectory = ''; searchSubdirectories = $true } |
            ConvertTo-Json | Set-Content -LiteralPath $config
        $before = (Get-FileHash -LiteralPath $config).Hash
        & (Join-Path $toolsDirectory 'setup.ps1') -LauncherDirectory $app -PreviewDirectory $pictures
        @(Get-ChildItem -LiteralPath $pictures -Filter '*.png').Count | Should -Be 5
        (Get-FileHash -LiteralPath $config).Hash | Should -Be $before
        Test-Path -LiteralPath (Join-Path $app 'SetupBackups') | Should -BeFalse
    }
    It 'supports read-only health reports and rejects damaged configuration without replacing it' {
        $app = Join-Path $TestDrive 'Reports'
        New-Item -ItemType Directory -Path $app | Out-Null
        $report = & (Join-Path $toolsDirectory 'setup.ps1') -LauncherDirectory $app -ReportOnly
        @($report | ConvertFrom-Json).Count | Should -BeGreaterThan 0
        $config = Join-Path $app 'launcher-config.json'
        Test-Path -LiteralPath $config | Should -BeFalse
        Set-Content -LiteralPath $config -Value '{ broken JSON'
        { & (Join-Path $toolsDirectory 'setup.ps1') -LauncherDirectory $app -ReportOnly } | Should -Throw
        Get-Content -LiteralPath $config | Should -Be '{ broken JSON'
        { & (Join-Path $toolsDirectory 'setup.ps1') -LauncherDirectory $app -SmokeTest } | Should -Throw '*dedicated*fixture*'
    }
}
