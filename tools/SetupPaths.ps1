# Installation paths and launch rules. See tests/fixtures/launch-contract.json.
function Resolve-SetupPath([string]$Path, [string]$BaseDirectory) {
    if ([string]::IsNullOrWhiteSpace($Path)) {
        return ''
    }
    $expanded = [Environment]::ExpandEnvironmentVariables($Path.Trim().Trim('"'))
    if (![IO.Path]::IsPathRooted($expanded)) {
        $expanded = Join-Path $BaseDirectory $expanded
    }
    return [IO.Path]::GetFullPath($expanded)
}

function Get-SetupVpxTarget([string]$Runner, [string]$ExplicitTarget = '') {
    if (!$Runner) {
        return [pscustomobject]@{
            Runner = ''
            Target = $ExplicitTarget
            Kind = 'Not configured'
            Evidence = ''
        }
    }
    $native = [IO.Path]::GetFileName($Runner) -match '^VPinballX(?:_|64|\.exe)'
    if ($native) {
        return [pscustomobject]@{
            Runner = $Runner
            Target = $Runner
            Kind = 'VPX directly'
            Evidence = 'Executable name'
        }
    }
    $target = $ExplicitTarget
    $evidence = 'Saved VPX target'
    if ([IO.Path]::GetFileName($Runner) -ieq 'VpxBackgroundInput.exe') {
        $sidecar = Join-Path ([IO.Path]::GetDirectoryName($Runner)) 'VpxBackgroundInput.vpx-path.txt'
        $source = [IO.Path]::ChangeExtension($Runner, '.cs')
        if (Test-Path -LiteralPath $sidecar -PathType Leaf) {
            $target = [IO.File]::ReadAllText($sidecar).Trim()
            $evidence = $sidecar
        }
        elseif (Test-Path -LiteralPath $source -PathType Leaf) {
            $m = [regex]::Match([IO.File]::ReadAllText($source), 'const\s+string\s+VpxPath\s*=\s*@"([^"]+)"')
            if ($m.Success) {
                $target = $m.Groups[1].Value
                $evidence = 'Legacy helper source (target still needs a launch test)'
            }
        }
    }
    return [pscustomobject]@{
        Runner = $Runner
        Target = $target
        Kind = 'Launch helper'
        Evidence = $evidence
    }
}

function Quote-SetupArgument([string]$Value) {
    # Windows CommandLineToArgvW quoting, including embedded quotes and trailing backslashes.
    return '"' + [regex]::Replace([regex]::Replace($Value, '(\\*)"', '$1$1\"'), '(\\+)$', '$1$1') + '"'
}

function Get-SetupTestCommand($Config, [string]$Table, [string]$LauncherDirectory) {
    $Table = Resolve-SetupPath $Table $LauncherDirectory
    if (!(Test-Path -LiteralPath $Table -PathType Leaf) -or [IO.Path]::GetExtension($Table) -ine '.vpx') {
        throw 'Choose an existing .vpx table.'
    }
    $runner = Resolve-SetupPath (Get-SetupValue $Config 'vpinballExecutable') $LauncherDirectory
    $working = [IO.Path]::GetDirectoryName($runner)
    $ini = ''
    $profileName = 'Default'
    foreach ($assignment in @(Get-SetupValue $Config 'tableProfiles' @())) {
        if ((Get-SetupValue $assignment 'table') -ine [IO.Path]::GetFileNameWithoutExtension($Table)) {
            continue
        }
        foreach ($launchProfile in @(Get-SetupValue $Config 'vpxProfiles' @())) {
            if ((Get-SetupValue $launchProfile 'name') -ine (Get-SetupValue $assignment 'profile')) {
                continue
            }
            $runner = Resolve-SetupPath (Get-SetupValue $launchProfile 'executable') $LauncherDirectory
            $ini = Get-SetupValue $launchProfile 'iniFile'
            $working = Get-SetupValue $launchProfile 'workingDirectory'
            $profileName = Get-SetupValue $launchProfile 'name'
            if ($working -eq 'table') {
                $working = [IO.Path]::GetDirectoryName($Table)
            }
            elseif (!$working) {
                $working = [IO.Path]::GetDirectoryName($runner)
            }
            break
        }
        break
    }
    if (!(Test-Path -LiteralPath $runner -PathType Leaf)) {
        throw 'The selected launch program does not exist.'
    }
    $arguments = '-Play ' + (Quote-SetupArgument $Table)
    if ($ini) {
        $ini = Resolve-SetupPath $ini $LauncherDirectory
        $arguments = '-Ini ' + (Quote-SetupArgument $ini) + ' ' + $arguments
    }
    $working = Resolve-SetupPath $working $LauncherDirectory
    return [pscustomobject]@{
        Executable = $runner
        Arguments = $arguments
        WorkingDirectory = $working
        Profile = $profileName
        Table = $Table
    }
}

function Get-SetupRomDirectories([string]$Table, [string]$ActualVpx, [string]$RegisteredPaths = '') {
    $paths = [Collections.Generic.List[string]]::new()
    if ($Table) {
        $paths.Add((Join-Path ([IO.Path]::GetDirectoryName($Table)) 'pinmame\roms'))
    }
    foreach ($path in ($RegisteredPaths -split ';')) {
        $expanded = [Environment]::ExpandEnvironmentVariables($path.Trim())
        if ([IO.Path]::IsPathRooted($expanded) -and !$paths.Contains($expanded)) {
            $paths.Add($expanded)
        }
    }
    if ($ActualVpx) {
        $root = [IO.Path]::GetDirectoryName($ActualVpx)
        $paths.Add((Join-Path $root 'VPinMAME\roms'))
        $paths.Add((Join-Path $root 'plugins\pinmame\pinmame\roms'))
    }
    return $paths.ToArray()
}
