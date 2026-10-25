BeforeAll {
    $script:toolsDirectory = Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'tools'
    Import-Module (Join-Path $toolsDirectory 'SetupCore.psm1') -Force -DisableNameChecking
    Add-Type -AssemblyName System.Windows.Forms, System.Drawing
    function New-Label($Parent, $Text, $X, $Y, $Width, $Height) {
        $control = [Windows.Forms.Label]::new()
        $control.Text = $Text
        $Parent.Controls.Add($control)
        return $control
    }
    function New-Button($Parent, $Text, $X, $Y, $Width) {
        $control = [Windows.Forms.Button]::new()
        $control.Text = $Text
        $Parent.Controls.Add($control)
        return $control
    }
    function Show-Problem($Problem) { $script:reportedProblem = [string]$Problem }
    $script:LauncherDirectory = Join-Path $TestDrive 'Launcher'
    $script:form = [Windows.Forms.Form]::new()
    $script:pages = @(0..4 | ForEach-Object { [Windows.Forms.Panel]::new() })
    $script:review = [Windows.Forms.TextBox]::new()
    $script:status = [Windows.Forms.Label]::new()
    . (Join-Path $toolsDirectory 'SetupBackupPage.ps1')
}

AfterAll {
    foreach ($control in @($form, $review, $status) + $pages) { $control.Dispose() }
}

Describe 'Backup page review requirements' {
    BeforeEach {
        $script:backupArchive = 'fixture.zip'
        $restoreSettings.Checked = $true
        $restoreHistory.Checked = $true
        Mock New-SetupRestorePlan {
            [pscustomobject]@{
                CreatedUtc = '2026-10-24T00:00:00Z'
                Changes = @('Replace fixture configuration')
                Files = @{ (Join-Path $LauncherDirectory 'launcher-config.json') = [Text.Encoding]::UTF8.GetBytes('{"vpinballExecutable":"fixture.exe","tablesDirectory":"Tables","vpxProfiles":[]}') }
            }
        }
    }
    It 'shows proposed paths and enables restore only after a successful review' {
        Update-BackupReview
        $restoreBackup.Enabled | Should -BeTrue
        $backupReview.Text | Should -Match 'fixture.exe'
        $backupReview.Text | Should -Match 'Replace fixture configuration'
        $restoreHistory.Checked = $false
        $restoreBackup.Enabled | Should -BeFalse
        $script:restorePlan | Should -BeNullOrEmpty
        $backupReview.Text | Should -Match 'Options changed'
    }
    It 'clears an old plan after validation fails' {
        Update-BackupReview
        Mock New-SetupRestorePlan { throw 'fixture integrity failure' }
        Update-BackupReview
        $restoreBackup.Enabled | Should -BeFalse
        $script:restorePlan | Should -BeNullOrEmpty
        $backupReview.Text | Should -Be 'fixture integrity failure'
    }
    It 'requires a selected archive and supports history-only review' {
        $script:backupArchive = ''
        Update-BackupReview
        $restoreBackup.Enabled | Should -BeFalse
        $backupReview.Text | Should -Be 'Choose a backup first.'
        $script:backupArchive = 'fixture.zip'
        $restoreSettings.Checked = $false
        Update-BackupReview
        $restoreBackup.Enabled | Should -BeTrue
        $backupReview.Text | Should -Not -Match 'Proposed program'
    }
    It 'refuses a restore event without a reviewed plan' {
        $script:restorePlan = $null
        $script:reportedProblem = ''
        # Invoke the subscribed click handler without displaying a modal form.
        $click = [Windows.Forms.Button].GetMethod('OnClick', [Reflection.BindingFlags]'Instance,NonPublic')
        $null = $click.Invoke($restoreBackup, @([EventArgs]::Empty))
        $script:reportedProblem | Should -Be 'Review the backup first.'
        $restoreBackup.Enabled | Should -BeFalse
        Should -Invoke New-SetupRestorePlan -Times 0
    }
}
