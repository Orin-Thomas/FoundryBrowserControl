# Foundry Browser Control

AI-powered browser automation for Microsoft Edge using [Foundry Local](https://github.com/microsoft/Foundry-Local) for local LLM intelligence.

## Architecture

A sidebar panel in Edge lets you chat with a local AI agent that can navigate, click, type, read, and extract data from web pages — all powered by an LLM running entirely on your machine.

```
Edge Extension (Sidebar + Content Script)
        ↕ Native Messaging (stdin/stdout)
C# Host (.NET 8 Console App)
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
1. ✅ Check if **Foundry Local** is installed — installs via `winget` if missing
2. ✅ Check if the **LLM model** (`phi-4-mini`) is available — downloads if needed
3. ✅ **Build** the C# native messaging host
4. ✅ **Register** the native messaging host in the Windows registry

Use `-Model phi-3.5-mini` for a smaller/faster model, or pass `-SkipFoundryInstall`, `-SkipModelDownload`, `-SkipBuild` to skip individual steps.

### After running the installer

1. Start the model: `foundry model run phi-4-mini`
2. Open Edge and navigate to `edge://extensions`
3. Enable **Developer mode** (toggle in the bottom-left)
4. Click **Load unpacked** and select the `extension/` folder
5. Copy the **Extension ID** and re-run the installer to lock it down:
   ```powershell
   .\install-host.ps1 -ExtensionId <your-extension-id> -SkipFoundryInstall -SkipModelDownload -SkipBuild
   ```
6. Click the extension icon — the sidebar opens. Type a task!

## Configuration

Set environment variables to customize the Foundry Local connection:

| Variable | Default | Description |
|---|---|---|
| `FOUNDRY_ENDPOINT` | `http://localhost:5273` | Foundry Local API endpoint |
| `FOUNDRY_MODEL` | `phi-4-mini` | Model alias to use |

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
│   ├── background.js       Service worker (native messaging bridge)
│   ├── content.js          Content script (DOM interaction)
│   ├── sidepanel.html/js/css  Sidebar panel UI
│   └── icons/              Extension icons
├── src/
│   └── FoundryBrowserControl.Host/   C# native messaging host
│       ├── Program.cs                Entry point
│       ├── Agent/                    Agent loop and prompt engineering
│       ├── Llm/                      Foundry Local API client
│       ├── Models/                   DTOs
│       └── NativeMessaging/          stdin/stdout protocol
├── scripts/
│   ├── install-host.ps1    Register native messaging host
│   └── uninstall-host.ps1  Unregister native messaging host
└── README.md
```

## Uninstalling

```powershell
cd scripts
.\uninstall-host.ps1
```

Then remove the extension from `edge://extensions`.

## Troubleshooting

- **Host log**: Check `%LOCALAPPDATA%\FoundryBrowserControl\host.log` for diagnostics
- **Extension errors**: Open Edge DevTools on the sidebar panel (right-click → Inspect) to see console messages
- **Foundry Local not running**: Ensure `foundry service status` shows the service is running and note the port
- **Model not loaded**: Run `foundry model run phi-4-mini` to start the model

## License

MIT
