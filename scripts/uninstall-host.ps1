# uninstall-host.ps1 — Unregister the Foundry Browser Control native messaging host

$ErrorActionPreference = "Stop"

$hostName = "com.foundry.browsercontrol"
$manifestDir = Join-Path $env:LOCALAPPDATA "FoundryBrowserControl"
$manifestPath = Join-Path $manifestDir "$hostName.json"

# Remove registry keys
$edgeRegPath = "HKCU:\Software\Microsoft\Edge\NativeMessagingHosts\$hostName"
if (Test-Path $edgeRegPath) {
    Remove-Item -Path $edgeRegPath -Force
    Write-Host "Removed Edge registry key: $edgeRegPath"
} else {
    Write-Host "Edge registry key not found (already removed)"
}

$chromeRegPath = "HKCU:\Software\Google\Chrome\NativeMessagingHosts\$hostName"
if (Test-Path $chromeRegPath) {
    Remove-Item -Path $chromeRegPath -Force
    Write-Host "Removed Chrome registry key: $chromeRegPath"
} else {
    Write-Host "Chrome registry key not found (already removed)"
}

# Remove manifest file
if (Test-Path $manifestPath) {
    Remove-Item -Path $manifestPath -Force
    Write-Host "Removed manifest: $manifestPath"
} else {
    Write-Host "Manifest not found (already removed)"
}

Write-Host ""
Write-Host "✅ Native messaging host uninstalled successfully!" -ForegroundColor Green
