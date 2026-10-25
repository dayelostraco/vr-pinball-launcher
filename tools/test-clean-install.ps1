<# Validate the real release package only on a disposable GitHub-hosted Windows runner.
   Unlike test-installer.ps1, this uses the real AppId and default per-user location.
#>
param(
    [Parameter(Mandatory = $true)][string]$InstallerFile,
    [Parameter(Mandatory = $true)][ValidatePattern('^[a-fA-F0-9]{64}$')][string]$ExpectedSha256
)
$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted') {
    throw 'This check requires a disposable GitHub-hosted runner; never run it on a live installation.'
}
$repo = Split-Path $PSScriptRoot -Parent
$installer = (Get-Item -LiteralPath $InstallerFile).FullName
if ((Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash -ine $ExpectedSha256) { throw 'Installer SHA-256 mismatch.' }
$destination = Join-Path $env:LOCALAPPDATA 'Programs\VR Pinball Launcher'
$registration = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{8804A507-FC38-44F5-9E05-CF9BF2913F64}_is1'
$state = Join-Path $env:USERPROFILE 'AppData\LocalLow\DefaultCompany\vr-launch'
if ((Test-Path -LiteralPath $destination) -or (Test-Path -LiteralPath $registration) -or (Test-Path -LiteralPath $state)) {
    throw 'The test account is not clean. Refusing to replace existing launcher files or state.'
}
$output = Join-Path $repo 'Logs\clean-install'
New-Item -ItemType Directory -Force $output | Out-Null
$uninstaller = Join-Path $destination 'unins000.exe'
function Invoke-CandidateInstall([string]$Name) {
    $process = Start-Process -FilePath $installer -WindowStyle Hidden -Wait -PassThru -ArgumentList @(
        '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/TASKS=',
        ('/LOG="' + (Join-Path $output ($Name + '.log')) + '"')
    )
    try { if ($process.ExitCode -ne 0) { throw "Installer failed: $($process.ExitCode)" } }
    finally { $process.Dispose() }
}
try {
    Invoke-CandidateInstall 'fresh'
    if (!(Test-Path -LiteralPath $registration)) { throw 'Per-user uninstall registration is missing.' }
    foreach ($relative in @('vr-launch.exe', 'UnityPlayer.dll', 'vr-launch_Data\Managed\VRLauncher.Runtime.dll',
        'vr-launch_Data\Managed\VRLauncher.Core.dll', 'vr-launch_Data\Managed\VRLauncher.Arcade.dll',
        'LICENSE', 'THIRD-PARTY-NOTICES.md')) {
        if (!(Test-Path -LiteralPath (Join-Path $destination $relative) -PathType Leaf)) { throw "Missing installed file: $relative" }
    }
    $config = Join-Path $destination 'launcher-config.json'
    if ((Get-FileHash -LiteralPath $config).Hash -ne (Get-FileHash -LiteralPath (Join-Path $repo 'installer\launcher-config.default.json')).Hash) {
        throw 'Fresh install did not use clean default configuration.'
    }
    if ((Test-Path -LiteralPath (Join-Path $destination 'ControllerBridge')) -or
        @(Get-ChildItem -LiteralPath $destination -Recurse -File | Where-Object Name -in @('auto_oculus_touch.dll','vJoyInterface.dll')).Count) {
        throw 'Standard installer contains experimental bridge payload.'
    }
    $menu = Join-Path ([Environment]::GetFolderPath('Programs')) 'VR Pinball Launcher'
    foreach ($name in @('VR Pinball Launcher.lnk', 'VR Pinball Launcher Setup.lnk')) {
        if (!(Test-Path -LiteralPath (Join-Path $menu $name))) { throw "Start menu shortcut missing: $name" }
    }
    & (Join-Path $destination 'tools\setup.ps1') -LauncherDirectory $destination -ReportOnly |
        Set-Content -LiteralPath (Join-Path $output 'health.json')
    $before = (Get-FileHash -LiteralPath $config).Hash
    $pictures = Join-Path $output 'setup-pages'
    & (Join-Path $destination 'tools\setup.ps1') -LauncherDirectory $destination -PreviewDirectory $pictures
    if (@(Get-ChildItem -LiteralPath $pictures -Filter '*.png').Count -ne 5) { throw 'Setup pages did not render.' }
    if ((Get-FileHash -LiteralPath $config).Hash -ne $before) { throw 'Setup preview changed configuration.' }
    $value = Get-Content -LiteralPath $config -Raw | ConvertFrom-Json
    $value | Add-Member -NotePropertyName cleanAccountSentinel -NotePropertyValue 'preserve on upgrade'
    [IO.File]::WriteAllText($config, ($value | ConvertTo-Json -Depth 30))
    $before = (Get-FileHash -LiteralPath $config).Hash
    Invoke-CandidateInstall 'upgrade'
    if ((Get-FileHash -LiteralPath $config).Hash -ne $before) { throw 'Upgrade replaced existing configuration.' }
    Write-Output 'PASS: clean hosted account, real AppId/default location, shortcuts, defaults, no experimental bridge, setup pages and upgrade preservation.'
} finally {
    if (Test-Path -LiteralPath $uninstaller) {
        $process = Start-Process -FilePath $uninstaller -ArgumentList '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART' -WindowStyle Hidden -Wait -PassThru
        try { if ($process.ExitCode -ne 0) { throw "Test uninstall failed: $($process.ExitCode)" } }
        finally { $process.Dispose() }
        if (Test-Path -LiteralPath $registration) { throw 'Uninstall registration remained after removal.' }
    }
}
