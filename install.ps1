# Installs Sable HUD for the current user, creates a Start Menu shortcut, and launches it.
# Use Settings -> General -> Start with Windows to enable autostart.
param(
    [switch]$NoLaunch
)

$ErrorActionPreference = 'Stop'
$source = Join-Path $PSScriptRoot 'SableHUD.exe'
if (-not (Test-Path $source)) {
    throw "SableHUD.exe was not found next to install.ps1. Run this from the extracted release folder."
}

$dest = Join-Path $env:LOCALAPPDATA 'SableHUD'
New-Item -ItemType Directory -Force -Path $dest | Out-Null
Copy-Item $source (Join-Path $dest 'SableHUD.exe') -Force
foreach ($extra in 'README.md', 'LICENSE', 'NOTICE') {
    $p = Join-Path $PSScriptRoot $extra
    if (Test-Path $p) { Copy-Item $p (Join-Path $dest $extra) -Force }
}
$drivers = Join-Path $PSScriptRoot 'drivers'
if (Test-Path $drivers) { Copy-Item $drivers (Join-Path $dest 'drivers') -Recurse -Force }

$exe = Join-Path $dest 'SableHUD.exe'
$startMenu = [Environment]::GetFolderPath('Programs')
$shortcutPath = Join-Path $startMenu 'Sable HUD.lnk'
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $exe
$shortcut.WorkingDirectory = $dest
$shortcut.IconLocation = "$exe,0"
$shortcut.Description = 'Sable HUD — controller-first gaming overlay'
$shortcut.Save()

Write-Host "Installed to $dest"
Write-Host "Start Menu shortcut: $shortcutPath"
if (-not $NoLaunch) { Start-Process $exe }
