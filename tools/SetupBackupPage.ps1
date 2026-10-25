# Desktop backup page. Dot-sourced by setup.ps1 after the common controls.
$null = New-Label $pages[4] 'Take your launcher settings and library history with you' 24 20 1000 36
$null = New-Label $pages[4] 'Backups contain configuration (including profiles and controller launch settings), favorites, play counts, history and table launch results. Tables, ROMs, artwork, helper programs, VPX settings and external controller scripts are not included. Close the launcher and VPX first.' 24 62 1030 82
$exportBackup = New-Button $pages[4] 'Export backup...' 24 152 190
$chooseBackup = New-Button $pages[4] 'Choose backup...' 228 152 190
$restoreSettings = [Windows.Forms.CheckBox]::new()
$restoreSettings.Text = 'Restore launcher settings'
$restoreSettings.Checked = $true
$restoreSettings.Location = [Drawing.Point]::new(24, 202)
$restoreSettings.Size = [Drawing.Size]::new(470, 30)
$pages[4].Controls.Add($restoreSettings)
$restoreHistory = [Windows.Forms.CheckBox]::new()
$restoreHistory.Text = 'Replace favorites and player history'
$restoreHistory.Checked = $true
$restoreHistory.Location = [Drawing.Point]::new(510, 202)
$restoreHistory.Size = [Drawing.Size]::new(520, 30)
$pages[4].Controls.Add($restoreHistory)
$keepPaths = [Windows.Forms.CheckBox]::new()
$keepPaths.Text = 'Keep this PC''s paths, controller launch settings, profiles and table assignments'
$keepPaths.Checked = $true
$keepPaths.Location = [Drawing.Point]::new(24, 240)
$keepPaths.Size = [Drawing.Size]::new(1030, 30)
$pages[4].Controls.Add($keepPaths)
$backupReview = [Windows.Forms.TextBox]::new()
$backupReview.Multiline = $true
$backupReview.ReadOnly = $true
$backupReview.ScrollBars = 'Vertical'
$backupReview.Location = [Drawing.Point]::new(24, 282)
$backupReview.Size = [Drawing.Size]::new(1030, 190)
$backupReview.Anchor = 'Top,Left,Right'
$backupReview.BackColor = $review.BackColor
$backupReview.ForeColor = $form.ForeColor
$pages[4].Controls.Add($backupReview)
$backupReview.Text = 'Choose a backup to inspect it before restoring. Export reads saved settings; unsaved changes on other pages are not included.'
$reviewBackup = New-Button $pages[4] 'Review restore' 24 490 180
$restoreBackup = New-Button $pages[4] 'Restore reviewed backup' 218 490 250
$restoreBackup.Enabled = $false
$script:backupArchive = ''
$script:restorePlan = $null
function Update-BackupReview {
    $restoreBackup.Enabled = $false
    $script:restorePlan = $null
    try {
        if (!$script:backupArchive) {
            throw 'Choose a backup first.'
        }
        $script:restorePlan = New-SetupRestorePlan $LauncherDirectory $script:backupArchive $restoreSettings.Checked $restoreHistory.Checked $keepPaths.Checked
        $backupReview.Text = 'Backup: ' + $script:backupArchive + "`r`nCreated: " + $script:restorePlan.CreatedUtc + "`r`n`r`n" + ($script:restorePlan.Changes -join "`r`n`r`n")
        if ($restoreSettings.Checked) {
            $proposed = [Text.Encoding]::UTF8.GetString($script:restorePlan.Files[(Join-Path $LauncherDirectory 'launcher-config.json')]) | ConvertFrom-Json
            $backupReview.AppendText("`r`n`r`nProposed program: " + (Get-SetupValue $proposed 'vpinballExecutable') + "`r`nTables: " + (Get-SetupValue $proposed 'tablesDirectory') + "`r`nProfiles: " + (@(Get-SetupValue $proposed 'vpxProfiles' @()).Count))
        }
        $restoreBackup.Enabled = $true
    }
    catch {
        $backupReview.Text = $_.Exception.Message
    }
}
$chooseBackup.Add_Click({ $d = [Windows.Forms.OpenFileDialog]::new(); $d.Filter = 'Launcher backup (ZIP)|*.zip'; if ($d.ShowDialog($form) -eq 'OK') {
            $script:backupArchive = $d.FileName; Update-BackupReview
        }; $d.Dispose() })
$reviewBackup.Add_Click({ Update-BackupReview })
foreach ($option in @($restoreSettings, $restoreHistory, $keepPaths)) {
    $option.Add_CheckedChanged({ $script:restorePlan = $null; $restoreBackup.Enabled = $false; $backupReview.Text = 'Options changed. Click Review restore before restoring.' })
}
$exportBackup.Add_Click({
        $d = [Windows.Forms.SaveFileDialog]::new(); $d.Filter = 'Launcher backup (ZIP)|*.zip'; $d.FileName = 'VRPinballLauncher-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.zip'
        try {
            if ($d.ShowDialog($form) -eq 'OK') {
                $saved = Export-SetupBackup $LauncherDirectory $d.FileName; $backupReview.Text = 'Backup exported: ' + $saved; $status.Text = 'Backup exported. Existing settings unchanged.'
            }
        }
        catch {
            Show-Problem $_.Exception.Message
        }
        finally {
            $d.Dispose()
        }
    })
$restoreBackup.Add_Click({
        try {
            if (!$script:restorePlan) {
                throw 'Review the backup first.'
            }
            if ([Windows.Forms.MessageBox]::Show($form, ($backupReview.Text + "`r`n`r`nReplace the selected settings now? Current files will be backed up first."), 'Restore launcher backup', 'YesNo', 'Question') -ne 'Yes') {
                return
            }
            $saved = Save-SetupRestorePlan $script:restorePlan
            [Windows.Forms.MessageBox]::Show($form, ('Restored. Setup will close; reopen it to review locations before launching. Previous files: ' + $saved), 'Backup restored', 'OK', 'Information') | Out-Null
            $form.Close()
        }
        catch {
            Show-Problem $_.Exception.Message; $script:restorePlan = $null; $restoreBackup.Enabled = $false
        }
    })
