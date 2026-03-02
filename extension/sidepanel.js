// sidepanel.js — Sidebar panel logic for Foundry Browser Control

const messagesEl = document.getElementById("messages");
const taskInput = document.getElementById("taskInput");
const sendBtn = document.getElementById("sendBtn");
const stopBtn = document.getElementById("stopBtn");
const clearBtn = document.getElementById("clearBtn");
const statusEl = document.getElementById("status");
const connectionLight = document.getElementById("connectionLight");

let port = null;
let isRunning = false;

// Connect to background service worker
function connectToBackground() {
  setConnectionState("checking", "Checking connection...");

  port = chrome.runtime.connect({ name: "sidebar" });

  port.onMessage.addListener((message) => {
    handleMessage(message);
  });

  port.onDisconnect.addListener(() => {
    console.log("[Sidebar] Disconnected from background");
    port = null;
    setRunning(false);
    setConnectionState("disconnected", "Disconnected from background");
    showStatus("disconnected", "Disconnected. Will reconnect on next task.");
  });

  // Request a connection health check
  port.postMessage({ type: "check_connection" });
}

// Handle messages from the background
function handleMessage(message) {
  console.log("[Sidebar] Received:", message);

  switch (message.type) {
    case "status":
      handleStatus(message.payload);
      break;
    case "ask_user":
      handleAskUser(message.payload);
      break;
    case "connection_status":
      handleConnectionStatus(message.payload);
      break;
    default:
      console.warn("[Sidebar] Unknown message type:", message.type);
  }
}

function handleStatus(payload) {
  const { status, message } = payload;

  switch (status) {
    case "thinking":
      showStatus("thinking", message);
      break;
    case "acting":
      showStatus("acting", message);
      addMessage("agent-action", message);
      break;
    case "complete":
      hideStatus();
      addMessage("agent-complete", message);
      setRunning(false);
      break;
    case "error":
      hideStatus();
      addMessage("agent-error", message);
      setRunning(false);
      break;
    case "stopped":
      hideStatus();
      addMessage("system", "Task stopped.");
      setRunning(false);
      break;
    case "cleared":
      clearMessages();
      hideStatus();
      setRunning(false);
      break;
    case "disconnected":
      hideStatus();
      addMessage("agent-error", message);
      setConnectionState("error", "Disconnected: " + message);
      setRunning(false);
      break;
  }
}

function handleAskUser(question) {
  hideStatus();
  addMessage("agent-question", question);
  setRunning(false); // Allow user to respond
  taskInput.placeholder = "Type your answer...";
  taskInput.focus();

  // Mark input as a response to a question
  taskInput.dataset.responseMode = "true";
}

// Send task to background
function sendTask() {
  const text = taskInput.value.trim();
  if (!text) return;

  if (!port) connectToBackground();

  const isResponse = taskInput.dataset.responseMode === "true";
  taskInput.dataset.responseMode = "false";
  taskInput.placeholder = "Tell me what to do...";

  addMessage("user", text);
  taskInput.value = "";

  if (isResponse) {
    port.postMessage({ type: "user_response", payload: text });
  } else {
    port.postMessage({ type: "task", payload: text });
  }

  setRunning(true);
}

// Stop current task
function stopTask() {
  if (port) {
    port.postMessage({ type: "stop" });
  }
  setRunning(false);
  hideStatus();
}

// Clear conversation
function clearConversation() {
  if (port) {
    port.postMessage({ type: "clear" });
  }
  clearMessages();
  hideStatus();
  setRunning(false);
}

// UI Helpers

function addMessage(type, text) {
  const div = document.createElement("div");
  div.className = `message ${type}`;

  const p = document.createElement("p");
  p.textContent = text;
  div.appendChild(p);

  // Add label
  const label = document.createElement("span");
  label.className = "message-label";
  switch (type) {
    case "user": label.textContent = "You"; break;
    case "agent-action": label.textContent = "🔧 Action"; break;
    case "agent-complete": label.textContent = "✅ Done"; break;
    case "agent-error": label.textContent = "❌ Error"; break;
    case "agent-question": label.textContent = "❓ Agent asks"; break;
    case "system": label.textContent = "System"; break;
  }
  div.insertBefore(label, p);

  messagesEl.appendChild(div);
  messagesEl.scrollTop = messagesEl.scrollHeight;
}

function clearMessages() {
  messagesEl.innerHTML = `
    <div class="message system">
      <p>👋 Hello! I can help you automate browser tasks. Tell me what you'd like me to do.</p>
      <p class="hint">Examples: "Search for weather in Seattle", "Fill in this form", "Extract all links"</p>
    </div>
  `;
}

function showStatus(type, message) {
  statusEl.classList.remove("hidden");
  statusEl.className = `status ${type}`;
  statusEl.querySelector(".status-text").textContent = message;
}

function hideStatus() {
  statusEl.classList.add("hidden");
}

function setRunning(running) {
  isRunning = running;
  sendBtn.classList.toggle("hidden", running);
  stopBtn.classList.toggle("hidden", !running);
  taskInput.disabled = running;
}

function handleConnectionStatus(payload) {
  const { nativeHost, foundryLocal, error, model, availableModels } = payload;

  if (nativeHost && foundryLocal) {
    const modelInfo = model ? ` (model: ${model})` : "";
    setConnectionState("connected", "Connected to Foundry Local" + modelInfo);
    addMessage("system",
      "Connected to Foundry Local" + modelInfo +
      (availableModels && availableModels.length > 0
        ? "\nAvailable models: " + availableModels.join(", ")
        : "")
    );
  } else if (nativeHost && !foundryLocal) {
    setConnectionState("error", "Host connected but Foundry Local is not responding");
    addMessage("agent-error",
      "Connection issue: Host is running but cannot reach Foundry Local.\n" +
      "Error: " + (error || "Unknown") + "\n" +
      "Troubleshooting:\n" +
      "  1. Check if Foundry Local is running: foundry service status\n" +
      "  2. Start the service: foundry service start\n" +
      "  3. Load the model: foundry model run phi-4-mini\n" +
      "  4. Check host log: %LOCALAPPDATA%\\FoundryBrowserControl\\host.log"
    );
  } else {
    setConnectionState("error", "Cannot connect to host");
    addMessage("agent-error",
      "Connection issue: Cannot connect to the host server.\n" +
      "Error: " + (error || "Unknown") + "\n" +
      "Troubleshooting:\n" +
      "  1. Start the host: dotnet run --project src/FoundryBrowserControl.Host\n" +
      "  2. Or run the built exe from the install location\n" +
      "  3. Default port: 52945 (set BROWSER_CONTROL_PORT to change)"
    );
  }
}

function setConnectionState(state, tooltip) {
  connectionLight.className = "connection-light " + state;
  connectionLight.title = tooltip;
}

// Event listeners
sendBtn.addEventListener("click", sendTask);
stopBtn.addEventListener("click", stopTask);
clearBtn.addEventListener("click", clearConversation);

taskInput.addEventListener("keydown", (e) => {
  if (e.key === "Enter" && !e.shiftKey) {
    e.preventDefault();
    sendTask();
  }
});

// Connect on load
connectToBackground();
