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
- **[Foundry Local](https://github.com/microsoft/Foundry-Local)** — Install with:
  ```
  winget install Microsoft.FoundryLocal
  ```
- **Microsoft Edge** (Chromium-based, v116+)

## Setup

### 1. Start Foundry Local with a model

```bash
foundry model run phi-4-mini
```

> This downloads and starts the `phi-4-mini` model locally. You can also use `phi-3.5-mini` for a smaller/faster option.

### 2. Build the C# host

```bash
cd src/FoundryBrowserControl.Host
dotnet build -c Debug
```

### 3. Load the extension in Edge

1. Open Edge and navigate to `edge://extensions`
2. Enable **Developer mode** (toggle in the bottom-left)
3. Click **Load unpacked** and select the `extension/` folder
4. Copy the **Extension ID** that appears

### 4. Register the native messaging host

```powershell
cd scripts
.\install-host.ps1 -ExtensionId <your-extension-id>
```

### 5. Use it

1. Click the extension icon in Edge's toolbar — the sidebar panel opens
2. Type a task like "Search Google for weather in Seattle"
3. Watch the agent think and act!

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
