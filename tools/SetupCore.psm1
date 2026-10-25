Set-StrictMode -Version 2
. (Join-Path $PSScriptRoot "SetupPaths.ps1")
. (Join-Path $PSScriptRoot "SetupBackup.ps1")

function Read-SetupConfig([string]$LauncherDirectory) {
    $path = Join-Path $LauncherDirectory 'launcher-config.json'
    if (!(Test-Path -LiteralPath $path -PathType Leaf)) {
        return [pscustomobject]@{
            vpinballExecutable = ''
            tablesDirectory = ''
            searchSubdirectories = $true
        }
    }
    $config = [IO.File]::ReadAllText($path) | ConvertFrom-Json -ErrorAction Stop
    if (!$config -or $config -is [array] -or $config -is [string] -or $config -is [ValueType]) {
        throw 'Launcher configuration must be a JSON object. The original file was not changed.'
    }
    return $config
}

function Get-SetupValue($Object, [string]$Name, $Default = '') {
    if ($null -ne $Object -and $Object.PSObject.Properties[$Name]) {
        return $Object.$Name
    }
    return $Default
}

function Find-SetupVpx([string]$LauncherDirectory, $Config) {
    $runner = Resolve-SetupPath (Get-SetupValue $Config 'vpinballExecutable') $LauncherDirectory
    $resolved = Get-SetupVpxTarget $runner (Get-SetupValue $Config 'realVpinballExecutable')
    if ($resolved.Target -and (Test-Path -LiteralPath $resolved.Target -PathType Leaf)) {
        return $resolved.Target
    }
    $roots = @('C:\Visual Pinball', 'D:\Visual Pinball', 'C:\Games\Visual Pinball', "$env:ProgramFiles\Visual Pinball", "${env:ProgramFiles(x86)}\Visual Pinball")
    foreach ($root in $roots) {
        foreach ($name in @('VPinballX_BGFX64.exe', 'VPinballX_GL64.exe', 'VPinballX64.exe')) {
            $candidate = Join-Path $root $name
            if (Test-Path -LiteralPath $candidate -PathType Leaf) {
                return $candidate
            }
        }
    }
    return ''
}

function Read-SetupIni([string]$Path) {
    $values = @{
    }
    $section = ''
    if ($Path -and (Test-Path -LiteralPath $Path -PathType Leaf)) {
        foreach ($line in [IO.File]::ReadAllLines($Path)) {
            if ($line -match '^\s*\[([^]]+)\]') {
                $section = $matches[1]
            }
            elseif ($line -match '^\s*([^;#=][^=]*?)\s*=\s*(.*?)\s*$') {
                $values["$section/$($matches[1].Trim())"] = $matches[2]
            }
        }
    }
    return $values
}

function Get-SetupIniLocation([string]$VpxExecutable, [string]$AppDataDirectory = $env:APPDATA) {
    $root = if ($VpxExecutable) {
        [IO.Path]::GetDirectoryName($VpxExecutable)
    }
    else {
        ''
    }
    $candidates = @()
    if ($root) {
        $candidates += Join-Path $root 'VPinballX.ini'
    }
    $candidates += Join-Path $AppDataDirectory 'VPinballX\10.8\VPinballX.ini'
    $candidates += Join-Path $AppDataDirectory 'VPinballX\VPinballX.ini'
    # A runtime log is evidence only when its application path matches this VPX install.
    foreach ($log in @((Join-Path $AppDataDirectory 'VPinballX\10.8\vpinball.log'), (Join-Path $AppDataDirectory 'VPinballX\vpinball.log'))) {
        if (!(Test-Path -LiteralPath $log -PathType Leaf)) {
            continue
        }
        $lines = @(Get-Content -LiteralPath $log -Tail 2500)
        $start = -1
        for ($i = $lines.Count - 1; $i -ge 0; $i--) {
            if ($lines[$i] -match 'Starting VPX -') {
                $start = $i
                break
            }
        }
        if ($start -lt 0) {
            continue
        }
        $section = $lines[$start..($lines.Count - 1)] -join "`n"
        $app = [regex]::Match($section, 'Application path:\s*([^\r\n]+)')
        $ini = [regex]::Match($section, 'Settings file was loaded from\s+([^\r\n]+)')
        if ($app.Success -and $ini.Success -and $root -and $app.Groups[1].Value.Trim().TrimEnd('\') -ieq $root.TrimEnd('\')) {
            $path = $ini.Groups[1].Value.Trim().Trim("'")
            if (Test-Path -LiteralPath $path -PathType Leaf) {
                return [pscustomobject]@{
                    Path = $path
                    Verified = $true
                    Evidence = 'Confirmed by latest matching VPX log'
                    Candidates = @($candidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf })
                }
            }
        }
    }
    $existing = @($candidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -Unique)
    $path = if ($existing.Count) {
        $existing[0]
    }
    else {
        ''
    }
    return [pscustomobject]@{
        Path = $path
        Verified = $false
        Evidence = 'Candidate only; launch a table to confirm which INI VPX uses'
        Candidates = $existing
    }
}

function Get-SetupHealth([string]$LauncherDirectory, $Config, [string]$VpxExecutable = '', [string]$TablesDirectory = '') {
    $rows = [Collections.Generic.List[object]]::new()
    function Add-Row($Name, $Status, $Details, $Path = '', $Blocking = $false) {
        $rows.Add([pscustomobject]@{
                Component = $Name; Status = $Status; Details = $Details; Path = $Path; Blocking = $Blocking
            })
    }
    $runner = Resolve-SetupPath (Get-SetupValue $Config 'vpinballExecutable') $LauncherDirectory
    $target = Get-SetupVpxTarget $runner (Get-SetupValue $Config 'realVpinballExecutable')
    if (!$VpxExecutable) {
        $VpxExecutable = $target.Target
    }
    if (!$TablesDirectory) {
        $TablesDirectory = Resolve-SetupPath (Get-SetupValue $Config 'tablesDirectory') $LauncherDirectory
    }
    $runnerFound = $runner -and (Test-Path -LiteralPath $runner -PathType Leaf)
    Add-Row 'Launch program' $(if ($runnerFound) {
            'Found'
        }
        else {
            'Missing'
        }) $target.Kind $runner (!$runnerFound)
    if ($target.Kind -eq 'Launch helper') {
        Add-Row 'Helper target' 'Needs testing' $target.Evidence $target.Target
    }
    $vpxFound = $VpxExecutable -and (Test-Path -LiteralPath $VpxExecutable -PathType Leaf)
    $version = if ($vpxFound) {
        [Diagnostics.FileVersionInfo]::GetVersionInfo($VpxExecutable).FileVersion
    }
    else {
        ''
    }
    Add-Row 'VPX executable' $(if ($vpxFound) {
            'Found'
        }
        else {
            'Missing'
        }) $(if ($version) {
            "Version $version"
        }
        else {
            'Select the actual VPX executable, separately from a helper'
        }) $VpxExecutable (!$vpxFound)
    $tablesFound = $TablesDirectory -and (Test-Path -LiteralPath $TablesDirectory -PathType Container)
    Add-Row 'Tables folder' $(if ($tablesFound) {
            'Found'
        }
        else {
            'Missing'
        }) 'Table INIs and scripts remain authoritative; this report does not change them.' $TablesDirectory (!$tablesFound)
    foreach ($pair in @(@('Table artwork and previews', 'tableMediaDirectory', 'Media\Tables'), @('Wheel artwork', 'wheelDirectory', 'Media\Wheel'))) {
        $media = Resolve-SetupPath (Get-SetupValue $Config $pair[1] $pair[2]) $LauncherDirectory
        Add-Row $pair[0] $(if ($media -and (Test-Path -LiteralPath $media -PathType Container)) {
                'Found'
            }
            else {
                'Needs testing'
            }) 'Optional artwork. Tables can be browsed without it; existing media paths are preserved.' $media
    }
    if (!$vpxFound) {
        return $rows.ToArray()
    }
    $root = [IO.Path]::GetDirectoryName($VpxExecutable)
    $iniLocation = Get-SetupIniLocation $VpxExecutable
    Add-Row 'VPX settings' $(if ($iniLocation.Verified) {
            'Found'
        }
        else {
            'Needs testing'
        }) $iniLocation.Evidence $iniLocation.Path
    $settings = Read-SetupIni $iniLocation.Path
    $vr = Get-SetupValue ([pscustomobject]$settings) 'PlayerVR/AskToTurnOn'
    Add-Row 'VR mode' 'Needs testing' $(if ($vr -eq '0') {
            'Always-on VR configured; headset/runtime still needs a launch test'
        }
        elseif ($vr -eq '2') {
            'Detected INI has VR turned off; verify the intended INI before changing it'
        }
        else {
            'Use a table test to confirm the OpenXR runtime and headset'
        }) $iniLocation.Path
    $registered = ''
    try {
        $registered = (Get-ItemProperty 'HKCU:\Software\Freeware\Visual PinMame\globals' -ErrorAction Stop).rompath
    }
    catch {
    }
    foreach ($roms in @(Get-SetupRomDirectories '' $VpxExecutable $registered)) {
        Add-Row 'ROM library' $(if (Test-Path -LiteralPath $roms -PathType Container) {
                'Found'
            }
            else {
                'Needs testing'
            }) 'Checked by table readiness too. Table-local pinmame folders and custom INIs can override libraries; ZIP contents are unverified.' $roms
    }
    foreach ($pair in @(@('Native PinMAME', 'pinmame', 'plugin-pinmame64.dll'), @('Native Serum', 'serum', 'plugin-serum64.dll'), @('Native ScoreView', 'scoreview', 'plugin-scoreview64.dll'), @('Native FlexDMD', 'flexdmd', 'plugin-flexdmd64.dll'), @('Native PuP', 'pup', 'plugin-pup64.dll'), @('Native B2S', 'b2s', 'plugin-b2s64.dll'))) {
        $file = Join-Path $root ("plugins\{0}\{1}" -f $pair[1], $pair[2])
        $found = Test-Path -LiteralPath $file -PathType Leaf
        $key = 'Plugin.' + $pair[1] + '/Enable'
        $value = Get-SetupValue ([pscustomobject]$settings) $key
        Add-Row $pair[0] $(if ($found) {
                'Found'
            }
            else {
                'Needs testing'
            }) $(if (!$found) {
                'Not detected; needed only by tables that use it'
            }
            elseif ($value -eq '0') {
                'Installed, disabled globally; table INIs may enable it'
            }
            else {
                'Installed; presence does not confirm table compatibility or display routing'
            }) $file
    }
    foreach ($pair in @(@('Legacy VPinMAME', 'VPinMAME\VPinMAME64.dll'), @('Legacy FlexDMD', 'VPinMAME\FlexDMD.dll'), @('External DMD settings', 'VPinMAME\DmdDevice.ini'))) {
        $file = Join-Path $root $pair[1]
        Add-Row $pair[0] $(if (Test-Path -LiteralPath $file -PathType Leaf) {
                'Found'
            }
            else {
                'Needs testing'
            }) 'Separate from native plugins; COM registration and per-table behavior require testing.' $file
    }
    $profiles = @(Get-SetupValue $Config 'vpxProfiles' @())
    foreach ($launchProfile in $profiles) {
        if (!$launchProfile) {
            continue
        }
        $exe = Resolve-SetupPath (Get-SetupValue $launchProfile 'executable') $LauncherDirectory
        Add-Row ('Profile: ' + (Get-SetupValue $launchProfile 'name')) $(if ($exe -and (Test-Path -LiteralPath $exe -PathType Leaf)) {
                'Found'
            }
            else {
                'Needs testing'
            }) 'Existing profile and table assignments will be preserved.' $exe
    }
    Add-Row 'Controls, audio and DMD' 'Needs testing' 'Use a selected table to confirm input, comfortable volume, scoring and the correct DMD artwork.'
    return $rows.ToArray()
}

function Get-SetupPlan([string]$LauncherDirectory, $Config, [string]$VpxExecutable, [string]$TablesDirectory, [ValidateSet('Keep', 'Direct', 'Helper')][string]$LaunchMode = 'Keep', [bool]$EnableVR = $false) {
    $vpx = Resolve-SetupPath $VpxExecutable $LauncherDirectory
    $tables = Resolve-SetupPath $TablesDirectory $LauncherDirectory
    if (!$vpx -or !(Test-Path -LiteralPath $vpx -PathType Leaf) -or [IO.Path]::GetFileName($vpx) -notmatch '^VPinballX(?:_|64|\.exe)') {
        throw 'Choose the actual VPinballX executable, not a launcher or helper.'
    }
    if (!$tables -or !(Test-Path -LiteralPath $tables -PathType Container)) {
        throw 'Choose an existing tables folder.'
    }
    $next = $Config | ConvertTo-Json -Depth 30 | ConvertFrom-Json
    $oldRunner = Resolve-SetupPath (Get-SetupValue $Config 'vpinballExecutable') $LauncherDirectory
    $oldTarget = Get-SetupVpxTarget $oldRunner (Get-SetupValue $Config 'realVpinballExecutable')
    $runner = $oldRunner
    if ($LaunchMode -eq 'Direct') {
        $runner = $vpx
    }
    elseif ($LaunchMode -eq 'Helper') {
        $runner = Join-Path $LauncherDirectory 'tools\VpxBackgroundInput.exe'
    }
    elseif (!$runner -or !(Test-Path -LiteralPath $runner -PathType Leaf)) {
        throw 'The current launch program is missing. Choose Direct or Background helper.'
    }
    elseif ($oldTarget.Kind -eq 'VPX directly') {
        $runner = $vpx
    }
    elseif (!$oldTarget.Target -or $oldTarget.Target -ine $vpx) {
        throw 'The existing helper target cannot be verified for this VPX path. Choose Direct or Background helper explicitly.'
    }
    $changes = [Collections.Generic.List[string]]::new()
    foreach ($item in @(@('vpinballExecutable', $runner), @('realVpinballExecutable', $vpx), @('tablesDirectory', $tables))) {
        $label = @{
            vpinballExecutable = 'Launch program'
            realVpinballExecutable = 'Actual VPX executable'
            tablesDirectory = 'Tables folder'
        }[$item[0]]
        if ((Get-SetupValue $Config $item[0]) -cne $item[1]) {
            $changes.Add("${label}: $(Get-SetupValue $Config $item[0]) -> $($item[1])")
        }
        $next | Add-Member -NotePropertyName $item[0] -NotePropertyValue $item[1] -Force
    }
    if ($LaunchMode -eq 'Helper') {
        $changes.Add("Build/replace background-input helper and target file in $(Join-Path $LauncherDirectory 'tools'). Existing files will be backed up.")
    }
    $ini = Get-SetupIniLocation $vpx
    if ($EnableVR) {
        if (!$ini.Verified) {
            throw 'The active INI is not confirmed. Test a table first, then refresh health before enabling VR.'
        }
        $changes.Add("VPX settings: $($ini.Path) [PlayerVR] AskToTurnOn -> 0 (always VR)")
    }
    return [pscustomobject]@{
        Config = $next
        Changes = $changes.ToArray()
        VpxExecutable = $vpx
        ConfigPath = (Join-Path $LauncherDirectory 'launcher-config.json')
        IniPath = $ini.Path
        EnableVR = $EnableVR
        InstallHelper = ($LaunchMode -eq 'Helper')
        LauncherDirectory = $LauncherDirectory
        OriginalConfigJson = ($Config | ConvertTo-Json -Depth 30)
    }
}

function Set-SetupVrIni([string]$Path) {
    # Preserve every original byte outside the single setting/section insertion.
    $bytes = [IO.File]::ReadAllBytes($Path)
    $prefix = [byte[]]@()
    $offset = 0
    $encoding = [Text.Encoding]::GetEncoding(28591)
    if ($bytes.Length -ge 3 -and $bytes[0] -eq 239 -and $bytes[1] -eq 187 -and $bytes[2] -eq 191) {
        $prefix = [byte[]]@(239, 187, 191)
        $offset = 3
    }
    elseif ($bytes.Length -ge 2 -and $bytes[0] -eq 255 -and $bytes[1] -eq 254) {
        $prefix = [byte[]]@(255, 254)
        $offset = 2
        $encoding = [Text.Encoding]::Unicode
    }
    elseif ($bytes.Length -ge 2 -and $bytes[0] -eq 254 -and $bytes[1] -eq 255) {
        $prefix = [byte[]]@(254, 255)
        $offset = 2
        $encoding = [Text.Encoding]::BigEndianUnicode
    }
    $text = $encoding.GetString($bytes, $offset, $bytes.Length - $offset)
    $newline = if ($text.Contains("`r`n")) {
        "`r`n"
    }
    else {
        "`n"
    }
    $section = [regex]::Match($text, '(?ims)^\[PlayerVR\](?:\r?\n|\z).*?(?=^\[|\z)')
    if ($section.Success) {
        $replacement = $section.Value
        if ($replacement -match '(?m)^\s*AskToTurnOn\s*=') {
            $replacement = [regex]::Replace($replacement, '(?im)^(\s*AskToTurnOn\s*=)[^\r\n]*', '$1 0')
        }
        else {
            $replacement = $replacement.TrimEnd("`r", "`n") + $newline + 'AskToTurnOn = 0' + $newline + $newline
        }
        $text = $text.Substring(0, $section.Index) + $replacement + $text.Substring($section.Index + $section.Length)
    }
    else {
        $text += $newline + '[PlayerVR]' + $newline + 'AskToTurnOn = 0' + $newline
    }
    [IO.File]::WriteAllBytes($Path, [byte[]]($prefix + $encoding.GetBytes($text)))
}

function Save-SetupPlan($Plan) {
    if (Get-Process -Name 'vr-launch', 'VPinballX_BGFX64', 'VPinballX_GL64', 'VPinballX64' -ErrorAction SilentlyContinue) {
        throw 'Close the launcher and VPX before applying setup changes.'
    }
    if (Test-Path -LiteralPath $Plan.ConfigPath -PathType Leaf) {
        $latest = Read-SetupConfig $Plan.LauncherDirectory
        if (($latest | ConvertTo-Json -Depth 30) -cne $Plan.OriginalConfigJson) {
            throw 'Configuration changed since the setup window opened. Close and reopen setup before applying.'
        }
    }
    $backup = Join-Path $Plan.LauncherDirectory ('SetupBackups\' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
    New-Item -ItemType Directory -Path $backup -Force | Out-Null
    $originalConfig = $null
    $originalIni = $null
    if (Test-Path -LiteralPath $Plan.ConfigPath) {
        $originalConfig = [IO.File]::ReadAllBytes($Plan.ConfigPath)
        [IO.File]::WriteAllBytes((Join-Path $backup 'launcher-config.json'), $originalConfig)
    }
    if ($Plan.EnableVR) {
        $originalIni = [IO.File]::ReadAllBytes($Plan.IniPath)
        [IO.File]::WriteAllBytes((Join-Path $backup 'VPinballX.ini'), $originalIni)
    }
    $Plan.Changes | Set-Content -LiteralPath (Join-Path $backup 'changes.txt') -Encoding UTF8
    $helperOriginals = @{
    }
    if ($Plan.InstallHelper) {
        foreach ($name in @('VpxBackgroundInput.exe', 'VpxBackgroundInput.vpx-path.txt')) {
            $file = Join-Path $Plan.LauncherDirectory ('tools\' + $name)
            $helperOriginals[$file] = if (Test-Path -LiteralPath $file -PathType Leaf) {
                [IO.File]::ReadAllBytes($file)
            }
            else {
                $null
            }
        }
    }
    try {
        if ($Plan.InstallHelper) {
            & (Join-Path $Plan.LauncherDirectory 'tools\install-vpx-background-input.ps1') -VpxExecutable $Plan.VpxExecutable -DestinationDirectory (Join-Path $Plan.LauncherDirectory 'tools') | Out-Null
        }
        if ($Plan.EnableVR) {
            Set-SetupVrIni $Plan.IniPath
        }
        $temporary = $Plan.ConfigPath + '.setup-tmp'
        [IO.File]::WriteAllText($temporary, ($Plan.Config | ConvertTo-Json -Depth 30), [Text.UTF8Encoding]::new($false))
        if (Test-Path -LiteralPath $Plan.ConfigPath) {
            [IO.File]::Replace($temporary, $Plan.ConfigPath, (Join-Path $backup 'config-before-atomic-replace.json'))
        }
        else {
            [IO.File]::Move($temporary, $Plan.ConfigPath)
        }
    }
    catch {
        if ($null -ne $originalConfig) {
            [IO.File]::WriteAllBytes($Plan.ConfigPath, $originalConfig)
        }
        if ($null -ne $originalIni) {
            [IO.File]::WriteAllBytes($Plan.IniPath, $originalIni)
        }
        foreach ($file in $helperOriginals.Keys) {
            if ($null -ne $helperOriginals[$file]) {
                [IO.File]::WriteAllBytes($file, $helperOriginals[$file])
            }
            elseif (Test-Path -LiteralPath $file -PathType Leaf) {
                Remove-Item -LiteralPath $file -Force
            }
        }
        throw
    }
    return $backup
}

Export-ModuleMember -Function *-Setup*
