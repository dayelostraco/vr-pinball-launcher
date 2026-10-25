<# Compile the same payload with an isolated AppId, then test fresh install and upgrade.
   This does not register/uninstall the real launcher. Requires Inno Setup 6 and a built player.
#>
param(
    [string]$BuildDirectory = (Join-Path (Split-Path $PSScriptRoot -Parent) 'Build'),
    [ValidatePattern('^\d+\.\d+\.\d+$')][string]$AppVersion = '1.2.0'
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$root = Join-Path ([IO.Path]::GetTempPath()) ('launcher-installer-' + [Guid]::NewGuid().ToString('N'))
$destination = Join-Path $root 'Installed'
New-Item -ItemType Directory -Path $root | Out-Null
$compiler = @(
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
    'C:\Program Files (x86)\Inno Setup 6\ISCC.exe',
    'C:\Program Files\Inno Setup 6\ISCC.exe'
) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (!$compiler) { throw 'Install Inno Setup 6 first.' }
if (!(Test-Path -LiteralPath (Join-Path $BuildDirectory 'vr-launch.exe'))) { throw 'Build the player first.' }
$temporaryScript = Join-Path $repo ('installer\.smoke-' + [Guid]::NewGuid().ToString('N') + '.iss')
$text = [IO.File]::ReadAllText((Join-Path $repo 'installer\vr-pinball-launcher.iss'))
$testAppId = '{{' + [Guid]::NewGuid().ToString().ToUpperInvariant() + '}'
$text = [regex]::Replace($text, '(?m)^AppId=.*$', 'AppId=' + $testAppId)
$text = $text.Replace('DefaultGroupName=VR Pinball Launcher', 'DefaultGroupName=VR Pinball Launcher TEST')
$installer = Join-Path $root 'TestInstaller.exe'
$uninstaller = Join-Path $destination 'unins000.exe'
function Invoke-TestInstall {
    $process = Start-Process -FilePath $installer -WindowStyle Hidden -Wait -PassThru -ArgumentList @(
        '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/NOICONS', '/TASKS=',
        ('/DIR="' + $destination + '"'), ('/LOG="' + (Join-Path $root 'install.log') + '"')
    )
    if ($process.ExitCode -ne 0) { throw "Test installer failed: $($process.ExitCode). See $root" }
}
try {
    [IO.File]::WriteAllText($temporaryScript, $text)
    & $compiler ("/DBuildDir=$BuildDirectory") ("/DAppVersion=$AppVersion") ("/O$root") '/FTestInstaller' $temporaryScript *> (Join-Path $root 'compile.log')
    if ($LASTEXITCODE -ne 0) { throw "Test package compilation failed. See $root" }
    Invoke-TestInstall
    $config = Join-Path $destination 'launcher-config.json'
    $template = Join-Path $repo 'installer\launcher-config.default.json'
    if ((Get-FileHash -LiteralPath $config).Hash -ne (Get-FileHash -LiteralPath $template).Hash) { throw 'Fresh install default configuration mismatch.' }
    foreach ($file in @('SetupCore.psm1','SetupPaths.ps1','SetupBackup.ps1','SetupBackupPage.ps1','setup.ps1','TrustedDownloads.ps1','download-lock.json')) {
        if ((Get-FileHash (Join-Path $destination ('tools\' + $file))).Hash -ne (Get-FileHash (Join-Path $PSScriptRoot $file)).Hash) { throw "Packaged setup mismatch: $file" }
    }
    if ((Test-Path -LiteralPath (Join-Path $destination 'ControllerBridge')) -or
        @(Get-ChildItem -LiteralPath $destination -Recurse -File | Where-Object Name -in @('auto_oculus_touch.dll','vJoyInterface.dll')).Count) {
        throw 'Standard installer contains experimental bridge payload.'
    }
    foreach ($assembly in @('VRLauncher.Runtime.dll','VRLauncher.Arcade.dll','VRLauncher.Core.dll')) {
        $relative = 'vr-launch_Data\Managed\' + $assembly
        if ((Get-FileHash (Join-Path $destination $relative)).Hash -ne (Get-FileHash (Join-Path $BuildDirectory $relative)).Hash) { throw "Packaged assembly mismatch: $assembly" }
    }
    foreach ($file in @('LICENSE','THIRD-PARTY-NOTICES.md','licenses\AutoOculusTouch-MIT.txt','licenses\vJoy-MIT.txt','licenses\LiberationSans-OFL.txt')) {
        if (!(Test-Path -LiteralPath (Join-Path $destination $file))) { throw "Packaged notice missing: $file" }
    }
    $value = Get-Content -LiteralPath $config -Raw | ConvertFrom-Json
    $value | Add-Member -NotePropertyName 'installerTestSentinel' -NotePropertyValue 'preserve this existing configuration'
    [IO.File]::WriteAllText($config, ($value | ConvertTo-Json -Depth 30))
    $before = (Get-FileHash -LiteralPath $config).Hash
    # An older installation or separately installed add-on remains user-owned.
    $existingBridge = Join-Path $destination 'ControllerBridge\user-mapping.ahk'
    New-Item -ItemType Directory -Path (Split-Path $existingBridge -Parent) | Out-Null
    Set-Content -LiteralPath $existingBridge -Value 'preserve existing bridge'
    Invoke-TestInstall
    if ((Get-FileHash -LiteralPath $config).Hash -ne $before) { throw 'Upgrade replaced existing configuration.' }
    if ((Get-Content -LiteralPath $existingBridge -Raw).Trim() -ne 'preserve existing bridge') { throw 'Upgrade changed an existing bridge.' }
    & (Join-Path $destination 'tools\setup.ps1') -LauncherDirectory $destination -ReportOnly | Set-Content (Join-Path $root 'health.json')
    Write-Output 'PASS: isolated fresh install, complete payload/notices, upgrade config preservation and packaged setup report.'
} finally {
    if (Test-Path -LiteralPath $temporaryScript) { Remove-Item -LiteralPath $temporaryScript -Force }
    if (Test-Path -LiteralPath $uninstaller) {
        $process = Start-Process -FilePath $uninstaller -ArgumentList '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART' -WindowStyle Hidden -Wait -PassThru
        if ($process.ExitCode -ne 0) { Write-Warning "Test uninstall failed. Test AppId: $testAppId; artifacts: $root" }
    }
    Write-Output "Installer test artifacts: $root"
}
