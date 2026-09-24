# Shared helpers for build.ps1, test.ps1 and tools\unity-batch.ps1. Dot-source this file.

# Prefer the version pinned in ProjectVersion.txt; fall back to the newest
# installed editor, warning about the mismatch rather than failing silently.
function Find-UnityExe {
    param([string]$ProjectPath, [string]$Explicit)

    if ($Explicit) {
        if (-not (Test-Path $Explicit)) { throw "Unity.exe not found at '$Explicit'." }
        return $Explicit
    }

    $versionFile = Join-Path $ProjectPath "ProjectSettings\ProjectVersion.txt"
    $pinned = $null
    if (Test-Path $versionFile) {
        $line = Select-String -Path $versionFile -Pattern '^m_EditorVersion:\s*(.+)$'
        if ($line) { $pinned = $line.Matches[0].Groups[1].Value.Trim() }
    }

    $root = "C:\Program Files\Unity\Hub\Editor"
    if (-not (Test-Path $root)) { throw "No Unity editors found under '$root'. Install one via Unity Hub." }

    if ($pinned) {
        $exact = Join-Path $root "$pinned\Editor\Unity.exe"
        if (Test-Path $exact) { return $exact }
        Write-Warning "Pinned editor $pinned is not installed; falling back to the newest available."
    }

    $candidate = Get-ChildItem $root -Directory |
        Sort-Object Name -Descending |
        ForEach-Object { Join-Path $_.FullName "Editor\Unity.exe" } |
        Where-Object { Test-Path $_ } |
        Select-Object -First 1

    if (-not $candidate) { throw "No Unity.exe found under '$root'." }
    return $candidate
}

# Unity.exe relaunches itself and the launcher process returns 0 immediately,
# so waiting on it proves nothing. Wait for the real editor process instead.
function Wait-UnityExit {
    param([string]$ExePath, [datetime]$Since)

    while ($true) {
        $running = Get-Process -Name 'Unity' -ErrorAction SilentlyContinue |
            Where-Object {
                $_.StartTime -ge $Since.AddSeconds(-5) -and
                ($_.Path -eq $ExePath -or -not $_.Path)
            }
        if (-not $running) { break }
        Start-Sleep -Seconds 3
    }
}

# A Personal licence is served by Unity Hub's licensing client. With the Hub
# closed, batchmode exits 198 with "No valid Unity Editor license found", so
# make sure it is running before building.
function Ensure-UnityHub {
    if (Get-Process -Name 'Unity Hub' -ErrorAction SilentlyContinue) { return }

    $appId = 'shell:AppsFolder\UnityTechnologies.UnityHub_2vrhnee42bhxm!UnityHub'
    Write-Host "Unity Hub is not running; starting it for licence validation..."
    try { Start-Process $appId } catch {
        Write-Warning "Could not start Unity Hub automatically. Start it manually if the build reports a licence error."
        return
    }

    for ($i = 0; $i -lt 20; $i++) {
        Start-Sleep -Seconds 2
        if (Get-Process -Name 'Unity.Licensing.Client' -ErrorAction SilentlyContinue) { break }
    }
}

# Batchmode refuses to open a project the editor already has open, and says so only in the log.
function Assert-NoOpenEditor {
    param([string]$LogFile)
    if ((Test-Path $LogFile) -and
        (Select-String -Path $LogFile -Pattern 'another Unity instance is running with this project open' -Quiet)) {
        throw "The project is open in the Unity editor on this PC. Close it and retry."
    }
}
