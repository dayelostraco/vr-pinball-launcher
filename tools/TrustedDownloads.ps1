# Versioned digests are reviewed in source; never trust a checksum fetched with the payload at install time.
function Get-TrustedDownload([string]$Name, [string]$LockPath = (Join-Path $PSScriptRoot 'download-lock.json')) {
    $lock = Get-Content -LiteralPath $LockPath -Raw | ConvertFrom-Json
    $entry = $lock.PSObject.Properties[$Name].Value
    if (!$entry -or $entry.sha256 -notmatch '^[a-fA-F0-9]{64}$') {
        throw 'No reviewed download digest exists for this component.'
    }
    $uri = [Uri]$entry.url
    if (!$uri.IsAbsoluteUri -or $uri.Scheme -ne 'https' -or $uri.Port -ne 443 -or
        $uri.Host -notin @('github.com', 'www.autohotkey.com') -or $uri.UserInfo -or
        [string]::IsNullOrWhiteSpace([IO.Path]::GetFileName($uri.LocalPath))) {
        throw 'Download source must be an approved HTTPS host.'
    }
    $directory = Join-Path ([IO.Path]::GetTempPath()) ('vr-launcher-download-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $directory | Out-Null
    $file = Join-Path $directory ([IO.Path]::GetFileName($uri.LocalPath))
    Invoke-WebRequest -Uri $uri.AbsoluteUri -OutFile $file -UseBasicParsing
    if ((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ine $entry.sha256) {
        throw "Download integrity check failed for $Name. Nothing was installed; inspect $directory."
    }
    Write-Host "Verified $Name $($entry.version) (SHA-256)"
    return $file
}
