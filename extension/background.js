// background.js — Service worker for Foundry Browser Control
// Manages WebSocket connection to the C# host and routes messages between sidebar, content script, and host.

const WS_URL = "ws://localhost:52945/ws";
const HEALTH_URL = "http://localhost:52945/health";

let ws = null;
let sidebarPort = null;
let reconnectTimer = null;
let healthCheckTimeout = null;

// Open sidebar when extension icon is clicked
chrome.sidePanel.setPanelBehavior({ openPanelOnActionClick: true });

// Listen for connections from the sidebar
chrome.runtime.onConnect.addListener((port) => {
  if (port.name === "sidebar") {
    sidebarPort = port;
    console.log("[Background] Sidebar connected");

    port.onMessage.addListener((message) => {
      handleSidebarMessage(message);
    });

    port.onDisconnect.addListener(() => {
      console.log("[Background] Sidebar disconnected");
      sidebarPort = null;
    });
  }
});

// Handle messages from the sidebar
function handleSidebarMessage(message) {
  console.log("[Background] From sidebar:", message);

  if (message.type === "check_connection") {
    checkConnection();
    return;
  }

  if (message.type === "task" || message.type === "stop" || message.type === "clear" || message.type === "user_response") {
    ensureWebSocket().then(() => {
      sendToHost(message);
    }).catch((err) => {
      sendToSidebar({
        type: "connection_status",
        payload: { nativeHost: false, foundryLocal: false, error: err.message || "Cannot connect to host" }
      });
    });
  }
}

// Check connection to the host and Foundry Local
async function checkConnection() {
  // First check if the host HTTP server is reachable
  try {
    const response = await fetch(HEALTH_URL, { signal: AbortSignal.timeout(3000) });
    if (!response.ok) throw new Error("Health endpoint returned " + response.status);
  } catch (err) {
    sendToSidebar({
      type: "connection_status",
      payload: {
        nativeHost: false,
        foundryLocal: false,
        error: "Cannot reach host at " + HEALTH_URL + ": " + err.message +
          ". Make sure the host is running: dotnet run --project src/FoundryBrowserControl.Host"
      }
    });
    return;
  }

  // Host is reachable, now connect via WebSocket and send health check
  try {
    await ensureWebSocket();
    sendToHost({ type: "health_check", requestId: "healthcheck-" + Date.now() });

    // Timeout if no response
    if (healthCheckTimeout) clearTimeout(healthCheckTimeout);
    healthCheckTimeout = setTimeout(() => {
      if (ws && ws.readyState === WebSocket.OPEN) {
        sendToSidebar({
          type: "connection_status",
          payload: { nativeHost: true, foundryLocal: false, error: "Health check timed out - Foundry Local may not be responding" }
        });
      }
    }, 15000);
  } catch (err) {
    sendToSidebar({
      type: "connection_status",
      payload: { nativeHost: false, foundryLocal: false, error: "WebSocket connection failed: " + err.message }
    });
  }
}

// Ensure WebSocket is connected
function ensureWebSocket() {
  return new Promise((resolve, reject) => {
    if (ws && ws.readyState === WebSocket.OPEN) {
      resolve();
      return;
    }

    // Close any existing connection
    if (ws) {
      try { ws.close(); } catch (e) { }
      ws = null;
    }

    console.log("[Background] Connecting to WebSocket:", WS_URL);
    ws = new WebSocket(WS_URL);

    ws.onopen = () => {
      console.log("[Background] WebSocket connected");
      if (reconnectTimer) {
        clearTimeout(reconnectTimer);
        reconnectTimer = null;
      }
      resolve();
    };

    ws.onmessage = (event) => {
      try {
        const message = JSON.parse(event.data);
        handleHostMessage(message);
      } catch (err) {
        console.error("[Background] Failed to parse host message:", err);
      }
    };

    ws.onclose = (event) => {
      console.log("[Background] WebSocket closed:", event.code, event.reason);
      ws = null;
      sendToSidebar({
        type: "status",
        payload: { status: "disconnected", message: "Host connection closed" }
      });
    };

    ws.onerror = (event) => {
      console.error("[Background] WebSocket error:", event);
      reject(new Error("WebSocket connection failed"));
    };

    // Connection timeout
    setTimeout(() => {
      if (ws && ws.readyState === WebSocket.CONNECTING) {
        ws.close();
        ws = null;
        reject(new Error("WebSocket connection timed out"));
      }
    }, 5000);
  });
}

// Handle messages from the host
function handleHostMessage(message) {
  console.log("[Background] From host:", message);

  switch (message.type) {
    case "get_page_state":
      requestPageState(message.requestId);
      break;

    case "execute_action":
      executeAction(message.payload, message.requestId);
      break;

    case "status":
    case "ask_user":
    case "complete":
      sendToSidebar(message);
      break;

    case "health_check_result":
      if (healthCheckTimeout) {
        clearTimeout(healthCheckTimeout);
        healthCheckTimeout = null;
      }
      sendToSidebar({
        type: "connection_status",
        payload: message.payload
      });
      break;

    default:
      console.warn("[Background] Unknown host message type:", message.type);
  }
}

// Request page state from the active tab's content script
async function requestPageState(requestId) {
  try {
    const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
    if (!tab?.id) {
      sendToHost({ type: "page_state", payload: null, requestId });
      return;
    }

    const response = await chrome.tabs.sendMessage(tab.id, { type: "get_page_state" });
    sendToHost({ type: "page_state", payload: response, requestId });
  } catch (error) {
    console.error("[Background] Failed to get page state:", error);
    sendToHost({ type: "page_state", payload: null, requestId });
  }
}

// Execute an action on the active tab via the content script
async function executeAction(action, requestId) {
  try {
    const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });

    // Handle navigation at the background level
    if (action.action === "navigate" && action.url) {
      await chrome.tabs.update(tab.id, { url: action.url });
      await new Promise((resolve) => {
        const listener = (tabId, changeInfo) => {
          if (tabId === tab.id && changeInfo.status === "complete") {
            chrome.tabs.onUpdated.removeListener(listener);
            resolve();
          }
        };
        chrome.tabs.onUpdated.addListener(listener);
        setTimeout(() => {
          chrome.tabs.onUpdated.removeListener(listener);
          resolve();
        }, 30000);
      });
      sendToHost({ type: "action_result", payload: { success: true, data: "Navigated to " + action.url }, requestId });
      return;
    }

    // Handle back at the background level
    if (action.action === "back") {
      await chrome.tabs.goBack(tab.id);
      await new Promise(resolve => setTimeout(resolve, 1000));
      sendToHost({ type: "action_result", payload: { success: true, data: "Went back" }, requestId });
      return;
    }

    if (!tab?.id) {
      sendToHost({ type: "action_result", payload: { success: false, error: "No active tab" }, requestId });
      return;
    }

    const response = await chrome.tabs.sendMessage(tab.id, { type: "execute_action", payload: action });
    sendToHost({ type: "action_result", payload: response, requestId });
  } catch (error) {
    console.error("[Background] Failed to execute action:", error);
    sendToHost({
      type: "action_result",
      payload: { success: false, error: error.message },
      requestId
    });
  }
}

// Send message to the sidebar
function sendToSidebar(message) {
  if (sidebarPort) {
    try {
      sidebarPort.postMessage(message);
    } catch (error) {
      console.error("[Background] Failed to send to sidebar:", error);
    }
  }
}

// Send message to host via WebSocket
function sendToHost(message) {
  if (ws && ws.readyState === WebSocket.OPEN) {
    try {
      ws.send(JSON.stringify(message));
    } catch (error) {
      console.error("[Background] Failed to send to host:", error);
    }
  } else {
    console.warn("[Background] Cannot send to host - WebSocket not connected");
    sendToSidebar({
      type: "status",
      payload: { status: "error", message: "Not connected to host. Ensure the host is running." }
    });
  }
}
