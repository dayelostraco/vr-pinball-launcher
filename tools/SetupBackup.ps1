# Backup validation and restore transactions; loaded by SetupCore.psm1.
function Get-SetupStatePath {
    return Join-Path $env:USERPROFILE 'AppData\LocalLow\DefaultCompany\vr-launch\state.json'
}

function Assert-SetupClosed {
    if (Get-Process -Name 'vr-launch', 'VPinballX_BGFX64', 'VPinballX_GL64', 'VPinballX64' -ErrorAction SilentlyContinue) {
        throw 'Close the launcher and VPX before exporting or restoring a backup.'
    }
}

function Get-SetupBytesHash([byte[]]$Bytes) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        return ([BitConverter]::ToString($sha.ComputeHash($Bytes))).Replace('-', '')
    }
    finally {
        $sha.Dispose()
    }
}

function Test-SetupBackupJson([string]$Name, [byte[]]$Bytes) {
    $value = [Text.Encoding]::UTF8.GetString($Bytes).TrimStart([char]0xFEFF) | ConvertFrom-Json -ErrorAction Stop
    if (!$value -or $value -is [array] -or $value -is [string] -or $value -is [ValueType]) {
        throw "Invalid JSON object: $Name"
    }
    if ($Name -eq 'state.json' -and ((Get-SetupValue $value 'version' 0) -ne 1 -or $null -eq $value.PSObject.Properties['favorites'] -or $null -eq $value.PSObject.Properties['plays'])) {
        throw 'Unsupported or incomplete player history.'
    }
    if ($Name -eq 'state.json') {
        if ($value.favorites -isnot [array] -or $value.plays -isnot [array]) {
            throw 'History favorites and plays must be arrays.'
        }
        foreach ($favorite in $value.favorites) {
            if ($favorite -isnot [string] -or [string]::IsNullOrWhiteSpace($favorite)) {
                throw 'Invalid favorite path.'
            }
        }
        foreach ($play in $value.plays) {
            $playPath = Get-SetupValue $play 'path' $null
            $playCount = Get-SetupValue $play 'count' -1
            if ($playPath -isnot [string] -or [string]::IsNullOrWhiteSpace($playPath) -or
                ($playCount -isnot [int] -and $playCount -isnot [long]) -or
                $playCount -lt 0 -or $playCount -gt [int]::MaxValue) {
                throw 'Invalid play record.'
            }
        }
        if ($value.PSObject.Properties['launches']) {
            if ($null -ne $value.launches -and $value.launches -isnot [array]) {
                throw 'Launch history must be an array.'
            }
            foreach ($launch in $value.launches) {
                $launchPath = Get-SetupValue $launch 'path' $null
                if ($launchPath -isnot [string] -or [string]::IsNullOrWhiteSpace($launchPath)) {
                    throw 'Invalid launch record.'
                }
            }
        }
    }
    return $value
}

function Export-SetupBackup([string]$LauncherDirectory, [string]$Destination, [string]$StatePath = (Get-SetupStatePath)) {
    Assert-SetupClosed
    Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
    if (Test-Path -LiteralPath $Destination) {
        throw 'Choose a new backup filename; an existing file will not be overwritten.'
    }
    $files = @{
    }
    $configPath = Join-Path $LauncherDirectory 'launcher-config.json'
    if (!(Test-Path -LiteralPath $configPath -PathType Leaf)) {
        throw 'No launcher configuration to export.'
    }
    $files['launcher-config.json'] = [IO.File]::ReadAllBytes($configPath)
    if (Test-Path -LiteralPath $StatePath -PathType Leaf) {
        $files['state.json'] = [IO.File]::ReadAllBytes($StatePath)
    }
    $hashes = [ordered]@{
    }
    foreach ($name in $files.Keys) {
        $null = Test-SetupBackupJson $name $files[$name]
        $hashes[$name] = Get-SetupBytesHash $files[$name]
    }
    $manifest = [ordered]@{
        format = 'VRPinballLauncherBackup'
        version = 1
        createdUtc = [DateTime]::UtcNow.ToString('o')
        files = $hashes
    }
    $files['manifest.json'] = [Text.Encoding]::UTF8.GetBytes(($manifest | ConvertTo-Json -Depth 8))
    $archive = $null
    try {
        $archive = [IO.Compression.ZipFile]::Open($Destination, [IO.Compression.ZipArchiveMode]::Create)
        foreach ($name in $files.Keys) {
            $entry = $archive.CreateEntry($name)
            $stream = $entry.Open()
            try {
                $stream.Write($files[$name], 0, $files[$name].Length)
            }
            finally {
                $stream.Dispose()
            }
        }
    }
    catch {
        if ($archive) {
            $archive.Dispose()
            $archive = $null
        }
        if (Test-Path -LiteralPath $Destination) {
            Remove-Item -LiteralPath $Destination -Force
        }
        throw
    }
    finally {
        if ($archive) {
            $archive.Dispose()
        }
    }
    return $Destination
}

function Read-SetupBackup([string]$Path) {
    Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($Path)
    $files = @{
    }
    try {
        if ($archive.Entries.Count -gt 3) {
            throw 'Unexpected backup contents.'
        }
        foreach ($entry in $archive.Entries) {
            $name = $entry.FullName
            if ($name -cnotin @('manifest.json', 'launcher-config.json', 'state.json') -or $files.ContainsKey($name) -or $entry.Length -gt 10MB) {
                throw 'Unexpected, duplicate, or oversized backup entry.'
            }
            $stream = $entry.Open()
            $memory = [IO.MemoryStream]::new()
            try {
                $buffer = New-Object byte[] 8192
                while (($count = $stream.Read($buffer, 0, $buffer.Length)) -gt 0) {
                    if ($memory.Length + $count -gt 10MB) {
                        throw 'Backup entry exceeds limit.'
                    }
                    $memory.Write($buffer, 0, $count)
                }
                $files[$name] = $memory.ToArray()
            }
            finally {
                $stream.Dispose()
                $memory.Dispose()
            }
        }
    }
    finally {
        $archive.Dispose()
    }
    if (!$files.ContainsKey('manifest.json') -or !$files.ContainsKey('launcher-config.json')) {
        throw 'Incomplete launcher backup.'
    }
    $manifest = Test-SetupBackupJson 'manifest.json' $files['manifest.json']
    if ((Get-SetupValue $manifest 'format') -cne 'VRPinballLauncherBackup' -or (Get-SetupValue $manifest 'version' 0) -ne 1) {
        throw 'Unsupported backup format.'
    }
    $hashes = Get-SetupValue $manifest 'files'
    foreach ($name in $files.Keys) {
        if ($name -eq 'manifest.json') {
            continue
        }
        if ((Get-SetupValue $hashes $name) -cne (Get-SetupBytesHash $files[$name])) {
            throw "Backup checksum failed: $name"
        }
        $null = Test-SetupBackupJson $name $files[$name]
    }
    foreach ($property in $hashes.PSObject.Properties) {
        if (!$files.ContainsKey($property.Name) -or $property.Name -eq 'manifest.json') {
            throw 'Manifest references an unexpected file.'
        }
    }
    return [pscustomobject]@{
        Files = $files
        CreatedUtc = (Get-SetupValue $manifest 'createdUtc')
        Config = (Test-SetupBackupJson 'launcher-config.json' $files['launcher-config.json'])
    }
}

function New-SetupRestorePlan([string]$LauncherDirectory, [string]$ArchivePath, [bool]$RestoreConfig = $true, [bool]$RestoreHistory = $true, [bool]$KeepMachinePaths = $true, [string]$StatePath = (Get-SetupStatePath)) {
    $backup = Read-SetupBackup $ArchivePath
    $files = @{
    }
    $changes = [Collections.Generic.List[string]]::new()
    if ($RestoreConfig) {
        $config = $backup.Config
        if ($KeepMachinePaths) {
            $current = Read-SetupConfig $LauncherDirectory
            foreach ($name in @('vpinballExecutable', 'realVpinballExecutable', 'tablesDirectory', 'wheelDirectory', 'tableMediaDirectory', 'enableControllerBridge', 'controllerBridgePath', 'controllerBridgeArgs', 'controllerBridgeWorkingDir', 'vpxProfiles', 'tableProfiles')) {
                if ($current.PSObject.Properties[$name]) {
                    $config | Add-Member -NotePropertyName $name -NotePropertyValue $current.$name -Force
                }
                elseif ($config.PSObject.Properties[$name]) {
                    $config.PSObject.Properties.Remove($name)
                }
            }
        }
        $files[(Join-Path $LauncherDirectory 'launcher-config.json')] = [Text.Encoding]::UTF8.GetBytes(($config | ConvertTo-Json -Depth 30))
        $changes.Add($(if ($KeepMachinePaths) {
                    'Restore launcher preferences; keep this PC''s paths, controller launch settings, profiles and table assignments.'
                }
                else {
                    'Replace launcher configuration, including paths, profiles, assignments and controller launch settings from the backup.'
                }))
        $changes.Add('Review restored executable paths before launching tables. External helpers and VPX add-ons are not included.')
    }
    if ($RestoreHistory) {
        if (!$backup.Files.ContainsKey('state.json')) {
            throw 'This backup contains no player history. Uncheck history to restore settings only.'
        }
        $files[$StatePath] = $backup.Files['state.json']
        $changes.Add('Replace favorites, play counts, history, browsing selection and table launch results. This does not merge history.')
    }
    if ($files.Count -eq 0) {
        throw 'Select settings or player history to restore.'
    }
    $originals = @{
    }
    foreach ($path in $files.Keys) {
        $originals[$path] = if (Test-Path -LiteralPath $path -PathType Leaf) {
            [IO.File]::ReadAllBytes($path)
        }
        else {
            $null
        }
    }
    return [pscustomobject]@{
        LauncherDirectory = $LauncherDirectory
        Files = $files
        Originals = $originals
        Changes = $changes.ToArray()
        CreatedUtc = $backup.CreatedUtc
        ArchivePath = $ArchivePath
    }
}

function Save-SetupRestorePlan($Plan) {
    Assert-SetupClosed
    foreach ($path in $Plan.Originals.Keys) {
        $current = if (Test-Path -LiteralPath $path -PathType Leaf) {
            [IO.File]::ReadAllBytes($path)
        }
        else {
            $null
        }
        $original = $Plan.Originals[$path]
        if (($null -eq $current) -ne ($null -eq $original) -or ($null -ne $current -and (Get-SetupBytesHash $current) -cne (Get-SetupBytesHash $original))) {
            throw 'A destination changed after review. Review the restore again.'
        }
    }
    $directory = Join-Path $Plan.LauncherDirectory ('SetupBackups\restore-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
    New-Item -ItemType Directory -Force $directory | Out-Null
    $Plan.Changes | Set-Content -LiteralPath (Join-Path $directory 'changes.txt') -Encoding UTF8
    @($Plan.Originals.Keys | ForEach-Object { [pscustomobject]@{
                Path = $_; Existed = ($null -ne $Plan.Originals[$_])
            } }) | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $directory 'restore-destinations.json') -Encoding UTF8
    $written = [Collections.Generic.List[string]]::new()
    try {
        foreach ($path in $Plan.Files.Keys) {
            $original = $Plan.Originals[$path]
            if ($null -ne $original) {
                [IO.File]::WriteAllBytes((Join-Path $directory ([IO.Path]::GetFileName($path))), $original)
            }
            $parent = [IO.Path]::GetDirectoryName($path)
            New-Item -ItemType Directory -Force $parent | Out-Null
            $temp = $path + '.restore-' + [Guid]::NewGuid().ToString('N')
            [IO.File]::WriteAllBytes($temp, $Plan.Files[$path])
            try {
                if (Test-Path -LiteralPath $path) {
                    [IO.File]::Replace($temp, $path, (Join-Path $directory ('atomic-' + [IO.Path]::GetFileName($path))))
                }
                else {
                    [IO.File]::Move($temp, $path)
                }
                $written.Add($path)
            }
            finally {
                if (Test-Path -LiteralPath $temp) {
                    Remove-Item -LiteralPath $temp -Force
                }
            }
        }
    }
    catch {
        foreach ($path in $written) {
            if ($null -ne $Plan.Originals[$path]) {
                [IO.File]::WriteAllBytes($path, $Plan.Originals[$path])
            }
            else {
                Remove-Item -LiteralPath $path -Force
            }
        }
        throw
    }
    return $directory
}
