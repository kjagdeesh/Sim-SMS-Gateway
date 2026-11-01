const serverUrlInput = document.querySelector("#server-url");
const apiKeyInput = document.querySelector("#api-key");
const connectionStatus = document.querySelector("#connection-status");
const responseCode = document.querySelector("#response-code");
const responseBody = document.querySelector("#response-body");
const sendButton = document.querySelector("#send-button");
const messageInput = document.querySelector("#message");

function getApiBaseUrl() {
  const rawUrl = serverUrlInput.value.trim();
  if (!rawUrl) {
    throw new Error("Enter the server URL shown by the Android app.");
  }

  if (!URL.canParse(rawUrl)) {
    throw new Error("Enter a valid server URL, including http:// and the port.");
  }

  const url = new URL(rawUrl);
  if (url.protocol !== "http:" || url.username || url.password || url.pathname !== "/" || url.search || url.hash) {
    throw new Error("Use the HTTP server address without credentials, a path, or query parameters.");
  }

  return url.href.replace(/\/+$/, "");
}

function getApiKey() {
  const key = apiKeyInput.value.trim();
  if (!key) {
    throw new Error("Paste the API key shown by the Android app.");
  }

  return key;
}

async function fetchWithTimeout(url, options = {}) {
  const controller = new AbortController();
  const timer = window.setTimeout(() => controller.abort(), 15000);
  try {
    return await fetch(url, { ...options, signal: controller.signal });
  } finally {
    window.clearTimeout(timer);
  }
}

function showResponse(status, body, isError = false) {
  responseCode.textContent = status;
  responseCode.dataset.state = isError ? "error" : "ok";
  responseBody.textContent = body;
}

function describeError(error) {
  if (error.name === "AbortError") {
    return "The request timed out. Check that the Android gateway is running and reachable.";
  }
  if (error instanceof TypeError) {
    return "Could not reach the gateway. Check the URL, Wi-Fi connection, and browser network permissions.";
  }
  return error.message || "The request failed.";
}

messageInput.addEventListener("input", () => {
  document.querySelector("#character-count").textContent = `${messageInput.value.length} / 1600`;
});

document.querySelector("#check-connection").addEventListener("click", async (event) => {
  const button = event.currentTarget;
  connectionStatus.dataset.state = "";
  connectionStatus.textContent = "Checking gateway...";
  button.disabled = true;

  try {
    const baseUrl = getApiBaseUrl();
    const response = await fetchWithTimeout(`${baseUrl}/health`);
    const body = await response.json();
    if (!response.ok || body.status !== "online") {
      throw new Error(body.error || `Gateway returned HTTP ${response.status}.`);
    }

    connectionStatus.dataset.state = "ok";
    connectionStatus.textContent = "Gateway is online and reachable.";
  } catch (error) {
    connectionStatus.dataset.state = "error";
    connectionStatus.textContent = describeError(error);
  } finally {
    button.disabled = false;
  }
});

document.querySelector("#sms-form").addEventListener("submit", async (event) => {
  event.preventDefault();
  responseCode.dataset.state = "";
  responseBody.textContent = "Sending request...";
  sendButton.disabled = true;

  try {
    const baseUrl = getApiBaseUrl();
    const apiKey = getApiKey();
    const response = await fetchWithTimeout(`${baseUrl}/api/v1/messages`, {
      method: "POST",
      headers: {
        "Authorization": `Bearer ${apiKey}`,
        "Content-Type": "application/json"
      },
      body: JSON.stringify({
        to: document.querySelector("#destination").value.trim(),
        message: messageInput.value
      })
    });

    const rawBody = await response.text();
    let formattedBody = rawBody;
    try {
      formattedBody = JSON.stringify(JSON.parse(rawBody), null, 2);
    } catch {
      formattedBody = rawBody || "(empty response)";
    }
    showResponse(`HTTP ${response.status}`, formattedBody, !response.ok);
  } catch (error) {
    showResponse("Request failed", describeError(error), true);
  } finally {
    sendButton.disabled = false;
  }
});
