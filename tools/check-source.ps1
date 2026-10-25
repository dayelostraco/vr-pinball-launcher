$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    $files = @(git ls-files --cached --others --exclude-standard | Sort-Object -Unique)
    if ($LASTEXITCODE -ne 0) { throw 'Cannot enumerate repository source.' }
    $checked = 0
    foreach ($relative in $files) {
        $path = Join-Path $root $relative
        if (!(Test-Path -LiteralPath $path -PathType Leaf)) { continue }
        if ([IO.Path]::GetExtension($path) -in @('.ps1', '.psm1', '.psd1')) {
            $tokens = $null
            $errors = $null
            $null = [Management.Automation.Language.Parser]::ParseFile($path, [ref]$tokens, [ref]$errors)
            if ($errors.Count) { throw "PowerShell parse errors in ${relative}: $($errors.Message -join '; ')" }
            $checked++
        }
        if ([IO.Path]::GetExtension($path) -eq '.json') {
            $null = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json -ErrorAction Stop
            $checked++
        }
    }
    Write-Output "Source syntax: $checked PowerShell/JSON files passed."
} finally { Pop-Location }
