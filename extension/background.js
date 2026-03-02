// background.js — Service worker for Foundry Browser Control
// Manages native messaging port and routes messages between sidebar, content script, and host.

const NATIVE_HOST_NAME = "com.foundry.browsercontrol";
let nativePort = null;
let sidebarPort = null;

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
    ensureNativePort();
    if (nativePort) {
      nativePort.postMessage(message);
    } else {
      sendToSidebar({
        type: "connection_status",
        payload: { nativeHost: false, foundryLocal: false, error: chrome.runtime.lastError?.message || "Native host not found" }
      });
    }
  }
}

// Check connection to native host and Foundry Local
function checkConnection() {
  // Try connecting to native host
  ensureNativePort();

  if (!nativePort) {
    sendToSidebar({
      type: "connection_status",
      payload: { nativeHost: false, foundryLocal: false, error: chrome.runtime.lastError?.message || "Native messaging host not found or not registered" }
    });
    return;
  }

  // Native host is connected, ask it to check Foundry Local
  const requestId = "healthcheck-" + Date.now();
  nativePort.postMessage({ type: "health_check", requestId });

  // Set a timeout in case the host doesn't respond
  setTimeout(() => {
    // If we still have the port, native host is at least alive
    if (nativePort) {
      sendToSidebar({
        type: "connection_status",
        payload: { nativeHost: true, foundryLocal: false, error: "Health check timed out - Foundry Local may not be responding" }
      });
    }
  }, 10000);
}

// Handle messages from the native host
function handleNativeMessage(message) {
  console.log("[Background] From native host:", message);

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
      sendToSidebar({
        type: "connection_status",
        payload: message.payload
      });
      break;

    default:
      console.warn("[Background] Unknown native message type:", message.type);
  }
}

// Request page state from the active tab's content script
async function requestPageState(requestId) {
  try {
    const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
    if (!tab?.id) {
      sendToNative({ type: "page_state", payload: null, requestId });
      return;
    }

    const response = await chrome.tabs.sendMessage(tab.id, { type: "get_page_state" });
    sendToNative({ type: "page_state", payload: response, requestId });
  } catch (error) {
    console.error("[Background] Failed to get page state:", error);
    sendToNative({ type: "page_state", payload: null, requestId });
  }
}

// Execute an action on the active tab via the content script
async function executeAction(action, requestId) {
  try {
    const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });

    // Handle navigation at the background level
    if (action.action === "navigate" && action.url) {
      await chrome.tabs.update(tab.id, { url: action.url });
      // Wait for page load
      await new Promise((resolve) => {
        const listener = (tabId, changeInfo) => {
          if (tabId === tab.id && changeInfo.status === "complete") {
            chrome.tabs.onUpdated.removeListener(listener);
            resolve();
          }
        };
        chrome.tabs.onUpdated.addListener(listener);
        // Timeout after 30 seconds
        setTimeout(() => {
          chrome.tabs.onUpdated.removeListener(listener);
          resolve();
        }, 30000);
      });
      sendToNative({ type: "action_result", payload: { success: true, data: "Navigated to " + action.url }, requestId });
      return;
    }

    // Handle back at the background level
    if (action.action === "back") {
      await chrome.tabs.goBack(tab.id);
      await new Promise(resolve => setTimeout(resolve, 1000));
      sendToNative({ type: "action_result", payload: { success: true, data: "Went back" }, requestId });
      return;
    }

    if (!tab?.id) {
      sendToNative({ type: "action_result", payload: { success: false, error: "No active tab" }, requestId });
      return;
    }

    const response = await chrome.tabs.sendMessage(tab.id, { type: "execute_action", payload: action });
    sendToNative({ type: "action_result", payload: response, requestId });
  } catch (error) {
    console.error("[Background] Failed to execute action:", error);
    sendToNative({
      type: "action_result",
      payload: { success: false, error: error.message },
      requestId
    });
  }
}

// Ensure native messaging port is connected
function ensureNativePort() {
  if (nativePort) return;

  try {
    nativePort = chrome.runtime.connectNative(NATIVE_HOST_NAME);

    nativePort.onMessage.addListener((message) => {
      handleNativeMessage(message);
    });

    nativePort.onDisconnect.addListener(() => {
      const error = chrome.runtime.lastError;
      console.log("[Background] Native host disconnected:", error?.message || "unknown");
      nativePort = null;
      sendToSidebar({
        type: "status",
        payload: { status: "disconnected", message: "Native host disconnected: " + (error?.message || "unknown") }
      });
    });

    console.log("[Background] Connected to native host");
  } catch (error) {
    console.error("[Background] Failed to connect to native host:", error);
    nativePort = null;
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

// Send message to native host
function sendToNative(message) {
  if (nativePort) {
    try {
      nativePort.postMessage(message);
    } catch (error) {
      console.error("[Background] Failed to send to native host:", error);
    }
  }
}
