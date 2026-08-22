# Smoke test for the built uninstaller exe.
# Runs /help and /DryRun and checks exit codes + log output.
# Usage: pwsh -File tests\run-smoke.ps1

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $root 'build\Uninstall_DSH_Desktop.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw "Missing $exe" }

$log = Join-Path $root 'build\smoke.log'
Remove-Item -LiteralPath $log -ErrorAction SilentlyContinue

$p1 = Start-Process -FilePath $exe -ArgumentList '/help' -PassThru -Wait
Write-Host "/help exit: $($p1.ExitCode)"
if ($p1.ExitCode -ne 0) { throw '/help failed' }

$p2 = Start-Process -FilePath $exe -ArgumentList "/DryRun /Log=$log" -PassThru -Wait
Write-Host "/DryRun exit: $($p2.ExitCode)"
if ($p2.ExitCode -ne 0) { throw '/DryRun failed' }

if (-not (Test-Path -LiteralPath $log)) { throw 'Log.log was not created' }
$content = [IO.File]::ReadAllText($log)
foreach ($needle in @('Uninstaller version', 'Detected DSH', 'Dry-Run', 'Log file')) {
    if ($content.IndexOf($needle, [StringComparison]::OrdinalIgnoreCase) -lt 0) {
        throw "Log missing expected marker: $needle"
    }
}
Write-Host 'Smoke test PASSED' -ForegroundColor Green
