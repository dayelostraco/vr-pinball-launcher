BeforeAll {
    $script:repository = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
    $script:fixtureSource = @'
$logIndex = [Array]::IndexOf($args, '-logFile')
$outputIndex = [Array]::IndexOf($args, '-buildOutput')
if ($logIndex -ge 0) {
    $text = switch ($env:VRL_BUILD_FIXTURE) {
        'compile' { 'error CS0001: fixture compile failure' }
        'failure' { 'Error: fixture build failure' }
        'locked' { 'another Unity instance is running with this project open' }
        default { "Build result: Succeeded`n[unity-batch] OK" }
    }
    [IO.File]::WriteAllText($args[$logIndex + 1], $text)
}
if ($outputIndex -ge 0 -and $env:VRL_BUILD_FIXTURE -ne 'missing') {
    [IO.File]::WriteAllText($args[$outputIndex + 1], 'Never execute: fixture output')
}
$global:LASTEXITCODE = if ($env:VRL_BUILD_FIXTURE -eq 'compiler-failure') { 7 } else { 0 }
'@
}

Describe 'Build orchestration failure handling' {
    BeforeEach {
        $script:originalFixture = $env:VRL_BUILD_FIXTURE
        $env:VRL_BUILD_FIXTURE = 'success'
        $script:fixture = Join-Path $TestDrive 'Fixture.ps1'
        Set-Content -LiteralPath $fixture -Value $fixtureSource
        $script:build = Join-Path $TestDrive ('Build-' + [Guid]::NewGuid().ToString('N'))
        $script:logs = Join-Path $TestDrive ('Logs-' + [Guid]::NewGuid().ToString('N'))
        Mock Get-Process { @() }
        Mock Get-Process { [pscustomobject]@{ Id = 1 } } -ParameterFilter { $Name -eq 'Unity Hub' }
    }
    AfterEach { $env:VRL_BUILD_FIXTURE = $originalFixture }

    It 'requires success evidence and a produced player' {
        & (Join-Path $repository 'build.ps1') -BuildDir $build -UnityExe $fixture -LogDirectory $logs -SkipInstaller
        Get-Content (Join-Path $build 'vr-launch.exe') | Should -Be 'Never execute: fixture output'
        $env:VRL_BUILD_FIXTURE = 'missing'
        Remove-Item -LiteralPath (Join-Path $build 'vr-launch.exe')
        { & (Join-Path $repository 'build.ps1') -BuildDir $build -UnityExe $fixture -LogDirectory $logs -SkipInstaller } | Should -Throw '*reported success*missing*'
    }
    It 'rejects compile errors and failed builds even when a process returns zero' {
        $env:VRL_BUILD_FIXTURE = 'compile'
        { & (Join-Path $repository 'build.ps1') -BuildDir $build -UnityExe $fixture -LogDirectory $logs -SkipInstaller } | Should -Throw '*Compilation failed*'
        $env:VRL_BUILD_FIXTURE = 'failure'
        { & (Join-Path $repository 'build.ps1') -BuildDir $build -UnityExe $fixture -LogDirectory $logs -SkipInstaller } | Should -Throw '*did not report success*'
    }
    It 'refuses to build while the player is running' {
        Mock Get-Process { [pscustomobject]@{ Id = 2 } } -ParameterFilter { $Name -eq 'vr-launch' }
        { & (Join-Path $repository 'build.ps1') -BuildDir $build -UnityExe $fixture -LogDirectory $logs -SkipInstaller } | Should -Throw '*running*'
        Test-Path -LiteralPath $build | Should -BeFalse
    }
    It 'checks batch method evidence and rejects compile errors, locks and missing completion' {
        & (Join-Path $repository 'tools\unity-batch.ps1') -Method Fixture.Run -UnityExe $fixture -LogDirectory $logs
        foreach ($case in @(@('compile', '*Compilation failed*'), @('locked', '*Close it and retry*'), @('failure', '*did not report success*'))) {
            $env:VRL_BUILD_FIXTURE = $case[0]
            { & (Join-Path $repository 'tools\unity-batch.ps1') -Method Fixture.Run -UnityExe $fixture -LogDirectory $logs } | Should -Throw $case[1]
        }
    }
    It 'requires a built player and propagates installer compiler failures' {
        { & (Join-Path $repository 'build-installer.ps1') -BuildDir $build -Compiler $fixture } | Should -Throw '*build the Unity project first*'
        New-Item -ItemType Directory -Path $build | Out-Null
        Set-Content -LiteralPath (Join-Path $build 'vr-launch.exe') -Value 'fixture'
        & (Join-Path $repository 'build-installer.ps1') -BuildDir $build -Compiler $fixture
        $env:VRL_BUILD_FIXTURE = 'compiler-failure'
        { & (Join-Path $repository 'build-installer.ps1') -BuildDir $build -Compiler $fixture } | Should -Throw '*exit code 7*'
        { & (Join-Path $repository 'build-installer.ps1') -BuildDir $build -Compiler (Join-Path $TestDrive 'missing.exe') } | Should -Throw '*compiler does not exist*'
    }
    It 'rejects experimental bridge files in a standard player build' {
        New-Item -ItemType Directory -Path $build | Out-Null
        Set-Content -LiteralPath (Join-Path $build 'vr-launch.exe') -Value 'fixture'
        $dll = Join-Path $build 'auto_oculus_touch.dll'
        Set-Content -LiteralPath $dll -Value 'stale bridge'
        { & (Join-Path $repository 'build-installer.ps1') -BuildDir $build -Compiler $fixture } | Should -Throw '*Experimental bridge files*'
        Remove-Item -LiteralPath $dll
        New-Item -ItemType Directory -Path (Join-Path $build 'ControllerBridge') | Out-Null
        { & (Join-Path $repository 'build-installer.ps1') -BuildDir $build -Compiler $fixture } | Should -Throw '*Experimental bridge files*'
    }
    It 'builds only the reviewed experimental archive without enabling or installing it' {
        $zip = Join-Path $TestDrive 'Touch-Experimental.zip'
        & (Join-Path $repository 'tools\build-controller-bridge.ps1') -OutputPath $zip
        $expanded = Join-Path $TestDrive 'Expanded'
        Expand-Archive -LiteralPath $zip -DestinationPath $expanded
        $files = @(Get-ChildItem -LiteralPath $expanded -Recurse -File)
        $files.Count | Should -Be 10
        foreach ($file in @('vpx_vr_controls.ahk','auto_oculus_touch.ahk','auto_oculus_touch.dll','vJoyInterface.dll','README.md')) {
            (Get-FileHash -LiteralPath (Join-Path $expanded ('ControllerBridge\' + $file))).Hash |
                Should -Be (Get-FileHash -LiteralPath (Join-Path $repository ('ControllerBridge\' + $file))).Hash
        }
        Test-Path -LiteralPath (Join-Path $expanded 'ControllerBridge\licenses\AutoOculusTouch-MIT.txt') | Should -BeTrue
        Test-Path -LiteralPath (Join-Path $expanded 'ControllerBridge\licenses\vJoy-MIT.txt') | Should -BeTrue
        Test-Path -LiteralPath (Join-Path $expanded 'launcher-config.json') | Should -BeFalse
    }
    It 'rejects modified or extra add-on DLLs before producing an archive' {
        $zip = Join-Path $TestDrive 'MustNotExist.zip'
        Mock Get-FileHash { [pscustomobject]@{Hash='changed'} } -ParameterFilter { $LiteralPath -like '*ControllerBridge*' }
        { & (Join-Path $repository 'tools\build-controller-bridge.ps1') -OutputPath $zip } | Should -Throw '*Native dependency integrity*'
        Test-Path -LiteralPath $zip | Should -BeFalse
        Mock Get-ChildItem { @() } -ParameterFilter { $LiteralPath -like '*ControllerBridge*' }
        { & (Join-Path $repository 'tools\build-controller-bridge.ps1') -OutputPath $zip } | Should -Throw '*Unreviewed or missing*'
    }
    It 'deploys complete runtime assemblies while preserving user configuration, media and bridge' {
        $originalLocalAppData = $env:LOCALAPPDATA
        try {
            $env:LOCALAPPDATA = Join-Path $TestDrive 'LocalAppData'
            $destination = Join-Path $env:LOCALAPPDATA 'Programs\VR Pinball Launcher'
            New-Item -ItemType Directory -Force -Path $destination | Out-Null
            foreach ($name in @('launcher-config.json', 'Media\fixture.png', 'ControllerBridge\user.ahk', 'vr-launch_Data\stale.dll')) {
                $path = Join-Path $destination $name
                New-Item -ItemType Directory -Force -Path (Split-Path $path -Parent) | Out-Null
                Set-Content -LiteralPath $path -Value 'user-owned fixture'
            }
            $managed = Join-Path $build 'vr-launch_Data\Managed'
            New-Item -ItemType Directory -Force -Path $managed | Out-Null
            Set-Content -LiteralPath (Join-Path $build 'UnityPlayer.dll') -Value 'fixture native runtime'
            foreach ($assembly in @('VRLauncher.Runtime.dll', 'VRLauncher.Core.dll', 'VRLauncher.Arcade.dll')) {
                Set-Content -LiteralPath (Join-Path $managed $assembly) -Value ('fixture ' + $assembly)
            }
            & (Join-Path $repository 'build.ps1') -BuildDir $build -UnityExe $fixture -LogDirectory $logs -SkipInstaller -Deploy
            foreach ($name in @('launcher-config.json', 'Media\fixture.png', 'ControllerBridge\user.ahk')) {
                Get-Content -LiteralPath (Join-Path $destination $name) | Should -Be 'user-owned fixture'
            }
            Test-Path -LiteralPath (Join-Path $destination 'vr-launch_Data\stale.dll') | Should -BeFalse
            foreach ($assembly in @('VRLauncher.Runtime.dll', 'VRLauncher.Core.dll', 'VRLauncher.Arcade.dll')) {
                Get-Content -LiteralPath (Join-Path $destination ('vr-launch_Data\Managed\' + $assembly)) | Should -Be ('fixture ' + $assembly)
            }
            Test-Path -LiteralPath (Join-Path $destination 'tools\setup.ps1') | Should -BeTrue
            Remove-Item -LiteralPath (Join-Path $managed 'VRLauncher.Core.dll')
            { & (Join-Path $repository 'build.ps1') -BuildDir $build -UnityExe $fixture -LogDirectory $logs -SkipInstaller -Deploy } | Should -Throw '*Incomplete deployment payload*'
            Get-Content -LiteralPath (Join-Path $destination 'vr-launch_Data\Managed\VRLauncher.Core.dll') | Should -Be 'fixture VRLauncher.Core.dll'
            { & (Join-Path $repository 'build.ps1') -BuildDir $destination -UnityExe $fixture -LogDirectory $logs -SkipInstaller -Deploy } | Should -Throw '*must not overlap*'
        }
        finally { $env:LOCALAPPDATA = $originalLocalAppData }
    }
    It 'finds an installer compiler on PATH or reports its absence' {
        New-Item -ItemType Directory -Path $build | Out-Null
        Set-Content -LiteralPath (Join-Path $build 'vr-launch.exe') -Value 'fixture'
        Mock Test-Path { $false } -ParameterFilter { $Path -like '*Inno Setup*ISCC.exe' }
        Mock Get-Command { [pscustomobject]@{ Source = $fixture } } -ParameterFilter { $Name -eq 'iscc' }
        & (Join-Path $repository 'build-installer.ps1') -BuildDir $build
        Mock Get-Command { $null } -ParameterFilter { $Name -eq 'iscc' }
        { & (Join-Path $repository 'build-installer.ps1') -BuildDir $build } | Should -Throw '*compiler (ISCC.exe) not found*'
    }
}
