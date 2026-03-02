// content.js — Content script for Foundry Browser Control
// Captures page state and executes browser actions on the page.

// Listen for messages from the background service worker
chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
  if (message.type === "get_page_state") {
    const state = capturePageState();
    sendResponse(state);
    return true;
  }

  if (message.type === "execute_action") {
    executeAction(message.payload).then(sendResponse);
    return true; // Keep the message channel open for async response
  }
});

/**
 * Captures a simplified representation of the current page state.
 * Extracts interactive elements with their properties and selectors.
 */
function capturePageState() {
  const elements = [];
  let idCounter = 0;

  // Selectors for interactive and informational elements
  const interactiveSelectors = [
    "a[href]",
    "button",
    "input",
    "textarea",
    "select",
    "[role='button']",
    "[role='link']",
    "[role='tab']",
    "[role='menuitem']",
    "[role='checkbox']",
    "[role='radio']",
    "[role='combobox']",
    "[role='searchbox']",
    "[role='textbox']",
    "[onclick]",
    "[contenteditable='true']",
  ];

  const allInteractive = document.querySelectorAll(interactiveSelectors.join(","));

  for (const el of allInteractive) {
    // Skip hidden elements
    if (!isVisible(el)) continue;

    const elem = {
      id: idCounter++,
      tag: el.tagName.toLowerCase(),
      role: el.getAttribute("role") || getImplicitRole(el),
      text: getElementText(el).substring(0, 200), // Limit text length
      selector: generateSelector(el),
    };

    // Add type-specific attributes
    if (el.tagName === "INPUT") {
      elem.type = el.type || "text";
      elem.value = el.value || "";
      elem.placeholder = el.placeholder || null;
    } else if (el.tagName === "TEXTAREA") {
      elem.type = "textarea";
      elem.value = el.value || "";
      elem.placeholder = el.placeholder || null;
    } else if (el.tagName === "SELECT") {
      elem.type = "select";
      const selected = el.options[el.selectedIndex];
      elem.value = selected ? selected.text : "";
    } else if (el.tagName === "A") {
      elem.href = el.href || null;
    }

    const ariaLabel = el.getAttribute("aria-label");
    if (ariaLabel) {
      elem.ariaLabel = ariaLabel;
    }

    elements.push(elem);
  }

  return {
    url: window.location.href,
    title: document.title,
    elements: elements.slice(0, 150), // Limit to 150 elements to avoid token overflow
  };
}

/**
 * Executes a browser action on the page.
 */
async function executeAction(action) {
  try {
    switch (action.action) {
      case "click":
        return executeClick(action);
      case "type":
        return executeType(action);
      case "read":
        return executeRead(action);
      case "scroll":
        return executeScroll(action);
      case "wait":
        return await executeWait(action);
      case "extract":
        return executeExtract(action);
      default:
        return { success: false, error: `Unknown action: ${action.action}` };
    }
  } catch (error) {
    return { success: false, error: error.message };
  }
}

function executeClick(action) {
  const el = findElement(action);
  if (!el) return { success: false, error: "Element not found" };

  el.scrollIntoView({ behavior: "smooth", block: "center" });
  el.click();
  return { success: true, data: `Clicked: ${getElementText(el).substring(0, 100)}` };
}

function executeType(action) {
  const el = findElement(action);
  if (!el) return { success: false, error: "Element not found" };

  el.scrollIntoView({ behavior: "smooth", block: "center" });
  el.focus();

  // Clear existing value
  if (el.tagName === "INPUT" || el.tagName === "TEXTAREA") {
    el.value = "";
  }

  // Simulate typing by dispatching input events
  const text = action.text || "";
  if (el.tagName === "INPUT" || el.tagName === "TEXTAREA") {
    el.value = text;
    el.dispatchEvent(new Event("input", { bubbles: true }));
    el.dispatchEvent(new Event("change", { bubbles: true }));
  } else if (el.contentEditable === "true") {
    el.textContent = text;
    el.dispatchEvent(new Event("input", { bubbles: true }));
  }

  return { success: true, data: `Typed "${text.substring(0, 50)}" into element` };
}

function executeRead(action) {
  if (action.selector) {
    const el = document.querySelector(action.selector);
    if (!el) return { success: false, error: `Element not found: ${action.selector}` };
    return { success: true, data: el.textContent?.trim().substring(0, 5000) || "" };
  }

  // Read main page content
  const main = document.querySelector("main") || document.querySelector("article") || document.body;
  return { success: true, data: main.textContent?.trim().substring(0, 5000) || "" };
}

function executeScroll(action) {
  const amount = action.amount || 500;
  const direction = action.direction || "down";

  window.scrollBy({
    top: direction === "down" ? amount : -amount,
    behavior: "smooth",
  });

  return { success: true, data: `Scrolled ${direction} by ${amount}px` };
}

async function executeWait(action) {
  if (action.selector) {
    // Wait for element to appear
    const timeout = action.milliseconds || 10000;
    const start = Date.now();
    while (Date.now() - start < timeout) {
      if (document.querySelector(action.selector)) {
        return { success: true, data: `Element found: ${action.selector}` };
      }
      await new Promise((r) => setTimeout(r, 200));
    }
    return { success: false, error: `Timeout waiting for: ${action.selector}` };
  }

  // Simple time-based wait
  const ms = action.milliseconds || 1000;
  await new Promise((r) => setTimeout(r, ms));
  return { success: true, data: `Waited ${ms}ms` };
}

function executeExtract(action) {
  const format = action.format || "text";
  const selector = action.selector;

  if (!selector) {
    return { success: false, error: "Selector required for extract action" };
  }

  if (format === "table") {
    const table = document.querySelector(selector);
    if (!table) return { success: false, error: `Table not found: ${selector}` };

    const rows = [];
    for (const row of table.querySelectorAll("tr")) {
      const cells = [];
      for (const cell of row.querySelectorAll("th, td")) {
        cells.push(cell.textContent?.trim() || "");
      }
      rows.push(cells);
    }
    return { success: true, data: JSON.stringify(rows) };
  }

  if (format === "list") {
    const items = document.querySelectorAll(selector);
    const list = Array.from(items).map((el) => el.textContent?.trim() || "");
    return { success: true, data: JSON.stringify(list) };
  }

  // Default: text
  const el = document.querySelector(selector);
  if (!el) return { success: false, error: `Element not found: ${selector}` };
  return { success: true, data: el.textContent?.trim().substring(0, 5000) || "" };
}

// ---- Helper Functions ----

/**
 * Finds an element using elementId, selector, or text matching.
 */
function findElement(action) {
  // By element ID from our page state
  if (action.elementId !== undefined && action.elementId !== null) {
    const state = capturePageState();
    const elem = state.elements.find((e) => e.id === action.elementId);
    if (elem) {
      return document.querySelector(elem.selector);
    }
  }

  // By CSS selector
  if (action.selector) {
    return document.querySelector(action.selector);
  }

  // By visible text
  if (action.text) {
    return findElementByText(action.text);
  }

  return null;
}

/**
 * Finds an element by its visible text content.
 */
function findElementByText(text) {
  const lower = text.toLowerCase();
  const allElements = document.querySelectorAll("a, button, input, label, [role='button'], [role='link']");

  for (const el of allElements) {
    const elText = getElementText(el).toLowerCase();
    if (elText.includes(lower)) return el;
  }

  // Broader search
  const walker = document.createTreeWalker(document.body, NodeFilter.SHOW_ELEMENT, null);
  while (walker.nextNode()) {
    const node = walker.currentNode;
    if (node.children.length === 0 && node.textContent?.toLowerCase().includes(lower)) {
      return node;
    }
  }

  return null;
}

/**
 * Gets the visible text of an element, preferring aria-label and value attributes.
 */
function getElementText(el) {
  return (
    el.getAttribute("aria-label") ||
    el.getAttribute("title") ||
    el.getAttribute("alt") ||
    el.value ||
    el.textContent?.trim() ||
    ""
  );
}

/**
 * Checks if an element is visible on the page.
 */
function isVisible(el) {
  const style = window.getComputedStyle(el);
  return (
    style.display !== "none" &&
    style.visibility !== "hidden" &&
    style.opacity !== "0" &&
    el.offsetWidth > 0 &&
    el.offsetHeight > 0
  );
}

/**
 * Gets the implicit ARIA role for common elements.
 */
function getImplicitRole(el) {
  const tag = el.tagName.toLowerCase();
  const roles = {
    a: "link",
    button: "button",
    input: "textbox",
    textarea: "textbox",
    select: "combobox",
    img: "img",
    nav: "navigation",
    main: "main",
    header: "banner",
    footer: "contentinfo",
  };
  return roles[tag] || null;
}

/**
 * Generates a unique CSS selector for an element.
 */
function generateSelector(el) {
  // Try ID first
  if (el.id) {
    return `#${CSS.escape(el.id)}`;
  }

  // Try aria-label
  const ariaLabel = el.getAttribute("aria-label");
  if (ariaLabel) {
    const selector = `${el.tagName.toLowerCase()}[aria-label="${CSS.escape(ariaLabel)}"]`;
    if (document.querySelectorAll(selector).length === 1) return selector;
  }

  // Try name attribute for form elements
  const name = el.getAttribute("name");
  if (name) {
    const selector = `${el.tagName.toLowerCase()}[name="${CSS.escape(name)}"]`;
    if (document.querySelectorAll(selector).length === 1) return selector;
  }

  // Build path-based selector
  const parts = [];
  let current = el;
  while (current && current !== document.body) {
    let selector = current.tagName.toLowerCase();

    if (current.id) {
      parts.unshift(`#${CSS.escape(current.id)}`);
      break;
    }

    const parent = current.parentElement;
    if (parent) {
      const siblings = Array.from(parent.children).filter(
        (c) => c.tagName === current.tagName
      );
      if (siblings.length > 1) {
        const index = siblings.indexOf(current) + 1;
        selector += `:nth-of-type(${index})`;
      }
    }

    parts.unshift(selector);
    current = current.parentElement;
  }

  return parts.join(" > ");
}
