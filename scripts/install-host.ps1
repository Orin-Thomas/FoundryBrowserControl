# install-host.ps1 — Register the Foundry Browser Control native messaging host for Microsoft Edge
param(
    [string]$HostPath = "",
    [string]$ExtensionId = ""
)

$ErrorActionPreference = "Stop"

$hostName = "com.foundry.browsercontrol"
$manifestDir = Join-Path $env:LOCALAPPDATA "FoundryBrowserControl"

# Find the host executable
if (-not $HostPath) {
    # Try to find the built executable
    $projectDir = Join-Path $PSScriptRoot "..\src\FoundryBrowserControl.Host"
    $publishDir = Join-Path $projectDir "bin\Release\net8.0\win-x64\publish"
    $debugDir = Join-Path $projectDir "bin\Debug\net8.0"

    if (Test-Path (Join-Path $publishDir "FoundryBrowserControl.Host.exe")) {
        $HostPath = Join-Path $publishDir "FoundryBrowserControl.Host.exe"
    } elseif (Test-Path (Join-Path $debugDir "FoundryBrowserControl.Host.exe")) {
        $HostPath = Join-Path $debugDir "FoundryBrowserControl.Host.exe"
    } else {
        Write-Error "Host executable not found. Build the project first: dotnet build -c Debug"
        exit 1
    }
}

$HostPath = (Resolve-Path $HostPath).Path
Write-Host "Using host path: $HostPath"

# Build allowed_origins
$allowedOrigins = @()
if ($ExtensionId) {
    $allowedOrigins += "chrome-extension://$ExtensionId/"
} else {
    Write-Host ""
    Write-Host "NOTE: You need to provide the extension ID after loading it in Edge."
    Write-Host "  1. Load the extension in Edge (edge://extensions, Developer mode, Load unpacked)"
    Write-Host "  2. Copy the extension ID"
    Write-Host "  3. Re-run: .\install-host.ps1 -ExtensionId <your-extension-id>"
    Write-Host ""
    Write-Host "For now, installing with a wildcard origin for development..."
    $allowedOrigins += "chrome-extension://*/"
}

# Create the native messaging host manifest
$manifest = @{
    name = $hostName
    description = "Foundry Browser Control - AI-powered browser automation"
    path = $HostPath
    type = "stdio"
    allowed_origins = $allowedOrigins
} | ConvertTo-Json -Depth 10

# Write manifest file
if (-not (Test-Path $manifestDir)) {
    New-Item -ItemType Directory -Path $manifestDir -Force | Out-Null
}

$manifestPath = Join-Path $manifestDir "$hostName.json"
$manifest | Out-File -FilePath $manifestPath -Encoding UTF8 -Force
Write-Host "Manifest written to: $manifestPath"

# Register in Windows Registry for Edge
$regPath = "HKCU:\Software\Microsoft\Edge\NativeMessagingHosts\$hostName"
if (-not (Test-Path $regPath)) {
    New-Item -Path $regPath -Force | Out-Null
}
Set-ItemProperty -Path $regPath -Name "(Default)" -Value $manifestPath
Write-Host "Registry key created: $regPath"

# Also register for Chrome (in case user wants to use Chrome too)
$chromeRegPath = "HKCU:\Software\Google\Chrome\NativeMessagingHosts\$hostName"
if (-not (Test-Path $chromeRegPath)) {
    New-Item -Path $chromeRegPath -Force | Out-Null
}
Set-ItemProperty -Path $chromeRegPath -Name "(Default)" -Value $manifestPath
Write-Host "Chrome registry key created: $chromeRegPath"

Write-Host ""
Write-Host "✅ Native messaging host installed successfully!" -ForegroundColor Green
Write-Host "   Host: $hostName"
Write-Host "   Path: $HostPath"
Write-Host "   Manifest: $manifestPath"
