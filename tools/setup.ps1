param(
    [string]$LauncherDirectory = (Split-Path $PSScriptRoot -Parent),
    [switch]$ReportOnly,
    [string]$PreviewDirectory = '',
    [switch]$SmokeTest
)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'SetupCore.psm1') -Force -DisableNameChecking
$LauncherDirectory = [IO.Path]::GetFullPath($LauncherDirectory)
$script:configProblem = ''
try {
    $script:config = Read-SetupConfig $LauncherDirectory
}
catch {
    $script:configProblem = $_.Exception.Message
    $script:config = [pscustomobject]@{
        vpinballExecutable = ''
        tablesDirectory = ''
        searchSubdirectories = $true
    }
}
if ($SmokeTest -and [IO.Path]::GetFileName((Get-SetupValue $script:config 'vpinballExecutable')) -cne 'VPinballX_SETUP_TEST.exe') {
    throw 'UI smoke tests require the dedicated VPinballX_SETUP_TEST.exe fixture.'
}
if ($ReportOnly) {
    if ($script:configProblem) {
        throw $script:configProblem
    }
    Get-SetupHealth $LauncherDirectory $script:config | ConvertTo-Json -Depth 8
    return
}
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
[Windows.Forms.Application]::EnableVisualStyles()
$form = [Windows.Forms.Form]::new()
$form.Text = 'VR Pinball Launcher - Setup'
$form.Size = [Drawing.Size]::new(1140, 790)
$form.MinimumSize = [Drawing.Size]::new(960, 680)
$form.StartPosition = 'CenterScreen'
$form.Font = [Drawing.Font]::new('Segoe UI', 10)
$form.BackColor = [Drawing.Color]::FromArgb(22, 28, 38)
$form.ForeColor = [Drawing.Color]::FromArgb(235, 240, 245)
$form.AutoScaleMode = 'Dpi'
function New-Label($Parent, $Text, $X, $Y, $Width, $Height = 30) {
    $c = [Windows.Forms.Label]::new()
    $c.Text = $Text
    $c.Location = [Drawing.Point]::new($X, $Y)
    $c.Size = [Drawing.Size]::new($Width, $Height)
    $Parent.Controls.Add($c)
    return $c
}
function New-Button($Parent, $Text, $X, $Y, $Width = 125) {
    $c = [Windows.Forms.Button]::new()
    $c.Text = $Text
    $c.Location = [Drawing.Point]::new($X, $Y)
    $c.Size = [Drawing.Size]::new($Width, 34)
    $c.FlatStyle = 'Flat'
    $c.BackColor = [Drawing.Color]::FromArgb(40, 58, 74)
    $c.ForeColor = $form.ForeColor
    $Parent.Controls.Add($c)
    return $c
}
function New-Box($Parent, $X, $Y, $Width) {
    $c = [Windows.Forms.TextBox]::new()
    $c.Location = [Drawing.Point]::new($X, $Y)
    $c.Size = [Drawing.Size]::new($Width, 28)
    $c.BackColor = [Drawing.Color]::FromArgb(32, 40, 52)
    $c.ForeColor = $form.ForeColor
    $Parent.Controls.Add($c)
    return $c
}
function Show-Problem($Problem) {
    [Windows.Forms.MessageBox]::Show($form, [string]$Problem, 'Setup needs attention', 'OK', 'Warning') | Out-Null
}
$header = [Windows.Forms.Panel]::new()
$header.Dock = 'Top'
$header.Height = 100
$form.Controls.Add($header)
$heading = New-Label $header 'Connect your existing pinball setup' 24 14 1000 34
$heading.Font = [Drawing.Font]::new('Segoe UI', 19, [Drawing.FontStyle]::Bold)
$null = New-Label $header 'Review locations, check components, test tables, and manage launcher backups.' 26 56 1000 30
$footer = [Windows.Forms.Panel]::new()
$footer.Dock = 'Bottom'
$footer.Height = 62
$form.Controls.Add($footer)
$back = New-Button $footer 'Back' 22 12 95
$next = New-Button $footer 'Next' 128 12 95
$status = New-Label $footer 'Your current table overrides and play history are preserved.' 245 16 840 34
$status.Anchor = 'Left,Right,Top'
$tabs = [Windows.Forms.TabControl]::new()
$tabs.Dock = 'Fill'
$form.Controls.Add($tabs)
$tabs.BringToFront()
$pages = @()
foreach ($title in @('1  Locations', '2  Health', '3  Test a table', '4  Review and apply', '5  Backup and restore')) {
    $p = [Windows.Forms.TabPage]::new($title)
    $p.BackColor = $form.BackColor
    $p.ForeColor = $form.ForeColor
    $p.AutoScroll = $true
    $tabs.TabPages.Add($p)
    $pages += , $p
}
$null = New-Label $pages[0] 'Actual VPX executable' 24 22 900
$vpxBox = New-Box $pages[0] 24 56 880
$vpxBox.Anchor = 'Left,Right,Top'
$vpxBrowse = New-Button $pages[0] 'Browse...' 925 52 130
$vpxBrowse.Anchor = 'Right,Top'
$null = New-Label $pages[0] 'Choose VPinballX_BGFX64.exe, VPinballX_GL64.exe, or another VPX build. Helpers are listed separately below.' 24 91 1030 45
$null = New-Label $pages[0] 'Tables folder' 24 145 900
$tablesBox = New-Box $pages[0] 24 179 880
$tablesBox.Anchor = 'Left,Right,Top'
$tablesBrowse = New-Button $pages[0] 'Browse...' 925 175 130
$tablesBrowse.Anchor = 'Right,Top'
$recursive = [Windows.Forms.CheckBox]::new()
$recursive.Text = 'Include tables in subfolders'
$recursive.Location = [Drawing.Point]::new(24, 216)
$recursive.Size = [Drawing.Size]::new(900, 30)
$pages[0].Controls.Add($recursive)
$null = New-Label $pages[0] 'How to launch tables' 24 270 900
$mode = [Windows.Forms.ComboBox]::new()
$mode.DropDownStyle = 'DropDownList'
$mode.Location = [Drawing.Point]::new(24, 304)
$mode.Width = 480
$pages[0].Controls.Add($mode)
$mode.Items.AddRange([object[]]@('Keep current launch program', 'Launch VPX directly', 'Use background-input helper'))
$runnerLabel = New-Label $pages[0] '' 24 350 1030 80
$null = New-Label $pages[0] 'Keeping the current program preserves your helper. Choose the helper option only when you want to install or replace it. Special per-table profiles stay unchanged.' 24 440 1030 65
$configLabel = New-Label $pages[0] ('Configuration: ' + (Join-Path $LauncherDirectory 'launcher-config.json')) 24 515 1030 45
$configLabel.ForeColor = [Drawing.Color]::FromArgb(150, 175, 195)
$vpxBox.Text = Find-SetupVpx $LauncherDirectory $script:config
$tablesBox.Text = Resolve-SetupPath (Get-SetupValue $script:config 'tablesDirectory') $LauncherDirectory
if (!$tablesBox.Text -and $vpxBox.Text) {
    $guess = Join-Path ([IO.Path]::GetDirectoryName($vpxBox.Text)) 'Tables'
    if (Test-Path -LiteralPath $guess) {
        $tablesBox.Text = $guess
    }
}
$recursive.Checked = [bool](Get-SetupValue $script:config 'searchSubdirectories' $true)
$existingRunner = Resolve-SetupPath (Get-SetupValue $script:config 'vpinballExecutable') $LauncherDirectory
$mode.SelectedIndex = if ($existingRunner -and (Test-Path -LiteralPath $existingRunner)) {
    0
}
else {
    1
}
$runnerLabel.Text = 'Current launch program: ' + $existingRunner + "`r`nProfiles, media locations, controller settings, and unknown JSON options will be preserved."
$vpxBrowse.Add_Click({ $d = [Windows.Forms.OpenFileDialog]::new(); $d.Filter = 'VPX executables|VPinballX*.exe|Executables|*.exe'; if ($d.ShowDialog($form) -eq 'OK') {
            $vpxBox.Text = $d.FileName
        }; $d.Dispose() })
$tablesBrowse.Add_Click({ $d = [Windows.Forms.FolderBrowserDialog]::new(); $d.ShowNewFolderButton = $false; if (Test-Path -LiteralPath $tablesBox.Text) {
            $d.SelectedPath = $tablesBox.Text
        }; if ($d.ShowDialog($form) -eq 'OK') {
            $tablesBox.Text = $d.SelectedPath
        }; $d.Dispose() })

$healthLegend = New-Label $pages[1] 'Found means detected, not proven working. Optional add-ons may be absent. Table overrides take precedence.' 20 15 1040 42
$refresh = New-Button $pages[1] 'Refresh checks' 20 62 145
$report = New-Button $pages[1] 'Save report...' 177 62 145
$openConfig = New-Button $pages[1] 'Open configuration' 334 62 185
$openConfig.Enabled = Test-Path -LiteralPath (Join-Path $LauncherDirectory 'launcher-config.json') -PathType Leaf
$openConfig.Add_Click({
        try {
            $editor = [Diagnostics.ProcessStartInfo]::new(); $editor.FileName = Join-Path $env:WINDIR 'System32\notepad.exe'; $editor.Arguments = Quote-SetupArgument (Join-Path $LauncherDirectory 'launcher-config.json'); $editor.UseShellExecute = $false; $null = [Diagnostics.Process]::Start($editor)
        }
        catch {
            Show-Problem $_.Exception.Message
        }
    })
$grid = [Windows.Forms.DataGridView]::new()
$grid.Location = [Drawing.Point]::new(20, 110)
$grid.Size = [Drawing.Size]::new(1040, 420)
$grid.Anchor = 'Top,Bottom,Left,Right'
$grid.ReadOnly = $true
$grid.AllowUserToAddRows = $false
$grid.AllowUserToDeleteRows = $false
$grid.RowHeadersVisible = $false
$grid.AutoSizeRowsMode = 'AllCells'
$grid.BackgroundColor = $form.BackColor
$grid.AutoGenerateColumns = $false
$grid.DefaultCellStyle.BackColor = [Drawing.Color]::FromArgb(30, 39, 51)
$grid.DefaultCellStyle.ForeColor = $form.ForeColor
$grid.DefaultCellStyle.WrapMode = 'True'
$grid.DefaultCellStyle.SelectionBackColor = [Drawing.Color]::FromArgb(45, 78, 98)
foreach ($name in @('Component', 'Status', 'Details', 'Path')) {
    $col = [Windows.Forms.DataGridViewTextBoxColumn]::new()
    $col.Name = $name
    $col.HeaderText = $name
    $col.AutoSizeMode = 'Fill'
    $col.FillWeight = @{
        Component = 17
        Status = 12
        Details = 43
        Path = 28
    }[$name]
    $null = $grid.Columns.Add($col)
}
$pages[1].Controls.Add($grid)
$script:health = @()
$script:testResult = $null
$script:testProcess = $null
$script:testLogOffset = 0
$script:testLogPending = ''
$script:testMatched = $false
function Refresh-Health {
    try {
        $assessed = $script:config
        try {
            $assessed = (Get-ProposedPlan).Config
        }
        catch {
        }
        $script:health = @(Get-SetupHealth $LauncherDirectory $assessed $vpxBox.Text $tablesBox.Text)
        $grid.Rows.Clear()
        foreach ($row in $script:health) {
            $i = $grid.Rows.Add($row.Component, $row.Status, $row.Details, $row.Path)
            $grid.Rows[$i].Cells['Status'].Style.ForeColor = if ($row.Status -eq 'Found') {
                [Drawing.Color]::LightGreen
            }
            elseif ($row.Status -eq 'Missing') {
                [Drawing.Color]::Salmon
            }
            else {
                [Drawing.Color]::Khaki
            }
        }
        $status.Text = 'Checks refreshed. Use a table test to confirm controls, audio, and displays.'
    }
    catch {
        Show-Problem $_.Exception.Message
    }
}
$refresh.Add_Click({ Refresh-Health })
$null = New-Label $pages[2] 'Choose a table to test' 24 20 900
$testBox = New-Box $pages[2] 24 55 880
$testBox.Anchor = 'Top,Left,Right'
$tableBrowse = New-Button $pages[2] 'Browse...' 925 51 130
$tableBrowse.Anchor = 'Top,Right'
$tableBrowse.Add_Click({ $d = [Windows.Forms.OpenFileDialog]::new(); $d.Filter = 'Visual Pinball tables|*.vpx'; if (Test-Path -LiteralPath $tablesBox.Text) {
            $d.InitialDirectory = $tablesBox.Text
        }; if ($d.ShowDialog($form) -eq 'OK') {
            $testBox.Text = $d.FileName
        }; $d.Dispose() })
$null = New-Label $pages[2] 'Close the VR launcher first so VPX can use the headset. Tests use proposed locations; saving setup is separate. A table test can update that table''s usual credits and save data.' 24 98 1030 55
$test = New-Button $pages[2] 'Start table test' 24 166 160
$stop = New-Button $pages[2] 'Close test table' 195 166 160
$stop.Enabled = $false
$testOutput = [Windows.Forms.TextBox]::new()
$testOutput.Multiline = $true
$testOutput.ReadOnly = $true
$testOutput.ScrollBars = 'Vertical'
$testOutput.Location = [Drawing.Point]::new(24, 216)
$testOutput.Size = [Drawing.Size]::new(1030, 150)
$testOutput.Anchor = 'Top,Left,Right'
$testOutput.BackColor = [Drawing.Color]::FromArgb(30, 39, 51)
$testOutput.ForeColor = $form.ForeColor
$pages[2].Controls.Add($testOutput)
$testOutput.Text = 'No test run yet. A successful startup log does not prove a healthy game. Confirm the items below during play.'
$checks = @()
$index = 0
foreach ($label in @('Controls respond correctly', 'Audio level is comfortable', 'Scoring and DMD artwork are correct', 'VR view is correct (if playing in VR)')) {
    $c = [Windows.Forms.CheckBox]::new()
    $c.Text = $label
    $c.Location = [Drawing.Point]::new(24, 385 + 36 * $index)
    $c.Size = [Drawing.Size]::new(1000, 30)
    $pages[2].Controls.Add($c)
    $checks += , $c
    $index++
}

$null = New-Label $pages[3] 'Review the exact changes before applying' 24 20 1000
$vrOption = [Windows.Forms.CheckBox]::new()
$vrOption.Text = 'Enable always-on VR in the active INI (requires a matching runtime log)'
$vrOption.Location = [Drawing.Point]::new(24, 62)
$vrOption.Size = [Drawing.Size]::new(1000, 32)
$pages[3].Controls.Add($vrOption)
$review = [Windows.Forms.TextBox]::new()
$review.Multiline = $true
$review.ReadOnly = $true
$review.ScrollBars = 'Vertical'
$review.Location = [Drawing.Point]::new(24, 112)
$review.Size = [Drawing.Size]::new(1030, 290)
$review.Anchor = 'Top,Left,Right'
$review.BackColor = [Drawing.Color]::FromArgb(30, 39, 51)
$review.ForeColor = $form.ForeColor
$pages[3].Controls.Add($review)
$apply = New-Button $pages[3] 'Apply reviewed changes' 24 427 235
$reviewRefresh = New-Button $pages[3] 'Update review' 272 427 160
$null = New-Label $pages[3] 'Applying requires the launcher and VPX to be closed. Original configuration is backed up automatically. No table scripts, ROMs, play history, per-table overrides, or global input mappings are rewritten.' 24 483 1030 70
function Get-ProposedPlan {
    if ($script:configProblem) {
        throw ('Cannot overwrite an unreadable configuration: ' + $script:configProblem)
    }
    $launchMode = @('Keep', 'Direct', 'Helper')[$mode.SelectedIndex]
    $plan = Get-SetupPlan $LauncherDirectory $script:config $vpxBox.Text $tablesBox.Text $launchMode $vrOption.Checked
    if ([bool](Get-SetupValue $script:config 'searchSubdirectories' $true) -ne $recursive.Checked) {
        $plan.Changes += ('searchSubdirectories -> ' + $recursive.Checked)
    }
    $plan.Config | Add-Member -NotePropertyName searchSubdirectories -NotePropertyValue $recursive.Checked -Force
    return $plan
}
function Update-Review {
    try {
        $p = Get-ProposedPlan
        $review.Text = if (@($p.Changes).Count) {
            ($p.Changes -join "`r`n`r`n") + "`r`n`r`nBackup: " + (Join-Path $LauncherDirectory 'SetupBackups') + "`r`nRestart the launcher after applying."
        }
        else {
            'No changes requested.'
        }
        $apply.Enabled = @($p.Changes).Count -gt 0
    }
    catch {
        $review.Text = $_.Exception.Message
        $apply.Enabled = $false
    }
}
$reviewRefresh.Add_Click({ Update-Review })
$vrOption.Add_CheckedChanged({ Update-Review })
$apply.Add_Click({
        try {
            $plan = Get-ProposedPlan
            $review.Text = $plan.Changes -join "`r`n`r`n"
            if ([Windows.Forms.MessageBox]::Show($form, ($review.Text + "`r`n`r`nApply these changes with backups?"), 'Apply setup changes', 'YesNo', 'Question') -ne 'Yes') {
                return
            }
            $apply.Enabled = $false; $status.Text = 'Applying the reviewed changes and writing backups...'; $form.Refresh()
            $backup = Save-SetupPlan $plan; $script:config = Read-SetupConfig $LauncherDirectory
            $mode.SelectedIndex = 0; $vrOption.Checked = $false
            $runnerLabel.Text = 'Current launch program: ' + (Get-SetupValue $script:config 'vpinballExecutable')
            Refresh-Health; Update-Review; $status.Text = 'Saved. Restart the launcher. Backup: ' + $backup
            [Windows.Forms.MessageBox]::Show($form, "Saved. Restart the launcher to use these settings.`r`nBackup: $backup", 'Setup saved', 'OK', 'Information') | Out-Null
        }
        catch {
            Show-Problem $_.Exception.Message; Update-Review
        }
    })
$report.Add_Click({
        $d = [Windows.Forms.SaveFileDialog]::new(); $d.Filter = 'Setup report (JSON)|*.json'; $d.FileName = 'pinball-setup-report.json'
        if ($d.ShowDialog($form) -eq 'OK') {
            try {
                $manual = @{
                }; foreach ($c in $checks) {
                    $manual[$c.Text] = $c.Checked
                }
                $data = [ordered]@{
                    GeneratedUtc = [DateTime]::UtcNow.ToString('o'); LauncherDirectory = $LauncherDirectory; Health = $script:health; TableTest = $script:testResult; ManualChecks = $manual
                }
                [IO.File]::WriteAllText($d.FileName, ($data | ConvertTo-Json -Depth 12), [Text.UTF8Encoding]::new($false)); $status.Text = 'Report saved: ' + $d.FileName
            }
            catch {
                Show-Problem $_.Exception.Message
            }
        }; $d.Dispose()
    })
$script:logPath = Join-Path $env:APPDATA 'VPinballX\10.8\vpinball.log'
$testTimer = [Windows.Forms.Timer]::new()
$testTimer.Interval = 500
$test.Add_Click({
        try {
            if (Get-Process -Name 'vr-launch', 'VPinballX_BGFX64', 'VPinballX_GL64', 'VPinballX64' -ErrorAction SilentlyContinue) {
                throw 'Close the launcher and any running VPX table before starting this test.'
            }
            $plan = Get-ProposedPlan
            if ($plan.InstallHelper) {
                throw 'Apply the helper installation first, then start the table test.'
            }
            $command = Get-SetupTestCommand $plan.Config $testBox.Text $LauncherDirectory
            $script:testLogOffset = if (Test-Path -LiteralPath $script:logPath) {
                (Get-Item -LiteralPath $script:logPath).Length
            }
            else {
                0
            }
            $script:testLogPending = ''; $script:testMatched = $false
            $start = [Diagnostics.ProcessStartInfo]::new(); $start.FileName = $command.Executable; $start.Arguments = $command.Arguments; $start.WorkingDirectory = $command.WorkingDirectory; $start.UseShellExecute = $false
            $script:testProcess = [Diagnostics.Process]::Start($start)
            $script:testResult = [pscustomobject]@{
                Table = $command.Table; Profile = $command.Profile; StartedUtc = [DateTime]::UtcNow.ToString('o'); StartupConfirmed = $false; ScriptOrRomProblem = ''; ExitCode = $null
            }
            foreach ($c in $checks) {
                $c.Checked = $false
            }
            $test.Enabled = $false; $stop.Enabled = $true; $testTimer.Start(); $testOutput.Text = 'Starting selected table using profile: ' + $command.Profile
        }
        catch {
            Show-Problem $_.Exception.Message
        }
    })
$stop.Add_Click({
        try {
            if (!$script:testProcess -or $script:testProcess.HasExited) {
                return
            }
            # Close only this test process or its descendants, never another VPX session.
            $ids = [Collections.Generic.List[int]]::new(); $ids.Add($script:testProcess.Id)
            $processes = @(Get-CimInstance Win32_Process)
            for ($i = 0; $i -lt $ids.Count; $i++) {
                foreach ($p in $processes) {
                    if ($p.ParentProcessId -eq $ids[$i] -and !$ids.Contains([int]$p.ProcessId)) {
                        $ids.Add([int]$p.ProcessId)
                    }
                }
            }
            for ($i = $ids.Count - 1; $i -ge 0; $i--) {
                $p = Get-Process -Id $ids[$i] -ErrorAction SilentlyContinue; if ($p -and $p.StartTime -ge $script:testProcess.StartTime) {
                    $null = $p.CloseMainWindow()
                }
            }
            $testOutput.AppendText("`r`nClose requested. If VPX has a dialog open, close it normally. No process is forcibly killed.")
        }
        catch {
            Show-Problem $_.Exception.Message
        }
    })
$testTimer.Add_Tick({
        try {
            if (!$script:testProcess) {
                return
            }
            if (Test-Path -LiteralPath $script:logPath) {
                $stream = [IO.File]::Open($script:logPath, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
                try {
                    if ($stream.Length -lt $script:testLogOffset) {
                        $script:testLogOffset = 0; $script:testLogPending = ''
                    }
                    $stream.Position = $script:testLogOffset; $bytes = New-Object byte[] ([int][Math]::Min(65536, $stream.Length - $script:testLogOffset)); $count = $stream.Read($bytes, 0, $bytes.Length); $script:testLogOffset += $count
                    $lines = ($script:testLogPending + [Text.Encoding]::UTF8.GetString($bytes, 0, $count)).Split([char]10)
                    for ($i = 0; $i -lt $lines.Length - 1; $i++) {
                        $line = $lines[$i]
                        if ($line.Contains('LoadGameFromFilename')) {
                            $script:testMatched = $line.IndexOf($script:testResult.Table, [StringComparison]::OrdinalIgnoreCase) -ge 0
                        }
                        if (!$script:testMatched) {
                            continue
                        }
                        if ($line.Contains('Startup done')) {
                            $script:testResult.StartupConfirmed = $true
                        }
                        if ($line.Contains('OnScriptError')) {
                            $script:testResult.ScriptOrRomProblem = 'The table reported a script error; inspect the VPX log.'
                        }
                    }
                    $script:testLogPending = $lines[-1]
                }
                finally {
                    $stream.Dispose()
                }
            }
            $seconds = [int]([DateTime]::UtcNow - [DateTime]::Parse($script:testResult.StartedUtc).ToUniversalTime()).TotalSeconds
            $testOutput.Text = if ($script:testResult.StartupConfirmed) {
                'VPX initialization confirmed. Check controls, audio, scoring and DMD appearance during play.'
            }
            else {
                "Waiting for VPX startup confirmation ($seconds seconds). A different log location may not be detected."
            }
            if ($script:testResult.ScriptOrRomProblem) {
                $testOutput.AppendText("`r`n" + $script:testResult.ScriptOrRomProblem)
            }
            if ($script:testProcess.HasExited) {
                $script:testResult.ExitCode = $script:testProcess.ExitCode; $testTimer.Stop(); $test.Enabled = $true; $stop.Enabled = $false
                $testOutput.AppendText("`r`nTable closed with code " + $script:testResult.ExitCode + '. Use the checkboxes to record what you verified, then Save report on the Health page.')
                $script:testProcess.Dispose(); $script:testProcess = $null
            }
        }
        catch {
            $testOutput.Text = 'Could not read the test log: ' + $_.Exception.Message
        }
    })
function Resize-SetupFields {
    foreach ($pair in @(@($vpxBox, $vpxBrowse, $pages[0]), @($tablesBox, $tablesBrowse, $pages[0]), @($testBox, $tableBrowse, $pages[2]))) {
        $pair[0].Width = [Math]::Max(350, $pair[2].ClientSize.Width - 200)
        $pair[1].Left = $pair[2].ClientSize.Width - 155
    }
    $grid.Width = [Math]::Max(600, $pages[1].ClientSize.Width - 40)
    $grid.Height = [Math]::Max(240, $pages[1].ClientSize.Height - 130)
    $testOutput.Width = [Math]::Max(600, $pages[2].ClientSize.Width - 48)
    $review.Width = [Math]::Max(600, $pages[3].ClientSize.Width - 48)
    if ($backupReview) {
        $backupReview.Width = [Math]::Max(600, $pages[4].ClientSize.Width - 48)
    }
}
$form.Add_Resize({ Resize-SetupFields })
$tabs.Add_SelectedIndexChanged({ Resize-SetupFields; $back.Enabled = $tabs.SelectedIndex -gt 0; $next.Enabled = $tabs.SelectedIndex -lt ($tabs.TabPages.Count - 1); if ($tabs.SelectedIndex -eq 1) {
            Refresh-Health
        }; if ($tabs.SelectedIndex -eq 3) {
            Update-Review
        } })
$back.Add_Click({ if ($tabs.SelectedIndex -gt 0) {
            $tabs.SelectedIndex--
        } })
$next.Add_Click({ if ($tabs.SelectedIndex -lt ($tabs.TabPages.Count - 1)) {
            $tabs.SelectedIndex++
        } })

. (Join-Path $PSScriptRoot "SetupBackupPage.ps1")
$form.Add_FormClosed({ $testTimer.Stop(); $testTimer.Dispose(); if ($script:testProcess) {
            $script:testProcess.Dispose()
        } })
$script:smokeFailure = ''
if ($SmokeTest) {
    $script:smokeDeadline = [DateTime]::UtcNow.AddSeconds(20)
    $script:smokeTimer = [Windows.Forms.Timer]::new()
    $script:smokeTimer.Interval = 100
    $script:smokeTimer.Add_Tick({
            if ($script:testResult -and $null -ne $script:testResult.ExitCode) {
                if ($script:testResult.ExitCode -ne 42 -or $script:testResult.StartupConfirmed -or !$script:testResult.ScriptOrRomProblem) {
                    $script:smokeFailure = 'Unexpected UI table-test result'
                }
                $script:smokeTimer.Stop(); $form.Close()
            }
            elseif ([DateTime]::UtcNow -gt $script:smokeDeadline) {
                $script:smokeFailure = 'UI smoke test timed out'; $script:smokeTimer.Stop(); $form.Close()
            }
        })
    $form.Add_FormClosed({ $script:smokeTimer.Dispose() })
}
$form.Add_Shown({
        Resize-SetupFields
        Refresh-Health
        if ($script:configProblem) {
            Show-Problem ('Configuration could not be read. Health checks remain read-only; applying is disabled. ' + $script:configProblem)
        }
        if ($PreviewDirectory) {
            try {
                New-Item -ItemType Directory -Force $PreviewDirectory | Out-Null
                for ($i = 0; $i -lt $tabs.TabPages.Count; $i++) {
                    $tabs.SelectedIndex = $i; $form.Refresh(); [Windows.Forms.Application]::DoEvents(); $bitmap = [Drawing.Bitmap]::new($form.Width, $form.Height); $form.DrawToBitmap($bitmap, [Drawing.Rectangle]::new(0, 0, $form.Width, $form.Height)); $bitmap.Save((Join-Path $PreviewDirectory ('setup-' + $i + '.png'))); $bitmap.Dispose()
                }
            }
            finally {
                $form.Close()
            }
        }
        if ($SmokeTest) {
            $tabs.SelectedIndex = 2; $testBox.Text = Join-Path $tablesBox.Text 'Setup UI Test.vpx'; $test.PerformClick(); $script:smokeTimer.Start()
        }
    })
$back.Enabled = $false
[Windows.Forms.Application]::Run($form)
$form.Dispose()
if ($SmokeTest) {
    if ($script:smokeFailure) {
        throw $script:smokeFailure
    }
    Write-Output 'PASS: setup UI health refresh and selected-table test returned exit42 without claiming startup success.'
}
