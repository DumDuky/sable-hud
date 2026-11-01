# Sable HUD build script
# Requires the .NET 8 SDK (x64). The repository's local SDK path is used when present.
param(
    [switch]$SkipSelfContained,
    [switch]$SkipFrameworkDependent
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $root 'src\SableHUD\SableHUD.csproj'
$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Force -Path $dist | Out-Null

$localDotnet = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet8\dotnet.exe'
$dotnet = if (Test-Path $localDotnet) { $localDotnet } else { 'dotnet' }

Write-Host "Using $dotnet"

if (-not $SkipSelfContained) {
    $out = Join-Path $dist 'portable'
    if (Test-Path $out) { Remove-Item $out -Recurse -Force }
    & $dotnet publish $project -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        -p:DebugType=None `
        -o $out
    if ($LASTEXITCODE -ne 0) { throw 'self-contained publish failed' }
    Copy-Item (Join-Path $out 'SableHUD.exe') (Join-Path $dist 'SableHUD.exe') -Force
    Remove-Item $out -Recurse -Force
    Write-Host "Portable exe: $(Join-Path $dist 'SableHUD.exe')"
}

if (-not $SkipFrameworkDependent) {
    $out = Join-Path $dist 'framework-dependent'
    if (Test-Path $out) { Remove-Item $out -Recurse -Force }
    & $dotnet publish $project -c Release -r win-x64 --self-contained false `
        -p:PublishSingleFile=true `
        -p:DebugType=None `
        -o $out
    if ($LASTEXITCODE -ne 0) { throw 'framework-dependent publish failed' }
    Write-Host "Framework-dependent exe: $(Join-Path $out 'SableHUD.exe')"
}

Copy-Item (Join-Path $root 'README.md') (Join-Path $dist 'README.md') -Force
Copy-Item (Join-Path $root 'LICENSE') (Join-Path $dist 'LICENSE') -Force
Copy-Item (Join-Path $root 'NOTICE') (Join-Path $dist 'NOTICE') -Force
$drivers = Join-Path $dist 'drivers'
New-Item -ItemType Directory -Force -Path $drivers | Out-Null
Copy-Item (Join-Path $root 'vendor\HidHide_1.5.230_x64.exe') $drivers -Force
Copy-Item (Join-Path $root 'vendor\HidHide-LICENSE.txt') $drivers -Force
if (Test-Path (Join-Path $dist 'docs')) { Remove-Item (Join-Path $dist 'docs') -Recurse -Force }
Copy-Item (Join-Path $root 'docs') (Join-Path $dist 'docs') -Recurse -Force

$zip = Join-Path $dist 'SableHUD-win-x64.zip'
if (Test-Path $zip) { Remove-Item $zip -Force }
$stage = Join-Path $dist 'package'
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force -Path $stage | Out-Null
Copy-Item (Join-Path $dist 'SableHUD.exe') $stage
Copy-Item (Join-Path $root 'README.md') $stage
Copy-Item (Join-Path $root 'LICENSE') $stage
Copy-Item (Join-Path $root 'NOTICE') $stage
Copy-Item (Join-Path $root 'install.ps1') $stage
Copy-Item (Join-Path $root 'uninstall.ps1') $stage
Copy-Item (Join-Path $root 'docs') $stage -Recurse
Copy-Item (Join-Path $dist 'drivers') $stage -Recurse
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -Force
Remove-Item $stage -Recurse -Force
Write-Host "Zip: $zip"
