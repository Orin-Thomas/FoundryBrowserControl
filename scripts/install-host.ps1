# install-host.ps1 - Full setup for Foundry Browser Control
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
Write-Host "=== Foundry Browser Control - Setup ===" -ForegroundColor Cyan
Write-Host ""

# -------------------------------------------------------
# Step 1: Check and install Foundry Local
# -------------------------------------------------------
if (-not $SkipFoundryInstall) {
    Write-Host "[1/7] Checking Foundry Local..." -ForegroundColor Yellow

    $foundryCmd = Get-Command "foundry" -ErrorAction SilentlyContinue
    if ($foundryCmd) {
        Write-Host "  [OK] Foundry Local is already installed: $($foundryCmd.Source)" -ForegroundColor Green
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
            Write-Host "  [WARN] Foundry Local was installed but 'foundry' is not in PATH yet." -ForegroundColor Yellow
            Write-Host "    You may need to restart your terminal, then re-run this script with -SkipFoundryInstall." -ForegroundColor Yellow
            exit 1
        }

        Write-Host "  [OK] Foundry Local installed successfully" -ForegroundColor Green
    }
} else {
    Write-Host "[1/7] Skipping Foundry Local install check (-SkipFoundryInstall)" -ForegroundColor DarkGray
}

# -------------------------------------------------------
# Step 2: Check and download the LLM model
# -------------------------------------------------------
if (-not $SkipModelDownload) {
    Write-Host "[2/7] Checking model '$Model'..." -ForegroundColor Yellow

    $foundryCmd = Get-Command "foundry" -ErrorAction SilentlyContinue
    if (-not $foundryCmd) {
        Write-Host "  [WARN] Foundry Local not in PATH. Skipping model check." -ForegroundColor Yellow
        Write-Host "    After restarting your terminal, run: foundry model run $Model" -ForegroundColor Yellow
    } else {
        # Check if the model is already cached (with timeout)
        Write-Host "  Querying model catalog (timeout: 30s)..." -ForegroundColor DarkGray
        $modelListJob = Start-Job -ScriptBlock { & foundry model list 2>&1 }
        $modelListDone = $modelListJob | Wait-Job -Timeout 30
        if ($modelListDone) {
            $modelList = Receive-Job $modelListJob | Out-String
            Remove-Job $modelListJob -Force
            if ($modelList -match [regex]::Escape($Model)) {
                Write-Host "  [OK] Model '$Model' is available in the catalog" -ForegroundColor Green
            } else {
                Write-Host "  Model '$Model' not found in catalog listing" -ForegroundColor Yellow
            }
        } else {
            Stop-Job $modelListJob -ErrorAction SilentlyContinue
            Remove-Job $modelListJob -Force
            Write-Host "  [WARN] 'foundry model list' timed out. Continuing..." -ForegroundColor Yellow
        }

        # Start the service if not running
        Write-Host "  Starting Foundry Local service..." -ForegroundColor Yellow
        $svcJob = Start-Job -ScriptBlock { & foundry service start 2>&1 }
        $svcDone = $svcJob | Wait-Job -Timeout 30
        if ($svcDone) {
            Receive-Job $svcJob | ForEach-Object { Write-Host "    $_" -ForegroundColor DarkGray }
        } else {
            Stop-Job $svcJob -ErrorAction SilentlyContinue
            Write-Host "  [WARN] 'foundry service start' timed out. Continuing..." -ForegroundColor Yellow
        }
        Remove-Job $svcJob -Force
        Start-Sleep -Seconds 3

        # Pull/cache the model (with timeout - this can take a while for first download)
        Write-Host "  Downloading/caching model '$Model' (timeout: 5 min, first download may be large)..." -ForegroundColor Yellow
        $cacheJob = Start-Job -ScriptBlock { param($m) & foundry cache model $m 2>&1 } -ArgumentList $Model
        $cacheDone = $cacheJob | Wait-Job -Timeout 300
        if ($cacheDone) {
            Receive-Job $cacheJob | ForEach-Object { Write-Host "    $_" -ForegroundColor DarkGray }
            Write-Host "  [OK] Model '$Model' is ready" -ForegroundColor Green
        } else {
            Stop-Job $cacheJob -ErrorAction SilentlyContinue
            Write-Host "  [WARN] Model caching timed out. The download may still be in progress." -ForegroundColor Yellow
            Write-Host "    Run manually after setup: foundry model run $Model" -ForegroundColor Yellow
        }
        Remove-Job $cacheJob -Force

        # Report which variant was selected for the hardware
        $infoJob = Start-Job -ScriptBlock { param($m) & foundry model info $m 2>&1 } -ArgumentList $Model
        $infoDone = $infoJob | Wait-Job -Timeout 15
        if ($infoDone) {
            $modelInfo = Receive-Job $infoJob | Out-String
            Write-Host ""
            Write-Host "  Hardware variant details:" -ForegroundColor DarkGray
            $modelInfo.Trim().Split("`n") | ForEach-Object {
                $line = $_.Trim()
                if ($line -match "(?i)(runtime|device|backend|hardware|accelerat|variant|gpu|cpu|npu|cuda|directml|onnx)") {
                    Write-Host "    $_" -ForegroundColor Cyan
                } elseif ($line) {
                    Write-Host "    $_" -ForegroundColor DarkGray
                }
            }
        } else {
            Stop-Job $infoJob -ErrorAction SilentlyContinue
            Write-Host "  [WARN] 'foundry model info' timed out." -ForegroundColor Yellow
        }
        Remove-Job $infoJob -Force
    }
} else {
    Write-Host "[2/7] Skipping model download (-SkipModelDownload)" -ForegroundColor DarkGray
}

# -------------------------------------------------------
# Step 3: Check .NET SDK and build the host
# -------------------------------------------------------
if (-not $SkipBuild) {
    Write-Host "[3/7] Building C# native messaging host..." -ForegroundColor Yellow

    $dotnetCmd = Get-Command "dotnet" -ErrorAction SilentlyContinue
    if (-not $dotnetCmd) {
        Write-Host "  .NET SDK not found. Attempting to install via winget..." -ForegroundColor Yellow

        $wingetCmd = Get-Command "winget" -ErrorAction SilentlyContinue
        if (-not $wingetCmd) {
            Write-Error "winget is not available. Please install .NET 8 SDK manually: https://dotnet.microsoft.com/download/dotnet/8.0"
            exit 1
        }

        winget install Microsoft.DotNet.SDK.8 --accept-source-agreements --accept-package-agreements
        if ($LASTEXITCODE -ne 0) {
            Write-Error "Failed to install .NET 8 SDK via winget. Please install manually: https://dotnet.microsoft.com/download/dotnet/8.0"
            exit 1
        }

        # Refresh PATH
        $env:Path = [System.Environment]::GetEnvironmentVariable("Path", "Machine") + ";" + [System.Environment]::GetEnvironmentVariable("Path", "User")

        $dotnetCmd = Get-Command "dotnet" -ErrorAction SilentlyContinue
        if (-not $dotnetCmd) {
            Write-Host "  [WARN] .NET 8 SDK was installed but 'dotnet' is not in PATH yet." -ForegroundColor Yellow
            Write-Host "    Restart your terminal, then re-run this script with -SkipFoundryInstall -SkipModelDownload." -ForegroundColor Yellow
            exit 1
        }

        Write-Host "  [OK] .NET 8 SDK installed successfully" -ForegroundColor Green
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
        Write-Host "  [OK] Build succeeded" -ForegroundColor Green
    } finally {
        Pop-Location
    }
} else {
    Write-Host "[3/7] Skipping build (-SkipBuild)" -ForegroundColor DarkGray
}

# -------------------------------------------------------
# Step 4: Locate the host executable
# -------------------------------------------------------
Write-Host "[4/7] Locating host executable..." -ForegroundColor Yellow

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
Write-Host "  [OK] Host: $HostPath" -ForegroundColor Green

# -------------------------------------------------------
# Step 5: Register native messaging host
# -------------------------------------------------------
Write-Host "[5/7] Registering native messaging host..." -ForegroundColor Yellow

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

Write-Host "  [OK] Native messaging host registered" -ForegroundColor Green

# -------------------------------------------------------
# Step 6: Start the model
# -------------------------------------------------------
Write-Host "[6/7] Starting Foundry Local model '$Model'..." -ForegroundColor Yellow

$foundryCmd = Get-Command "foundry" -ErrorAction SilentlyContinue
if (-not $foundryCmd) {
    Write-Host "  [WARN] Foundry CLI not in PATH. Restart terminal and run: foundry model run $Model" -ForegroundColor Yellow
} else {
    # Check if the model is already loaded by querying the service
    $modelAlreadyRunning = $false
    $svcStatusJob = Start-Job -ScriptBlock { & foundry service status 2>&1 }
    $svcStatusDone = $svcStatusJob | Wait-Job -Timeout 15
    if ($svcStatusDone) {
        $serviceOutput = Receive-Job $svcStatusJob | Out-String
    } else {
        Stop-Job $svcStatusJob -ErrorAction SilentlyContinue
        $serviceOutput = ""
        Write-Host "  [WARN] 'foundry service status' timed out." -ForegroundColor Yellow
    }
    Remove-Job $svcStatusJob -Force

    if ($serviceOutput -match "running" -and $serviceOutput -notmatch "not running") {
        $endpointMatch = [regex]::Match($serviceOutput, "https?://[\w\.\-]+:\d+")
        $endpoint = if ($endpointMatch.Success) { $endpointMatch.Value } else { "http://localhost:5273" }
        try {
            $response = Invoke-RestMethod -Uri "$endpoint/v1/models" -Method Get -TimeoutSec 5 -ErrorAction Stop
            $loadedModels = ($response.data | ForEach-Object { $_.id }) -join ", "
            if ($loadedModels -match [regex]::Escape($Model)) {
                $modelAlreadyRunning = $true
                Write-Host "  [OK] Model '$Model' is already running" -ForegroundColor Green
            }
        } catch {
            # Service may not be ready yet, proceed to start
        }
    }

    if (-not $modelAlreadyRunning) {
        Write-Host "  Starting Foundry Local service..." -ForegroundColor Yellow
        $startJob = Start-Job -ScriptBlock { & foundry service start 2>&1 }
        $startDone = $startJob | Wait-Job -Timeout 30
        if ($startDone) {
            Receive-Job $startJob | ForEach-Object { Write-Host "    $_" -ForegroundColor DarkGray }
        } else {
            Stop-Job $startJob -ErrorAction SilentlyContinue
            Write-Host "  [WARN] 'foundry service start' timed out." -ForegroundColor Yellow
        }
        Remove-Job $startJob -Force
        Start-Sleep -Seconds 3

        # Discover the endpoint
        $svcOutJob = Start-Job -ScriptBlock { & foundry service status 2>&1 }
        $svcOutDone = $svcOutJob | Wait-Job -Timeout 15
        $svcOut = ""
        if ($svcOutDone) {
            $svcOut = Receive-Job $svcOutJob | Out-String
        } else {
            Stop-Job $svcOutJob -ErrorAction SilentlyContinue
        }
        Remove-Job $svcOutJob -Force
        $epMatch = [regex]::Match($svcOut, "https?://[\w\.\-]+:\d+")
        $ep = if ($epMatch.Success) { $epMatch.Value } else { "http://localhost:5273" }
        Write-Host "  Service endpoint: $ep" -ForegroundColor DarkGray

        # Trigger model loading by sending a lightweight chat completion request
        Write-Host "  Loading model '$Model' (this may take a minute on first load)..." -ForegroundColor Yellow
        $body = @{
            model = $Model
            messages = @(@{ role = "user"; content = "hi" })
            max_tokens = 1
        } | ConvertTo-Json -Depth 10

        $timeout = 120
        $elapsed = 0
        $modelReady = $false
        while ($elapsed -lt $timeout) {
            try {
                $resp = Invoke-RestMethod -Uri "$ep/v1/chat/completions" -Method Post -Body $body -ContentType "application/json" -TimeoutSec 60 -ErrorAction Stop
                Write-Host "  [OK] Model '$Model' is loaded and responding" -ForegroundColor Green
                $modelReady = $true
                break
            } catch {
                $elapsed += 5
                if ($elapsed -lt $timeout) {
                    Write-Host "    Still loading... ($elapsed s)" -ForegroundColor DarkGray
                    Start-Sleep -Seconds 5
                }
            }
        }

        if (-not $modelReady) {
            Write-Host "  [WARN] Model is still loading. Check: foundry service status" -ForegroundColor Yellow
        }
    }
}

# -------------------------------------------------------
# Step 7: Validate Foundry Local service is running
# -------------------------------------------------------
Write-Host "[7/7] Validating Foundry Local service..." -ForegroundColor Yellow

$foundryCmd = Get-Command "foundry" -ErrorAction SilentlyContinue
if (-not $foundryCmd) {
    Write-Host "  [WARN] Foundry CLI not in PATH. Restart terminal and run: foundry service status" -ForegroundColor Yellow
} else {
    # Check service status
    $valJob = Start-Job -ScriptBlock { & foundry service status 2>&1 }
    $valDone = $valJob | Wait-Job -Timeout 15
    if ($valDone) {
        $serviceOutput = Receive-Job $valJob | Out-String
    } else {
        Stop-Job $valJob -ErrorAction SilentlyContinue
        $serviceOutput = ""
        Write-Host "  [WARN] 'foundry service status' timed out." -ForegroundColor Yellow
    }
    Remove-Job $valJob -Force

    if ($serviceOutput) {
        Write-Host "  Service status:" -ForegroundColor DarkGray
        $serviceOutput.Trim().Split("`n") | ForEach-Object { Write-Host "    $_" -ForegroundColor DarkGray }
    }

    if ($serviceOutput -match "running" -and $serviceOutput -notmatch "not running") {
        Write-Host "  [OK] Foundry Local service is running" -ForegroundColor Green

        # Try a quick health check against the API endpoint
        $endpointMatch = [regex]::Match($serviceOutput, "https?://[\w\.\-]+:\d+")
        $endpoint = if ($endpointMatch.Success) { $endpointMatch.Value } else { "http://localhost:5273" }

        try {
            $response = Invoke-RestMethod -Uri "$endpoint/v1/models" -Method Get -TimeoutSec 10 -ErrorAction Stop
            $modelIds = ($response.data | ForEach-Object { $_.id }) -join ", "
            if ($modelIds) {
                Write-Host "  [OK] API is responding. Loaded models: $modelIds" -ForegroundColor Green
            } else {
                Write-Host "  [OK] API is responding but no models are loaded yet." -ForegroundColor Yellow
                Write-Host "    Run: foundry model run $Model" -ForegroundColor Yellow
            }
        } catch {
            Write-Host "  [WARN] Service is running but API did not respond at $endpoint" -ForegroundColor Yellow
            Write-Host "    The model may still be loading. Check with: foundry service status" -ForegroundColor Yellow
        }
    } else {
        Write-Host "  [WARN] Foundry Local service is not running." -ForegroundColor Yellow
        Write-Host "    Start it with: foundry model run $Model" -ForegroundColor Yellow
    }
}

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
Write-Host "  1. Load the extension in Edge:" -ForegroundColor White
Write-Host "     - Open edge://extensions" -ForegroundColor White
Write-Host "     - Enable Developer mode" -ForegroundColor White
Write-Host "     - Click 'Load unpacked' and select the 'extension/' folder" -ForegroundColor White
if (-not $ExtensionId) {
    Write-Host "  2. Copy the Extension ID and re-run:" -ForegroundColor White
    Write-Host "     .\install-host.ps1 -ExtensionId <id> -SkipFoundryInstall -SkipModelDownload -SkipBuild" -ForegroundColor White
}
Write-Host ""
