# install-host.ps1 — Full setup for Foundry Browser Control
# Installs prerequisites, builds the host, and registers native messaging.
param(
    [string]$HostPath = "",
    [string]$ExtensionId = "",
    [string]$Model = "phi-4-mini",
    [switch]$SkipFoundryInstall,
    [switch]$SkipModelDownload,
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"

$hostName = "com.foundry.browsercontrol"
$manifestDir = Join-Path $env:LOCALAPPDATA "FoundryBrowserControl"
$projectDir = Join-Path $PSScriptRoot "..\src\FoundryBrowserControl.Host"

Write-Host ""
Write-Host "=== Foundry Browser Control — Setup ===" -ForegroundColor Cyan
Write-Host ""

# -------------------------------------------------------
# Step 1: Check and install Foundry Local
# -------------------------------------------------------
if (-not $SkipFoundryInstall) {
    Write-Host "[1/5] Checking Foundry Local..." -ForegroundColor Yellow

    $foundryCmd = Get-Command "foundry" -ErrorAction SilentlyContinue
    if ($foundryCmd) {
        Write-Host "  ✓ Foundry Local is already installed: $($foundryCmd.Source)" -ForegroundColor Green
    } else {
        Write-Host "  Foundry Local not found. Installing via winget..." -ForegroundColor Yellow

        $wingetCmd = Get-Command "winget" -ErrorAction SilentlyContinue
        if (-not $wingetCmd) {
            Write-Error "winget is not available. Please install Foundry Local manually: https://github.com/microsoft/Foundry-Local"
            exit 1
        }

        winget install Microsoft.FoundryLocal --accept-source-agreements --accept-package-agreements
        if ($LASTEXITCODE -ne 0) {
            Write-Error "Failed to install Foundry Local via winget. Please install manually: https://github.com/microsoft/Foundry-Local"
            exit 1
        }

        # Refresh PATH so foundry command is available
        $env:Path = [System.Environment]::GetEnvironmentVariable("Path", "Machine") + ";" + [System.Environment]::GetEnvironmentVariable("Path", "User")

        $foundryCmd = Get-Command "foundry" -ErrorAction SilentlyContinue
        if (-not $foundryCmd) {
            Write-Host "  ⚠ Foundry Local was installed but 'foundry' is not in PATH yet." -ForegroundColor Yellow
            Write-Host "    You may need to restart your terminal, then re-run this script with -SkipFoundryInstall." -ForegroundColor Yellow
            exit 1
        }

        Write-Host "  ✓ Foundry Local installed successfully" -ForegroundColor Green
    }
} else {
    Write-Host "[1/5] Skipping Foundry Local install check (--SkipFoundryInstall)" -ForegroundColor DarkGray
}

# -------------------------------------------------------
# Step 2: Check and download the LLM model
# -------------------------------------------------------
if (-not $SkipModelDownload) {
    Write-Host "[2/5] Checking model '$Model'..." -ForegroundColor Yellow

    $foundryCmd = Get-Command "foundry" -ErrorAction SilentlyContinue
    if (-not $foundryCmd) {
        Write-Host "  ⚠ Foundry Local not in PATH. Skipping model check." -ForegroundColor Yellow
        Write-Host "    After restarting your terminal, run: foundry model run $Model" -ForegroundColor Yellow
    } else {
        # Check if the model is already cached
        $modelList = & foundry model list 2>&1 | Out-String
        if ($modelList -match [regex]::Escape($Model)) {
            Write-Host "  ✓ Model '$Model' is available in the catalog" -ForegroundColor Green
        } else {
            Write-Host "  Model '$Model' not found in catalog listing" -ForegroundColor Yellow
        }

        # Ensure the service is running and the model is loaded
        Write-Host "  Starting Foundry Local service and loading model (this may download the model on first run)..." -ForegroundColor Yellow
        Write-Host "  This can take several minutes for large models." -ForegroundColor DarkGray

        # Start the service if not running
        $serviceStatus = & foundry service status 2>&1 | Out-String
        if ($serviceStatus -match "not running" -or $serviceStatus -match "error") {
            & foundry service start 2>&1 | Out-Null
            Start-Sleep -Seconds 3
        }

        # Pull/cache the model (foundry model run will download if needed, but we just want to cache it)
        try {
            & foundry cache model $Model 2>&1 | ForEach-Object { Write-Host "    $_" -ForegroundColor DarkGray }
            Write-Host "  ✓ Model '$Model' is ready" -ForegroundColor Green
        } catch {
            Write-Host "  ⚠ Could not cache model automatically. Run manually: foundry model run $Model" -ForegroundColor Yellow
        }
    }
} else {
    Write-Host "[2/5] Skipping model download (--SkipModelDownload)" -ForegroundColor DarkGray
}

# -------------------------------------------------------
# Step 3: Check .NET SDK and build the host
# -------------------------------------------------------
if (-not $SkipBuild) {
    Write-Host "[3/5] Building C# native messaging host..." -ForegroundColor Yellow

    $dotnetCmd = Get-Command "dotnet" -ErrorAction SilentlyContinue
    if (-not $dotnetCmd) {
        Write-Error ".NET SDK not found. Please install .NET 8 SDK: https://dotnet.microsoft.com/download/dotnet/8.0"
        exit 1
    }

    $sdkVersion = & dotnet --version 2>&1
    Write-Host "  .NET SDK version: $sdkVersion" -ForegroundColor DarkGray

    Push-Location $projectDir
    try {
        & dotnet build -c Debug --nologo -v quiet 2>&1 | ForEach-Object { Write-Host "    $_" -ForegroundColor DarkGray }
        if ($LASTEXITCODE -ne 0) {
            Write-Error "Build failed. Check the output above for errors."
            exit 1
        }
        Write-Host "  ✓ Build succeeded" -ForegroundColor Green
    } finally {
        Pop-Location
    }
} else {
    Write-Host "[3/5] Skipping build (--SkipBuild)" -ForegroundColor DarkGray
}

# -------------------------------------------------------
# Step 4: Locate the host executable
# -------------------------------------------------------
Write-Host "[4/5] Locating host executable..." -ForegroundColor Yellow

if (-not $HostPath) {
    $publishDir = Join-Path $projectDir "bin\Release\net8.0\win-x64\publish"
    $debugDir = Join-Path $projectDir "bin\Debug\net8.0"

    if (Test-Path (Join-Path $publishDir "FoundryBrowserControl.Host.exe")) {
        $HostPath = Join-Path $publishDir "FoundryBrowserControl.Host.exe"
    } elseif (Test-Path (Join-Path $debugDir "FoundryBrowserControl.Host.exe")) {
        $HostPath = Join-Path $debugDir "FoundryBrowserControl.Host.exe"
    } else {
        Write-Error "Host executable not found. Build failed or project structure is incorrect."
        exit 1
    }
}

$HostPath = (Resolve-Path $HostPath).Path
Write-Host "  ✓ Host: $HostPath" -ForegroundColor Green

# -------------------------------------------------------
# Step 5: Register native messaging host
# -------------------------------------------------------
Write-Host "[5/5] Registering native messaging host..." -ForegroundColor Yellow

# Build allowed_origins
$allowedOrigins = @()
if ($ExtensionId) {
    $allowedOrigins += "chrome-extension://$ExtensionId/"
} else {
    Write-Host ""
    Write-Host "  NOTE: No extension ID provided. Using wildcard for development." -ForegroundColor Yellow
    Write-Host "  For production, re-run with: .\install-host.ps1 -ExtensionId <your-extension-id> -SkipFoundryInstall -SkipModelDownload -SkipBuild" -ForegroundColor Yellow
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
Write-Host "  Manifest: $manifestPath" -ForegroundColor DarkGray

# Register in Windows Registry for Edge
$regPath = "HKCU:\Software\Microsoft\Edge\NativeMessagingHosts\$hostName"
if (-not (Test-Path $regPath)) {
    New-Item -Path $regPath -Force | Out-Null
}
Set-ItemProperty -Path $regPath -Name "(Default)" -Value $manifestPath
Write-Host "  Edge registry: $regPath" -ForegroundColor DarkGray

# Also register for Chrome (in case user wants to use Chrome too)
$chromeRegPath = "HKCU:\Software\Google\Chrome\NativeMessagingHosts\$hostName"
if (-not (Test-Path $chromeRegPath)) {
    New-Item -Path $chromeRegPath -Force | Out-Null
}
Set-ItemProperty -Path $chromeRegPath -Name "(Default)" -Value $manifestPath
Write-Host "  Chrome registry: $chromeRegPath" -ForegroundColor DarkGray

Write-Host "  ✓ Native messaging host registered" -ForegroundColor Green

# -------------------------------------------------------
# Summary
# -------------------------------------------------------
Write-Host ""
Write-Host "=== Setup Complete ===" -ForegroundColor Cyan
Write-Host ""
Write-Host "  Host:     $hostName" -ForegroundColor White
Write-Host "  Exe:      $HostPath" -ForegroundColor White
Write-Host "  Manifest: $manifestPath" -ForegroundColor White
Write-Host "  Model:    $Model" -ForegroundColor White
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "  1. Start the model:  foundry model run $Model" -ForegroundColor White
Write-Host "  2. Load the extension in Edge:" -ForegroundColor White
Write-Host "     - Open edge://extensions" -ForegroundColor White
Write-Host "     - Enable Developer mode" -ForegroundColor White
Write-Host "     - Click 'Load unpacked' and select the 'extension/' folder" -ForegroundColor White
if (-not $ExtensionId) {
    Write-Host "  3. Copy the Extension ID and re-run:" -ForegroundColor White
    Write-Host "     .\install-host.ps1 -ExtensionId <id> -SkipFoundryInstall -SkipModelDownload -SkipBuild" -ForegroundColor White
}
Write-Host ""
