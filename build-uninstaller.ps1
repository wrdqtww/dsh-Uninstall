# Build the standalone DSH uninstaller from the canonical source folder.
# Compiles every .cs in this directory with the .NET Framework 4.x compiler,
# embeds the icon, and writes build\Uninstall_DSH_Desktop.exe.
#
# Usage: pwsh -File build-uninstaller.ps1

param([string]$Version = '')

$ErrorActionPreference = 'Stop'

$root    = Split-Path -Parent $MyInvocation.MyCommand.Path
$outDir  = Join-Path $root 'build'
$icon    = Join-Path $root 'Uninstall_DSH_Desktop_icon.ico'
$tmpOut  = Join-Path $outDir 'Uninstall_DSH_Desktop.new.exe'
$finalOut = Join-Path $outDir 'Uninstall_DSH_Desktop.exe'

$mainCs = Join-Path $root 'DSH_Desktop_Uninstaller.cs'
$mainText = [IO.File]::ReadAllText($mainCs)
$verMatch = [regex]::Match($mainText, 'static\s+readonly\s+string\s+UninstallerVersion\s*=\s*"([^"]+)"')
if (-not $verMatch.Success) { throw 'UninstallerVersion constant not found in DSH_Desktop_Uninstaller.cs' }
$srcVersion = $verMatch.Groups[1].Value
if ($Version -and ($Version -ne $srcVersion)) { throw "Requested version $Version does not match source UninstallerVersion $srcVersion" }
Write-Host "Uninstaller version: $srcVersion" -ForegroundColor Cyan

$srcFiles = @(Get-ChildItem -LiteralPath $root -Filter '*.cs' | Sort-Object Name | ForEach-Object { $_.FullName })

# Generate VersionInfo.cs so the compiled exe carries a FileVersion/ProductVersion resource.
$verParts = ($srcVersion -split '\.')
while ($verParts.Count -lt 4) { $verParts += '0' }
$fileVersion = ($verParts | Select-Object -First 4) -join '.'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$versionInfoCs = Join-Path $outDir 'VersionInfo.cs'
$versionInfoText = @('[assembly: System.Reflection.AssemblyVersion("' + $fileVersion + '")]','[assembly: System.Reflection.AssemblyFileVersion("' + $fileVersion + '")]') -join [Environment]::NewLine
[IO.File]::WriteAllText($versionInfoCs, $versionInfoText)
$srcFiles = @($srcFiles) + @($versionInfoCs)
if ($srcFiles.Count -eq 0) { throw "No C# source files found under $root" }
if (-not (Test-Path -LiteralPath $icon)) { throw "Missing icon: $icon" }


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
    '/codepage:65001',
    "/out:$tmpOut",
    "/r:$fwDir\System.Windows.Forms.dll",
    "/r:$fwDir\System.Drawing.dll",
    "/r:$fwDir\System.Management.dll",
    "/r:$fwDir\System.Web.Extensions.dll"
) + $srcFiles

Write-Host "Compiling: $csc" -ForegroundColor Cyan
& $csc @args
if ($LASTEXITCODE -ne 0) { throw "csc failed with exit code $LASTEXITCODE" }

  Write-Host 'Running unit tests...' -ForegroundColor Cyan
  & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'tests\RunUnitTests.ps1')
  if ($LASTEXITCODE -ne 0) { throw "Unit tests failed with exit code $LASTEXITCODE" }

Write-Host 'Embedding icon...' -ForegroundColor Cyan
& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'embed-icon-in-exe.ps1') -ExePath $tmpOut -IconPath $icon
if ($LASTEXITCODE -ne 0) {
    Remove-Item -LiteralPath $tmpOut -Force -ErrorAction SilentlyContinue
    throw "Icon embedding failed with exit code $LASTEXITCODE"
}

Move-Item -LiteralPath $tmpOut -Destination $finalOut -Force
Write-Host "Built: $finalOut" -ForegroundColor Green
