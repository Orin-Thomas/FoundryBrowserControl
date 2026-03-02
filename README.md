# Foundry Browser Control

AI-powered browser automation for Microsoft Edge using [Foundry Local](https://github.com/microsoft/Foundry-Local) for local LLM intelligence.

## Architecture

A sidebar panel in Edge lets you chat with a local AI agent that can navigate, click, type, read, and extract data from web pages — all powered by an LLM running entirely on your machine.

```
Edge Extension (Sidebar + Content Script)
        ↕ WebSocket (ws://localhost:52945/ws)
C# Host (.NET 8 WebSocket Server)
        ↕ OpenAI-compatible API
Foundry Local (phi-4-mini)
```

## Prerequisites

- **Windows 10/11** (x64 or ARM)
- **[.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)**
- **Microsoft Edge** (Chromium-based, v116+)
- **[Foundry Local](https://github.com/microsoft/Foundry-Local)** — installed automatically by the setup script, or manually with:
  ```
  winget install Microsoft.FoundryLocal
  ```

## Setup

### Quick Start (one command)

```powershell
cd scripts
.\install-host.ps1
```

This script will:
1. Check if **Foundry Local** is installed — installs via `winget` if missing
2. Check if the **LLM model** (`phi-4-mini`) is available — downloads if needed
3. **Build** the C# WebSocket host
4. **Locate** the built executable
5. **Start** the Foundry Local model
6. **Start** the WebSocket host server

Use `-Model phi-3.5-mini` for a smaller/faster model, or pass `-SkipFoundryInstall`, `-SkipModelDownload`, `-SkipBuild` to skip individual steps.

### After running the installer

1. Open Edge and navigate to `edge://extensions`
2. Enable **Developer mode** (toggle in the bottom-left)
3. Click **Load unpacked** and select the `extension/` folder
4. Click the extension icon — the sidebar opens
5. A green indicator means the host and Foundry Local are connected

### Starting the host manually

If you need to restart the host:

```powershell
# From the project root:
dotnet run --project src/FoundryBrowserControl.Host

# Or run the built exe directly:
src\FoundryBrowserControl.Host\bin\Debug\net8.0\FoundryBrowserControl.Host.exe
```

## Configuration

Set environment variables to customize behavior:

| Variable | Default | Description |
|---|---|---|
| `FOUNDRY_ENDPOINT` | Auto-discovered | Foundry Local API endpoint |
| `FOUNDRY_MODEL` | `phi-4-mini` | Model alias to use |
| `BROWSER_CONTROL_PORT` | `52945` | WebSocket server port |

Find your Foundry Local port with:
```bash
foundry service status
```

## Supported Actions

The agent can perform these actions on web pages:

| Action | Description |
|---|---|
| `navigate` | Go to a URL |
| `click` | Click an element (by ID, selector, or text) |
| `type` | Type text into an input field |
| `read` | Read text content from the page |
| `scroll` | Scroll the page up or down |
| `wait` | Wait for an element or a specified time |
| `extract` | Extract structured data (text, table, or list) |
| `back` | Go back in browser history |
| `complete` | Signal task completion |
| `ask_user` | Ask the user a clarifying question |

## Project Structure

```
├── extension/              Edge extension (Manifest V3)
│   ├── manifest.json       Extension manifest
│   ├── background.js       Service worker (WebSocket client)
│   ├── content.js          Content script (DOM interaction)
│   ├── sidepanel.html/js/css  Sidebar panel UI
│   └── icons/              Extension icons
├── src/
│   └── FoundryBrowserControl.Host/   C# WebSocket host
│       ├── Program.cs                WebSocket server + Foundry discovery
│       ├── Agent/                    Agent loop and prompt engineering
│       ├── Llm/                      Foundry Local API client
│       ├── Models/                   DTOs
│       └── Transport/                WebSocket transport
├── scripts/
│   ├── install-host.ps1    Full setup script
│   └── uninstall-host.ps1  Cleanup script
└── README.md
```

## Troubleshooting

- **Host log**: Check `%LOCALAPPDATA%\FoundryBrowserControl\host.log` for diagnostics
- **Host health**: Visit `http://localhost:52945/health` in a browser — should return `{"status":"ok"}`
- **Extension errors**: Open Edge DevTools on the sidebar panel (right-click → Inspect) to see console messages
- **Foundry Local not running**: Ensure `foundry service status` shows the service is running and note the port
- **Model not loaded**: Run `foundry model run phi-4-mini` to start the model

## License

MIT
