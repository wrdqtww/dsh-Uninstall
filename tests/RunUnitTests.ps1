# Compile and run the pure-logic unit tests for the uninstaller helpers.
param([string]$Version = '')

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$tests = Join-Path $root 'tests'
$core = Join-Path $root 'DSH_Desktop_Uninstaller.Core.cs'
$unit = Join-Path $tests 'UnitTests.cs'
$outExe = Join-Path $tests 'UnitTests.exe'

$fw = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$csc = Join-Path $fw 'csc.exe'
if (-not (Test-Path -LiteralPath $csc)) {
    $fw32 = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'
    $csc = Join-Path $fw32 'csc.exe'
}
if (-not (Test-Path -LiteralPath $csc)) { throw "csc.exe not found under $env:WINDIR\Microsoft.NET" }

if (Test-Path -LiteralPath $outExe) { Remove-Item -LiteralPath $outExe -Force }

& $csc /nologo /target:exe "/out:$outExe" $core $unit
if ($LASTEXITCODE -ne 0) { throw "Unit test compilation failed (exit $LASTEXITCODE)" }

$p = Start-Process -FilePath $outExe -Wait -PassThru -NoNewWindow
if ($p.ExitCode -ne 0) { throw "Unit tests failed (exit $($p.ExitCode))" }
Write-Host "Unit tests passed." -ForegroundColor Green
