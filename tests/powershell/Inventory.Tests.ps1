BeforeAll {
    $script:inventoryTool = Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'tools\write-release-inventory.ps1'
}

Describe 'Release provenance inventory' {
    It 'records complete binary hashes, package versions and honest signature status without personal paths' {
        $source = Join-Path $TestDrive 'Source'
        $build = Join-Path $TestDrive 'Build'
        foreach ($directory in @($build, "$source\ControllerBridge", "$source\ProjectSettings", "$source\Packages")) {
            New-Item -ItemType Directory -Force -Path $directory | Out-Null
        }
        Set-Content -LiteralPath "$build\vr-launch.exe" -Value 'fixture player'
        Set-Content -LiteralPath "$build\fixture.dll" -Value 'standard library'
        Set-Content -LiteralPath "$source\ControllerBridge\fixture.dll" -Value 'fixture library'
        Set-Content -LiteralPath "$source\ProjectSettings\ProjectVersion.txt" -Value 'm_EditorVersion: 6000.6.2f1'
        Set-Content -LiteralPath "$source\Packages\packages-lock.json" -Value '{"dependencies":{"fixture.package":{"version":"1.2.3","source":"registry"}}}'
        Mock Get-AuthenticodeSignature { [pscustomobject]@{ Status = 'NotSigned'; SignerCertificate = $null } }
        Mock Get-AuthenticodeSignature { [pscustomobject]@{ Status = 'Valid'; SignerCertificate = [pscustomobject]@{ Subject = 'Fixture publisher' } } } -ParameterFilter { $LiteralPath -like '*.dll' }
        $output = Join-Path $TestDrive 'Output\inventory.json'
        & $inventoryTool -SourceDirectory $source -BuildDirectory $build -OutputPath $output
        $text = Get-Content -LiteralPath $output -Raw
        $report = $text | ConvertFrom-Json
        $report.unityEditor | Should -Be '6000.6.2f1'
        $report.unityPackages.'fixture.package'.version | Should -Be '1.2.3'
        $report.binaries.Count | Should -Be 2
        $player = $report.binaries | Where-Object path -eq 'Build/vr-launch.exe'
        $player.sha256 | Should -Be (Get-FileHash -LiteralPath "$build\vr-launch.exe").Hash.ToLowerInvariant()
        $player.signatureStatus | Should -Be 'NotSigned'
        ($report.binaries | Where-Object path -eq 'Build/fixture.dll').signer | Should -Be 'Fixture publisher'
        @($report.binaries | Where-Object path -like 'ControllerBridge/*').Count | Should -Be 0
        $text | Should -Not -Match ([regex]::Escape($TestDrive))
        Remove-Item -LiteralPath "$build\vr-launch.exe"
        { & $inventoryTool -SourceDirectory $source -BuildDirectory $build -OutputPath $output } | Should -Throw '*Build the player*'
    }
}
