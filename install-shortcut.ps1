# Creates the "Brightness Tray" shortcut in the user's Start Menu Programs folder

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$exePath = Join-Path $scriptDir "bin\BrightnessTray.exe"

if (!(Test-Path $exePath)) {
    Write-Warning "BrightnessTray.exe not found. Running build.ps1 first..."
    & (Join-Path $scriptDir "build.ps1")
}

$startMenuDir = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs"
$shortcutPath = Join-Path $startMenuDir "Brightness Tray.lnk"

$wsh = New-Object -ComObject WScript.Shell
$shortcut = $wsh.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $exePath
$shortcut.WorkingDirectory = Split-Path -Parent $exePath
$shortcut.Description = "Brightness Tray - Monitor brightness and input switching"
$shortcut.IconLocation = "$exePath,0"
$shortcut.Save()

Write-Host "Start menu shortcut installed successfully:" -ForegroundColor Green
Write-Host "  $shortcutPath"
Write-Host "Points to: $exePath"
