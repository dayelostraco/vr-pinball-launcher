# Exercise the download trust boundary without network access or executing a payload.
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'TrustedDownloads.ps1')
$fixtureDirectory = Join-Path ([IO.Path]::GetTempPath()) ('launcher-download-tests-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixtureDirectory | Out-Null
$lockPath = Join-Path $fixtureDirectory 'lock.json'
$payload = Join-Path $fixtureDirectory 'payload.bin'
[IO.File]::WriteAllText($payload, 'known fixture bytes')
$digest = (Get-FileHash -LiteralPath $payload -Algorithm SHA256).Hash
$script:downloadFiles = [Collections.Generic.List[string]]::new()
$script:requests = 0
$script:corrupt = $false
$script:networkFailure = $false
$script:passed = 0
function Assert($Condition, $Message) {
    if (!$Condition) { throw $Message }
    $script:passed++
}
function Assert-Throws([scriptblock]$Action, [string]$Expected) {
    $message = $null
    try { &$Action | Out-Null } catch { $message = $_.Exception.Message }
    Assert ($message -and $message.Contains($Expected)) "Expected '$Expected', got '$message'"
}
function Write-Lock($Url = 'https://github.com/example/release/payload.zip', $Hash = $digest) {
    @{component = @{url = $Url; sha256 = $Hash; version = 'fixture'}} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $lockPath
}
function Invoke-WebRequest($Uri, $OutFile, [switch]$UseBasicParsing) {
    $script:requests++
    $script:downloadFiles.Add($OutFile)
    if ($script:networkFailure) { throw 'Simulated network failure' }
    if ($script:corrupt) { [IO.File]::WriteAllText($OutFile, 'tampered bytes') }
    else { Copy-Item -LiteralPath $payload -Destination $OutFile }
}
try {
    Write-Lock
    $verified = Get-TrustedDownload 'component' $lockPath
    Assert ((Get-FileHash -LiteralPath $verified).Hash -eq $digest) 'Verified payload changed'
    $second = Get-TrustedDownload 'component' $lockPath
    Assert ($verified -ne $second) 'Downloads share a writable staging path'
    $script:corrupt = $true
    Assert-Throws {Get-TrustedDownload 'component' $lockPath} 'integrity check failed'
    $script:corrupt = $false
    $script:networkFailure = $true
    Assert-Throws {Get-TrustedDownload 'component' $lockPath} 'Simulated network failure'
    $script:networkFailure = $false
    $before = $script:requests
    Assert-Throws {Get-TrustedDownload 'unknown' $lockPath} 'No reviewed download digest'
    Write-Lock -Hash 'bad'
    Assert-Throws {Get-TrustedDownload 'component' $lockPath} 'No reviewed download digest'
    foreach ($url in @('http://github.com/file.zip', 'https://example.com/file.zip',
        'https://user@github.com/file.zip', 'https://github.com:444/file.zip',
        'https://github.com/', '/relative/file.zip')) {
        Write-Lock -Url $url
        Assert-Throws {Get-TrustedDownload 'component' $lockPath} 'approved HTTPS host'
    }
    Assert ($script:requests -eq $before) 'Untrusted metadata triggered a download'
    Set-Content -LiteralPath $lockPath -Value 'invalid json'
    Assert-Throws {Get-TrustedDownload 'component' $lockPath} 'Invalid JSON'
    Write-Output "Download checks: $script:passed passed. No network requests or installers executed."
} finally {
    foreach ($file in $script:downloadFiles) {
        if (Test-Path -LiteralPath $file) { Remove-Item -LiteralPath $file }
        # Only remove the known, now-empty staging directory; never recurse.
        Remove-Item -LiteralPath (Split-Path $file -Parent)
    }
    Remove-Item -LiteralPath $lockPath, $payload -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $fixtureDirectory
}
