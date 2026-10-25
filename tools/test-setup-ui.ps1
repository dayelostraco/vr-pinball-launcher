$ErrorActionPreference='Stop'
$root=Join-Path ([IO.Path]::GetTempPath()) ('vpx-setup-ui-'+[Guid]::NewGuid().ToString('N'))
$tables=Join-Path $root 'Tables';New-Item -ItemType Directory -Force $root,$tables | Out-Null
$exe=Join-Path $root 'VPinballX_SETUP_TEST.exe';$source=Join-Path $root 'Fixture.cs'
[IO.File]::WriteAllText($source, @'
internal static class TestApp {
    static int Main(string[] args) {
        string log = System.IO.Path.Combine(System.Environment.GetEnvironmentVariable("APPDATA"), "VPinballX", "10.8", "vpinball.log");
        // Simulate log rotation, an unrelated successful table, then the selected
        // table failing. None of these fixture bytes come from a real VPX run.
        System.IO.File.WriteAllText(log, "LoadGameFromFilename unrelated.vpx\nStartup done\nLoadGameFromFilename " + args[1] + "\nOnScriptError fixture\n");
        System.Threading.Thread.Sleep(700);
        return 42;
    }
}
'@)
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:winexe "/out:$exe" $source
if($LASTEXITCODE -ne 0){throw 'UI fixture compile failed'}
[IO.File]::WriteAllText((Join-Path $tables 'Setup UI Test.vpx'),'A dummy fixture; never opened by VPX.')
$config=[ordered]@{vpinballExecutable=$exe;tablesDirectory=$tables;searchSubdirectories=$true}
[IO.File]::WriteAllText((Join-Path $root 'launcher-config.json'),($config|ConvertTo-Json))
$before=(Get-FileHash -LiteralPath (Join-Path $root 'launcher-config.json')).Hash
$originalAppData = $env:APPDATA
try {
    $env:APPDATA = Join-Path $root 'Roaming'
    $logDirectory = Join-Path $env:APPDATA 'VPinballX\10.8'
    New-Item -ItemType Directory -Force -Path $logDirectory | Out-Null
    [IO.File]::WriteAllText((Join-Path $logDirectory 'vpinball.log'), ('old log' + (' ' * 4096)))
    & (Join-Path $PSScriptRoot 'setup.ps1') -LauncherDirectory $root -SmokeTest
}
finally { $env:APPDATA = $originalAppData }
if((Get-FileHash -LiteralPath (Join-Path $root 'launcher-config.json')).Hash -ne $before){throw 'UI test modified configuration without Apply'}
Write-Output "PASS: configuration untouched. UI fixtures: $root"
