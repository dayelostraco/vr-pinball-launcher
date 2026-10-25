<# Record release binary integrity, signatures and resolved package versions.
   This is a provenance inventory, not a vulnerability scanner or an SBOM certification.
#>
param(
    [string]$BuildDirectory = (Join-Path (Split-Path $PSScriptRoot -Parent) 'Build'),
    [string]$SourceDirectory = (Split-Path $PSScriptRoot -Parent),
    [string]$OutputPath = (Join-Path (Split-Path $PSScriptRoot -Parent) 'dist\release-inventory.json')
)
$ErrorActionPreference = 'Stop'
if (!(Test-Path -LiteralPath (Join-Path $BuildDirectory 'vr-launch.exe') -PathType Leaf)) {
    throw 'Build the player before generating its release inventory.'
}
$files = @()
foreach ($inputRoot in @(
    @{ Name = 'Build'; Path = $BuildDirectory }
)) {
    $base = [IO.Path]::GetFullPath($inputRoot.Path).TrimEnd('\') + '\'
    foreach ($file in Get-ChildItem -LiteralPath $base -Recurse -File | Where-Object Extension -in @('.dll', '.exe')) {
        $signature = Get-AuthenticodeSignature -LiteralPath $file.FullName
        $files += [ordered]@{
            path = $inputRoot.Name + '/' + $file.FullName.Substring($base.Length).Replace('\', '/')
            size = $file.Length
            sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            fileVersion = $file.VersionInfo.FileVersion
            signatureStatus = [string]$signature.Status
            signer = if ($signature.SignerCertificate) { $signature.SignerCertificate.Subject } else { $null }
        }
    }
}
$version = Select-String -LiteralPath (Join-Path $SourceDirectory 'ProjectSettings\ProjectVersion.txt') -Pattern '^m_EditorVersion:\s*(.+)$'
$inventory = [ordered]@{
    formatVersion = 1
    scope = 'Standard launcher player; experimental Touch add-on excluded and inventoried by its native-lock.json.'
    generatedUtc = [DateTime]::UtcNow.ToString('o')
    unityEditor = $version.Matches[0].Groups[1].Value.Trim()
    unityPackages = (Get-Content -LiteralPath (Join-Path $SourceDirectory 'Packages\packages-lock.json') -Raw | ConvertFrom-Json).dependencies
    binaries = @($files | Sort-Object { $_.path })
    limitations = 'Hashes identify this payload. Signatures and package versions do not establish absence of vulnerabilities. Review Unity and native vendor advisories separately.'
}
$destination = [IO.Path]::GetFullPath($OutputPath)
New-Item -ItemType Directory -Force -Path (Split-Path $destination -Parent) | Out-Null
[IO.File]::WriteAllText($destination, ($inventory | ConvertTo-Json -Depth 30), [Text.UTF8Encoding]::new($false))
Write-Output "Release inventory: $destination ($($files.Count) binaries)"
