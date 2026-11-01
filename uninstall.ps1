# Removes Sable HUD, its shortcut and its autostart entry. Settings stay unless -RemoveData is passed.
param(
    [switch]$RemoveData
)

$ErrorActionPreference = 'Continue'

Get-Process -Name 'SableHUD' -ErrorAction SilentlyContinue | Stop-Process -Force
Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'" -ErrorAction SilentlyContinue |
    Where-Object { $_.CommandLine -like '*SableHUD.dll*' } |
    ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }

$dest = Join-Path $env:LOCALAPPDATA 'SableHUD'
if (Test-Path $dest) { Remove-Item $dest -Recurse -Force }

$shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'Sable HUD.lnk'
if (Test-Path $shortcut) { Remove-Item $shortcut -Force }

Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'SableHUD' -ErrorAction SilentlyContinue

if ($RemoveData) {
    $data = Join-Path $env:APPDATA 'SableHUD'
    if (Test-Path $data) { Remove-Item $data -Recurse -Force }
    Write-Host 'Settings and logs removed.'
} else {
    Write-Host 'Settings kept in %APPDATA%\SableHUD (use -RemoveData to delete them).'
}

Write-Host 'Sable HUD uninstalled.'
