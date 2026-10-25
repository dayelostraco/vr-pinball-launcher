param(
    [Parameter(Mandatory = $true)][string]$VpxExecutable,
    [string]$DestinationDirectory
)
$ErrorActionPreference = 'Stop'
$vpxFile = Get-Item -LiteralPath $VpxExecutable
if ($vpxFile.PSIsContainer -or $vpxFile.Extension -ne '.exe') {
    throw 'VpxExecutable must name the actual VPX executable.'
}
if (!$DestinationDirectory) {
    $DestinationDirectory = Join-Path $vpxFile.DirectoryName 'Tools'
}
$destination = [IO.Path]::GetFullPath($DestinationDirectory)
$hostFile = Join-Path $destination 'VpxBackgroundInput.exe'
if ($vpxFile.FullName -eq $hostFile) { throw 'Point VpxExecutable at VPX, not the helper.' }
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { throw '.NET Framework 4 C# compiler not found.' }
New-Item -ItemType Directory -Force -Path $destination | Out-Null
$pathFile = Join-Path $destination 'VpxBackgroundInput.vpx-path.txt'
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss-fff'
foreach ($existing in @($hostFile, $pathFile)) {
    if (Test-Path -LiteralPath $existing) {
        Copy-Item -LiteralPath $existing -Destination ($existing + '.bak-' + $stamp)
    }
}
& $compiler /nologo /target:winexe "/out:$hostFile" (Join-Path $PSScriptRoot 'VpxBackgroundInput.cs')
if ($LASTEXITCODE -ne 0) { throw 'Helper compilation failed.' }
[IO.File]::WriteAllText($pathFile, $vpxFile.FullName)
$check = Start-Process -FilePath $hostFile -ArgumentList '--self-test' -WindowStyle Hidden -Wait -PassThru
if ($check.ExitCode -ne 0) { throw "Background-input self-test failed: $($check.ExitCode)" }
Write-Output "Installed and self-tested: $hostFile"
Write-Output 'Set vpinballExecutable in your installed launcher-config.json to this path, then restart the launcher.'
