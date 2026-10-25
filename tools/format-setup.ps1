# Optional contributor tool. Install-Module PSScriptAnalyzer -RequiredVersion 1.25.0 -Scope CurrentUser
param([switch]$Check)
$ErrorActionPreference = 'Stop'
Import-Module PSScriptAnalyzer -RequiredVersion 1.25.0
$settings = Import-PowerShellDataFile (Join-Path (Get-Module PSScriptAnalyzer).ModuleBase 'Settings\CodeFormatting.psd1')
$settings.Rules.PSPlaceOpenBrace.IgnoreOneLineBlock = $false
$settings.Rules.PSPlaceCloseBrace.IgnoreOneLineBlock = $false
$settings.Rules.PSPlaceCloseBrace.NoEmptyLineBefore = $true
$settings.Rules.PSAlignAssignmentStatement.Enable = $false
$settings.Rules.PSUseConsistentWhitespace.IgnoreAssignmentOperatorInsideHashTable = $false
foreach ($name in @('setup.ps1','SetupCore.psm1','SetupPaths.ps1','SetupBackup.ps1','SetupBackupPage.ps1')) {
    $path = Join-Path $PSScriptRoot $name
    $original = [IO.File]::ReadAllText($path)
    $tokens = $null
    $errors = $null
    $null = [Management.Automation.Language.Parser]::ParseInput($original, [ref]$tokens, [ref]$errors)
    if ($errors.Count) { throw "Cannot format invalid script: $name" }
    $depth = 0
    $offsets = [Collections.Generic.List[int]]::new()
    foreach ($token in $tokens) {
        if ($token.Kind -in @('LParen','AtParen','DollarParen')) { $depth++ }
        if ($token.Kind -eq 'RParen') { $depth-- }
        # A semicolon in a for(...) header remains a semicolon.
        if ($token.Kind -eq 'Semi' -and $depth -eq 0) { $offsets.Add($token.Extent.StartOffset) }
    }
    $formatted = $original
    foreach ($offset in ($offsets | Sort-Object -Descending)) {
        $formatted = $formatted.Remove($offset, 1).Insert($offset, "`n")
    }
    # Semicolon replacement preserves offsets; expand hashtable openings too.
    foreach ($token in ($tokens | Where-Object Kind -eq 'AtCurly' | Sort-Object { $_.Extent.EndOffset } -Descending)) {
        $offset = $token.Extent.EndOffset
        if ($formatted.Substring($offset) -notmatch '^[ \t]*\r?\n') {
            $formatted = $formatted.Insert($offset, "`n")
        }
    }
    $formatted = Invoke-Formatter -ScriptDefinition $formatted.Replace("`r`n", "`n") -Settings $settings
    $formatted = [regex]::Replace($formatted.Replace("`r`n", "`n"), '(?m)[ \t]+$', '').TrimEnd() + "`n"
    if ($Check) {
        if ($original.Replace("`r`n", "`n") -cne $formatted) { throw "Run tools/format-setup.ps1: $name" }
    } else { [IO.File]::WriteAllText($path, $formatted, [Text.UTF8Encoding]::new($false)) }
}
