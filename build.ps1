param(
  [ValidateSet('Release','Debug')][string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

Write-Host "Building ($Configuration)..."
dotnet build .\FTMapScreenSaver.csproj -c $Configuration

$tfm = 'net8.0-windows'
$outDir = Join-Path $root "bin\$Configuration\$tfm"
$exe = Join-Path $outDir 'FTMapScreenSaver.exe'
if (-not (Test-Path $exe)) { throw "Build output not found: $exe" }

$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Force -Path $dist | Out-Null
$scr = Join-Path $dist 'FTMapScreenSaver.scr'
Copy-Item -Force $exe $scr

Write-Host "Created: $scr"
Write-Host "Run fullscreen: $scr /s"
