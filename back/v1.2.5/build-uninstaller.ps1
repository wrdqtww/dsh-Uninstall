# Build the standalone DSH uninstaller (C# / .NET Framework 4.x) and embed
# the DSH uninstaller icon. Produces build/Uninstall_DSH_Desktop.exe.
#
# Usage (from repo root): pwsh -File .\build-uninstaller.ps1

$ErrorActionPreference = 'Stop'

$root     = $PSScriptRoot
$outDir   = Join-Path $root 'build'
$icon     = Join-Path $root 'Uninstall_DSH_Desktop_icon.ico'
$tmpOut   = Join-Path $outDir 'Uninstall_DSH_Desktop.new.exe'
$finalOut = Join-Path $outDir 'Uninstall_DSH_Desktop.exe'

# Compile every .cs at repo root so future modules can be added without
# editing this script again.
$srcFiles = @(Get-ChildItem -LiteralPath $root -Filter '*.cs' | Sort-Object Name | ForEach-Object { $_.FullName })
if ($srcFiles.Count -eq 0) { throw "No C# source files found under $root" }
if (-not (Test-Path -LiteralPath $icon)) { throw "Missing icon: $icon" }

New-Item -ItemType Directory -Force -Path $outDir | Out-Null

# Locate the .NET Framework 4.x C# compiler (x64 first, then x86).
$cscCandidates = @(
    (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'),
    (Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe')
)
$csc = $cscCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $csc) { throw 'csc.exe (.NET Framework 4.x) not found' }
$fwDir = Split-Path -Parent $csc

$args = @(
    '/nologo',
    '/target:winexe',
    "/out:$tmpOut",
    "/r:$fwDir\System.Windows.Forms.dll",
    "/r:$fwDir\System.Drawing.dll",
    "/r:$fwDir\System.Management.dll"
) + $srcFiles

Write-Host "Compiling: $csc" -ForegroundColor Cyan
& $csc @args
if ($LASTEXITCODE -ne 0) { throw "csc failed with exit code $LASTEXITCODE" }

Write-Host 'Embedding icon...' -ForegroundColor Cyan
& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'embed-icon-in-exe.ps1') -ExePath $tmpOut -IconPath $icon
if ($LASTEXITCODE -ne 0) {
    Remove-Item -LiteralPath $tmpOut -Force -ErrorAction SilentlyContinue
    throw "Icon embedding failed with exit code $LASTEXITCODE"
}

Move-Item -LiteralPath $tmpOut -Destination $finalOut -Force
Write-Host "Built: $finalOut" -ForegroundColor Green