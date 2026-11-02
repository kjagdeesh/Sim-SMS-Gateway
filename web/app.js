const $ = (selector) => document.querySelector(selector);
const $$ = (selector) => [...document.querySelectorAll(selector)];

function h(tag, props = {}, ...children) {
    const element = document.createElement(tag);
    for (const [key, value] of Object.entries(props)) {
        if (value === undefined || value === null || value === false) continue;
        if (key === "class") element.className = value;
        else if (key === "text") element.textContent = value;
        else if (key.startsWith("on")) element.addEventListener(key.slice(2), value);
        else element.setAttribute(key, value === true ? "" : value);
    }
    for (const child of children.flat()) {
        if (child === undefined || child === null || child === false) continue;
        element.append(child.nodeType ? child : document.createTextNode(String(child)));
    }
    return element;
}

function icon(name, brand = false) {
    return h("i", {
        class: `${brand ? "fa-brands" : "fa-solid"} fa-${name}`,
        "aria-hidden": "true"
    });
}

function statusBadge(status) {
    const ok = status === "success";
    return h("span", {
        class: `badge badge-${ok ? "success" : "failed"}`
    }, icon(ok ? "circle-check" : "circle-xmark"), ok ? "Success" : "Failed");
}

const E164 = /^\+[1-9]\d{7,14}$/;
const MAX_BULK = 100;
const PAGE_SIZE = 25;
const PLACEHOLDER_URL = "http://PHONE_IP:8765";

const conn = {
    url: localStorage.getItem("simsms.serverUrl") ?? "",
    key: sessionStorage.getItem("simsms.apiKey") ?? "",
    online: false
};
let statusFilter = "";
let historyOffset = 0;
let mode = "single";
let recipients = [];

// ---------- Toast ----------
let toastTimer;

function toast(text) {
    const element = $("#toast");
    element.textContent = text;
    element.hidden = false;
    clearTimeout(toastTimer);
    toastTimer = setTimeout(() => {
        element.hidden = true;
    }, 2200);
}

async function copyText(text) {
    try {
        await navigator.clipboard.writeText(text);
        toast("Copied to clipboard");
    } catch {
        toast("Copy failed — select the text and copy manually");
    }
}

// ---------- API ----------
function parseBaseUrl(raw) {
    const rawUrl = raw.trim();
    if (!rawUrl) throw new Error("Enter the server URL shown by the Android app.");
    if (!URL.canParse(rawUrl)) throw new Error("Enter a valid server URL, including http:// and the port.");
    const url = new URL(rawUrl);
    if (url.protocol !== "http:" || url.username || url.password || url.pathname !== "/" || url.search || url.hash) {
        throw new Error("Use the HTTP server address without credentials, a path, or query parameters.");
    }
    return url.href.replace(/\/+$/, "");
}

async function fetchWithTimeout(url, options = {}, timeoutMs = 15000) {
    const controller = new AbortController();
    const timer = setTimeout(() => controller.abort(), timeoutMs);
    try {
        return await fetch(url, {
            ...options,
            signal: controller.signal
        });
    } finally {
        clearTimeout(timer);
    }
}

async function apiRequest(path, {
    method = "GET",
    body,
    timeoutMs,
    auth = true
} = {}) {
    const baseUrl = parseBaseUrl(conn.url);
    const headers = {};
    if (auth) {
        if (!conn.key) throw new Error("Paste the API key shown by the Android app.");
        headers.Authorization = "Bearer " + conn.key;
    }
    if (body !== undefined) headers["Content-Type"] = "application/json";

    const response = await fetchWithTimeout(baseUrl + path, {
        method,
        headers,
        body: body === undefined ? undefined : typeof body === "string" ? body : JSON.stringify(body)
    }, timeoutMs);
    const rawBody = await response.text();
    let json = null;
    try {
        json = JSON.parse(rawBody);
    } catch {
        /* shown as text */ }
    return {
        response,
        rawBody,
        json
    };
}

function describeError(error) {
    if (error.name === "AbortError")
    return "The request timed out. Check that the Android gateway is running and reachable.";
    if (error instanceof TypeError)
    return "Could not reach the gateway. Check the URL, Wi-Fi connection, and that the server is started.";
    return error.message || "The request failed.";
}

function apiError(response, json) {
    if (response.status === 401) return "The API key is not valid.";
    return json?.error || `Gateway returned HTTP ${response.status}.`;
}

// ---------- Connection ----------
function renderConnection() {
    const dot = $("#conn-dot");
    dot.dataset.state = conn.online ? "ok" : "";
    $("#conn-title").textContent = conn.online ? "Connected" : "Not connected";
    $("#conn-detail").textContent = conn.online ? conn.url.replace(/^https?:\/\//, "") : "Click to connect your phone";
    $("#topbar-connect").replaceChildren(icon(conn.online ? "circle-check" : "plug"), h("span", {
        text: conn.online ? "Connected" : "Connect"
    }));
}

function openConnectionDialog() {
    $("#server-url").value = conn.url;
    $("#api-key").value = conn.key;
    setStatus($("#connection-status"), "", "");
    $("#sidebar").classList.remove("open");
    $("#connection-dialog").showModal();
}

function setStatus(element, text, state) {
    element.textContent = text;
    element.dataset.state = state;
}

async function connect(url, key) {
    const baseUrl = parseBaseUrl(url);
    if (!key.trim()) throw new Error("Paste the API key shown by the Android app.");
    const health = await fetchWithTimeout(`${baseUrl}/health`);
    const healthBody = await health.json().catch(() => ({}));
    if (!health.ok || healthBody.status !== "online") throw new Error(`Gateway returned HTTP ${health.status}.`);

    conn.url = baseUrl;
    conn.key = key.trim();
    const {
        response,
        json
    } = await apiRequest("/api/v1/stats");
    if (!response.ok) {
        conn.online = false;
        throw new Error(apiError(response, json));
    }

    conn.online = true;
    localStorage.setItem("simsms.serverUrl", conn.url);
    sessionStorage.setItem("simsms.apiKey", conn.key);
    renderConnection();
    renderDocs();
}

$("#connection-card").addEventListener("click", openConnectionDialog);
$("#topbar-connect").addEventListener("click", openConnectionDialog);
$("#dialog-close").addEventListener("click", () => $("#connection-dialog").close());
document.addEventListener("click", (event) => {
    if (event.target.closest("[data-action='connect']")) openConnectionDialog();
});

$("#connection-form").addEventListener("submit", async (event) => {
    event.preventDefault();
    const button = $("#check-connection");
    button.disabled = true;
    setStatus($("#connection-status"), "Connecting…", "");
    try {
        await connect($("#server-url").value, $("#api-key").value);
        setStatus($("#connection-status"), "Connected.", "ok");
        $("#connection-dialog").close();
        toast("Connected to your gateway");
        onRouteEntered(currentRoute());
    } catch (error) {
        conn.online = false;
        renderConnection();
        setStatus($("#connection-status"), describeError(error), "error");
    } finally {
        button.disabled = false;
    }
});

// ---------- Router ----------
const ROUTES = ["home", "dashboard", "send", "api", "about"];

function currentRoute() {
    const route = location.hash.replace(/^#\/?/, "");
    return ROUTES.includes(route) ? route : "home";
}

function onRouteEntered(route) {
    if (route === "dashboard") refreshDashboard();
    if (route === "about") loadDeveloper();
}

function showRoute() {
    const route = currentRoute();
    for (const view of $$(".view")) view.hidden = view.id !== `view-${route}`;
    for (const item of $$(".menu-item")) {
        if (item.dataset.route === route) item.setAttribute("aria-current", "page");
        else item.removeAttribute("aria-current");
    }
    $("#page-title").textContent = $(`#view-${route}`).dataset.title;
    closeMenu();
    window.scrollTo(0, 0);
    onRouteEntered(route);
}

function closeMenu() {
    $("#sidebar").classList.remove("open");
    $("#scrim").hidden = true;
    $("#menu-toggle").setAttribute("aria-expanded", "false");
}

$("#menu-toggle").addEventListener("click", () => {
    $("#sidebar").classList.add("open");
    $("#scrim").hidden = false;
    $("#menu-toggle").setAttribute("aria-expanded", "true");
});
$("#scrim").addEventListener("click", closeMenu);
window.addEventListener("hashchange", showRoute);

// ---------- Countries & numbers ----------
const countrySelect = $("#country");
for (const [iso, name, dial] of COUNTRIES) {
    countrySelect.append(h("option", {
        value: iso,
        text: `${countryFlag(iso)}  ${name} (+${dial})`
    }));
}
const savedIso = localStorage.getItem("simsms.country");
countrySelect.value = COUNTRIES.some(([iso]) => iso === savedIso) ? savedIso : "IN";

const dialCodes = [...new Set(COUNTRIES.map(([, , dial]) => dial))].sort((a, b) => b.length - a.length);

function selectedDial() {
    return COUNTRIES.find(([iso]) => iso === countrySelect.value)[2];
}

// Returns an E.164 number, or null when the input cannot be one.
function toE164(raw) {
    const text = raw.trim();
    let digits = text.replace(/\D/g, "");
    if (text.startsWith("+")) digits = digits.replace(/^0+/, "");
    else if (text.startsWith("00")) digits = digits.replace(/^00/, "");
    else digits = selectedDial() + digits.replace(/^0+/, "");
    const number = "+" + digits;
    return E164.test(number) ? number : null;
}

function prettyNumber(number) {
    const digits = number.slice(1);
    const dial = dialCodes.find((code) => digits.startsWith(code));
    if (!dial) return number;
    const rest = digits.slice(dial.length);
    const grouped = rest.length > 5 ? `${rest.slice(0, 5)} ${rest.slice(5)}` : rest;
    return `+${dial} ${grouped}`;
}

function updateDialPrefix() {
    const [iso, name] = COUNTRIES.find(([countryIso]) => countryIso === countrySelect.value);
    const flag = $("#dial-prefix");
    const dial = selectedDial();
    flag.textContent = `${countryFlag(iso)} +${dial}`;
    flag.setAttribute("aria-label", `${name} country code +${dial}`);
    flag.title = name;
    localStorage.setItem("simsms.country", countrySelect.value);
    updateSinglePreview();
}

function updateSinglePreview() {
    const preview = $("#destination-preview");
    const value = $("#destination").value;
    if (!value.trim()) {
        setStatus(preview, "Enter a number to preview the full international format.", "");
        return;
    }

    const number = toE164(value);
    if (number) setStatus(preview, `Will send to ${prettyNumber(number)}`, "ok");
    else setStatus(preview, "This doesn't look like a valid number for the selected country.", "error");
}

countrySelect.addEventListener("change", updateDialPrefix);
$("#destination").addEventListener("input", updateSinglePreview);
$("#message").addEventListener("input", () => {
    $("#character-count").textContent = `${$("#message").value.length} / 1600`;
});

// ---------- Bulk recipient input ----------
function addRecipientTokens(text) {
    const parts = text.split(/[\n\r,;]+/).map((part) => part.trim()).filter(Boolean);
    let added = 0;
    let duplicates = 0;
    const addOne = (token) => {
        const number = toE164(token);
        const key = number ?? token;
        if (recipients.some((item) => (item.number ?? item.raw) === key)) {
            duplicates++;
            return;
        }
        recipients.push({
            raw: token,
            number
        });
        added++;
    };

    for (const part of parts) {
        if (/\s/.test(part) && !toE164(part)) {
            part.split(/\s+/).forEach(addOne);
            continue;
        }
        addOne(part);
    }

    const note = $("#recipient-note");
    const messages = [];
    if (added) messages.push(`Added ${added} number${added === 1 ? "" : "s"}.`);
    if (duplicates) messages.push(`${duplicates} duplicate${duplicates === 1 ? "" : "s"} skipped.`);
    setStatus(note, messages.join(" "), "");
    renderRecipients();
}

function renderRecipients() {
    const chips = $("#recipient-chips");
    chips.replaceChildren(...recipients.map((item, index) => h("span", {
            class: `rchip${item.number ? "" : " invalid"}`,
            title: item.number ? "" : "Not a valid number"
        },
        item.number ? prettyNumber(item.number) : item.raw,
        h("button", {
            type: "button",
            "aria-label": `Remove ${item.raw}`,
            onclick: () => {
                recipients.splice(index, 1);
                renderRecipients();
            }
        }, icon("xmark")))));

    const valid = recipients.filter((item) => item.number).length;
    const invalid = recipients.length - valid;
    const count = $("#recipient-count");
    let text = `${valid} valid recipient${valid === 1 ? "" : "s"}`;
    if (invalid) text += ` · ${invalid} invalid`;
    count.textContent = valid > MAX_BULK ? `${text} · limit is ${MAX_BULK}` : text;
    count.dataset.state = invalid || valid > MAX_BULK ? "error" : "";
}

const entry = $("#recipient-entry");

function flushEntry() {
    if (entry.value.trim()) {
        addRecipientTokens(entry.value);
        entry.value = "";
    }
}

entry.addEventListener("keydown", (event) => {
    if (event.key === "Enter" || event.key === "," || event.key === ";") {
        event.preventDefault();
        flushEntry();
    } else if (event.key === "Backspace" && !entry.value && recipients.length) {
        recipients.pop();
        renderRecipients();
    }
});
entry.addEventListener("paste", (event) => {
    const text = event.clipboardData?.getData("text");
    if (text && /[\n\r,;\s]/.test(text.trim())) {
        event.preventDefault();
        addRecipientTokens(text);
    }
});
entry.addEventListener("blur", flushEntry);
$("#recipient-box").addEventListener("click", () => entry.focus());
$("#recipient-clear").addEventListener("click", () => {
    recipients = [];
    setStatus($("#recipient-note"), "", "");
    renderRecipients();
});
$("#recipient-file").addEventListener("change", async (event) => {
    const file = event.target.files[0];
    event.target.value = "";
    if (!file) return;
    if (file.size > 512 * 1024) {
        setStatus($("#recipient-note"),
            "That file is too large. Use a list of at most a few hundred numbers.", "error");
        return;
    }

    const text = await file.text();
    // CSV rows: use the first column that looks like a number so names/headers are ignored.
    const lines = text.split(/\r?\n/).map((line) => line.split(/[,;\t]/).map((cell) => cell.trim().replace(
        /^"|"$/g, "")).find((cell) => /^\+?[\d\s()-]{6,}$/.test(cell)));
    addRecipientTokens(lines.filter(Boolean).join("\n"));
});

// ---------- Send tabs ----------
function setMode(next) {
    mode = next;
    const bulk = next === "bulk";
    $("#tab-single").setAttribute("aria-selected", String(!bulk));
    $("#tab-bulk").setAttribute("aria-selected", String(bulk));
    $("#single-fields").hidden = bulk;
    $("#bulk-fields").hidden = !bulk;
    $("#send-label").textContent = bulk ? "Send bulk SMS" : "Send SMS";
    showSendError("");
}

function showSendError(text) {
    $("#send-error").textContent = text;
    $("#send-error").hidden = !text;
}

$("#tab-single").addEventListener("click", () => setMode("single"));
$("#tab-bulk").addEventListener("click", () => setMode("bulk"));

function buildRequest() {
    const message = $("#message").value;
    if (!message.trim()) throw new Error("Enter a message.");

    if (mode === "single") {
        const to = toE164($("#destination").value);
        if (!to) throw new Error("Enter a valid phone number for the selected country.");
        return {
            path: "/api/v1/messages",
            body: {
                to,
                message
            }
        };
    }

    flushEntry();
    const invalid = recipients.filter((item) => !item.number);
    if (invalid.length) {
        throw new Error(
            `Remove or fix the highlighted invalid number${invalid.length === 1 ? "" : "s"}: ${invalid.slice(0, 3).map((item) => item.raw).join(", ")}${invalid.length > 3 ? "…" : ""}`
            );
    }
    if (recipients.length === 0) throw new Error("Add at least one recipient.");
    if (recipients.length > MAX_BULK) throw new Error(`Bulk sends are limited to ${MAX_BULK} recipients per request.`);
    return {
        path: "/api/v1/messages/bulk",
        body: {
            recipients: recipients.map((item) => item.number),
            message
        }
    };
}

function showResults(items) {
    const success = items.filter((item) => item.status === "success").length;
    $("#result-empty").hidden = true;
    $("#result-summary").hidden = false;
    $("#result-success").textContent = `${success} success`;
    $("#result-failed").textContent = `${items.length - success} failed`;
    $("#result-list").replaceChildren(...items.map((item) => h("li", {},
        h("span", {}, prettyNumber(item.to), item.error ? h("br") : null, item.error ? h("small", {
            text: item.error
        }) : null),
        statusBadge(item.status))));
}

function showRaw(status, body, isError) {
    const code = $("#response-code");
    code.textContent = status;
    code.dataset.state = isError ? "error" : "ok";
    $("#response-body").textContent = body;
}

$("#sms-form").addEventListener("submit", async (event) => {
    event.preventDefault();
    showSendError("");
    const button = $("#send-button");
    button.disabled = true;

    try {
        const {
            path,
            body
        } = buildRequest();
        if (!conn.online) {
            openConnectionDialog();
            throw new Error("Connect to your gateway first.");
        }

        const {
            response,
            rawBody,
            json
        } = await apiRequest(path, {
            method: "POST",
            body,
            timeoutMs: 60000
        });
        showRaw(`HTTP ${response.status}`, json ? JSON.stringify(json, null, 2) : rawBody ||
            "(empty response)", !response.ok);

        if (path.endsWith("/bulk") && response.ok) {
            showResults(json.results);
            toast(`${json.success} sent, ${json.failed} failed`);
        } else if (!path.endsWith("/bulk") && (response.ok || response.status === 503)) {
            showResults([{
                to: body.to,
                status: response.ok ? "success" : "failed",
                error: response.ok ? null : json?.error
            }]);
            toast(response.ok ? "Message accepted by Android" : "Message failed");
        } else {
            throw new Error(apiError(response, json));
        }
    } catch (error) {
        showSendError(describeError(error));
    } finally {
        button.disabled = false;
    }
});

// ---------- Dashboard ----------
function setStats(stats) {
    const total = stats?.total ?? 0;
    const success = stats?.success ?? 0;
    const rate = total ? Math.round((success / total) * 100) : 0;
    $("#stat-total").textContent = total;
    $("#stat-success").textContent = success;
    $("#stat-failed").textContent = stats?.failed ?? 0;
    $("#stat-rate").textContent = `${rate}%`;
    $("#stat-progress").value = rate;
}

function showEmpty(title, text, actions = []) {
    $("#history-empty").hidden = false;
    $("#history-table-wrap").hidden = true;
    $("#load-more").hidden = true;
    $("#history-empty-title").textContent = title;
    $("#history-empty-text").textContent = text;
    $("#history-empty-actions").replaceChildren(...actions);
}

function appendHistoryRow(item) {
    const row = $("#history-body").insertRow();
    row.insertCell().textContent = new Date(item.createdAt).toLocaleString();
    row.insertCell().textContent = prettyNumber(item.destination);
    const messageCell = row.insertCell();
    messageCell.className = "message-cell";
    messageCell.textContent = item.message;
    messageCell.title = item.message;
    const statusCell = row.insertCell();
    statusCell.append(statusBadge(item.status));
    if (item.error) statusCell.append(h("small", {
        class: "reason",
        text: item.error
    }));
}

async function loadHistory(append = false) {
    if (!append) historyOffset = 0;
    const query = new URLSearchParams({
        limit: String(PAGE_SIZE),
        offset: String(historyOffset)
    });
    if (statusFilter) query.set("status", statusFilter);

    const {
        response,
        json
    } = await apiRequest(`/api/v1/messages?${query}`);
    if (!response.ok) throw new Error(apiError(response, json));

    const body = $("#history-body");
    if (!append) body.replaceChildren();
    json.items.forEach(appendHistoryRow);
    historyOffset += json.items.length;

    if (body.rows.length === 0) {
        showEmpty(statusFilter ? `No ${statusFilter} messages` : "No messages yet", statusFilter ?
            "Try another filter." : "Messages you send will appear here.",
            statusFilter ? [] : [h("a", {
                class: "button button-primary",
                href: "#/send",
                text: "Send a message"
            })]);
        return;
    }

    $("#history-empty").hidden = true;
    $("#history-table-wrap").hidden = false;
    $("#load-more").hidden = json.items.length < PAGE_SIZE;
}

async function refreshDashboard() {
    setStatus($("#history-status"), "", "");
    if (!conn.online) {
        setStats(null);
        showEmpty("Not connected",
            "Connect to your phone's gateway to see your message history. Nothing has been loaded yet.",
            [h("button", {
                class: "button button-primary",
                type: "button",
                "data-action": "connect",
                text: "Connect gateway"
            })]);
        return;
    }

    try {
        const {
            response,
            json
        } = await apiRequest("/api/v1/stats");
        if (!response.ok) throw new Error(apiError(response, json));
        setStats(json);
        await loadHistory();
    } catch (error) {
        setStats(null);
        setStatus($("#history-status"), describeError(error), "error");
    }
}

$("#refresh-history").addEventListener("click", refreshDashboard);
$("#load-more").addEventListener("click", async () => {
    try {
        await loadHistory(true);
    } catch (error) {
        setStatus($("#history-status"), describeError(error), "error");
    }
});
for (const chip of $$(".chip")) {
    chip.addEventListener("click", () => {
        statusFilter = chip.dataset.filter;
        $$(".chip").forEach((other) => other.setAttribute("aria-pressed", String(other === chip)));
        refreshDashboard();
    });
}

// ---------- API documentation ----------
const SAMPLE_SEND = {
    to: "+919876543210",
    message: "Hello from my app"
};
const SAMPLE_BULK = {
    recipients: ["+919876543210", "+14155550123"],
    message: "Hello everyone"
};

const ENDPOINTS = [{
        id: "health",
        method: "GET",
        path: "/health",
        title: "Health check",
        auth: false,
        summary: "Checks that the gateway app is running. Use it to test the connection before sending.",
        responses: [{
            code: 200,
            text: "The gateway is online.",
            example: {
                status: "online"
            }
        }]
    },
    {
        id: "send",
        method: "POST",
        path: "/api/v1/messages",
        title: "Send one SMS",
        auth: true,
        sends: true,
        summary: "Sends a single SMS through the phone's SIM. Success means Android accepted it — carrier delivery is not confirmed.",
        params: [{
                name: "to",
                type: "string",
                required: true,
                desc: "Destination in international E.164 format: + country code + number, e.g. +919876543210."
            },
            {
                name: "message",
                type: "string",
                required: true,
                desc: "Text to send, up to 1600 characters."
            }
        ],
        body: SAMPLE_SEND,
        responses: [{
                code: 202,
                text: "Accepted by Android and recorded as success.",
                example: {
                    status: "accepted",
                    id: 12,
                    detail: "Android accepted the SMS request; carrier delivery is not confirmed."
                }
            },
            {
                code: 400,
                text: "Invalid number, empty message, or message too long.",
                example: {
                    error: "Destination must be an E.164 number, such as +14155550123."
                }
            },
            {
                code: 401,
                text: "Missing or wrong API key.",
                example: {
                    error: "A valid bearer API key is required."
                }
            },
            {
                code: 503,
                text: "The SIM or Android refused the send. Recorded as failed.",
                example: {
                    error: "SIM unavailable.",
                    id: 13,
                    status: "failed"
                }
            }
        ]
    },
    {
        id: "bulk",
        method: "POST",
        path: "/api/v1/messages/bulk",
        title: "Send bulk SMS",
        auth: true,
        sends: true,
        summary: "Sends the same message to up to 100 unique recipients, one after another. One failure never stops the others. If any number is invalid, nothing is sent.",
        params: [{
                name: "recipients",
                type: "string[]",
                required: true,
                desc: "1–100 unique E.164 numbers."
            },
            {
                name: "message",
                type: "string",
                required: true,
                desc: "Text to send to everyone, up to 1600 characters."
            }
        ],
        body: SAMPLE_BULK,
        responses: [{
                code: 200,
                text: "Batch processed. Check each result's status.",
                example: {
                    batchId: "0b9c4d…",
                    total: 2,
                    success: 1,
                    failed: 1,
                    results: [{
                        id: 14,
                        to: "+919876543210",
                        status: "success",
                        error: null
                    }, {
                        id: 15,
                        to: "+14155550123",
                        status: "failed",
                        error: "SIM unavailable."
                    }]
                }
            },
            {
                code: 400,
                text: "Invalid, duplicate, or too many recipients; nothing was sent.",
                example: {
                    error: "Recipient 2 must be an E.164 number, such as +14155550123."
                }
            },
            {
                code: 401,
                text: "Missing or wrong API key.",
                example: {
                    error: "A valid bearer API key is required."
                }
            }
        ]
    },
    {
        id: "history",
        method: "GET",
        path: "/api/v1/messages",
        title: "List message history",
        auth: true,
        summary: "Returns previously attempted messages from the phone's SQLite database, newest first.",
        params: [{
                name: "status",
                in: "query",
                type: "string",
                desc: "Optional filter: success or failed."
            },
            {
                name: "limit",
                in: "query",
                type: "integer",
                desc: "Page size, 1–200. Default 50."
            },
            {
                name: "offset",
                in: "query",
                type: "integer",
                desc: "How many items to skip. Default 0."
            }
        ],
        tryPath: "/api/v1/messages?limit=10",
        responses: [{
                code: 200,
                text: "A page of messages.",
                example: {
                    items: [{
                        id: 15,
                        batchId: "0b9c4d…",
                        destination: "+14155550123",
                        message: "Hello everyone",
                        status: "failed",
                        error: "SIM unavailable.",
                        createdAt: "2026-11-01T14:20:11.5+00:00"
                    }],
                    limit: 10,
                    offset: 0
                }
            },
            {
                code: 400,
                text: "Invalid status, limit, or offset.",
                example: {
                    error: "status must be 'success' or 'failed'."
                }
            },
            {
                code: 401,
                text: "Missing or wrong API key.",
                example: {
                    error: "A valid bearer API key is required."
                }
            }
        ]
    },
    {
        id: "stats",
        method: "GET",
        path: "/api/v1/stats",
        title: "Message totals",
        auth: true,
        summary: "Returns how many messages succeeded and failed in total.",
        responses: [{
                code: 200,
                text: "Totals across all stored messages.",
                example: {
                    total: 14,
                    success: 12,
                    failed: 2
                }
            },
            {
                code: 401,
                text: "Missing or wrong API key.",
                example: {
                    error: "A valid bearer API key is required."
                }
            }
        ]
    }
];

let docsSelected = "start";

function sampleBase() {
    return conn.url ? conn.url.replace(/\/+$/, "") : PLACEHOLDER_URL;
}

function codeSamples(endpoint, path) {
    const url = sampleBase() + path;
    const body = endpoint.body ? JSON.stringify(endpoint.body) : null;
    const authHeader = endpoint.auth ? ' \\\n  -H "Authorization: Bearer YOUR_API_KEY"' : "";
    const curl = `curl${endpoint.method === "POST" ? " -X POST" : ""} "${url}"${authHeader}` +
        (body ? ` \\\n  -H "Content-Type: application/json" \\\n  -d '${body}'` : "");

    const jsHeaders = [endpoint.auth ? `"Authorization": "Bearer YOUR_API_KEY"` : null, body ?
        `"Content-Type": "application/json"` : null
    ].filter(Boolean);
    const js = `const response = await fetch("${url}", {\n  method: "${endpoint.method}",` +
        (jsHeaders.length ? `\n  headers: { ${jsHeaders.join(", ")} },` : "") +
        (body ? `\n  body: JSON.stringify(${JSON.stringify(endpoint.body, null, 2).replace(/\n/g, "\n  ")}),` : "") +
        `\n});\nconsole.log(response.status, await response.json());`;

    const pyHeaders = [endpoint.auth ? `"Authorization": "Bearer YOUR_API_KEY"` : null].filter(Boolean);
    const py = `import requests\n\nresponse = requests.${endpoint.method.toLowerCase()}(\n    "${url}",` +
        (pyHeaders.length ? `\n    headers={${pyHeaders.join(", ")}},` : "") +
        (body ?
            `\n    json=${JSON.stringify(endpoint.body, null, 4).replace(/\n/g, "\n    ").replace(/: null/g, ": None")},` :
            "") +
        `\n    timeout=60,\n)\nprint(response.status_code, response.json())`;

    return {
        curl,
        javascript: js,
        python: py
    };
}

const JSON_TOKEN_PATTERN = /"(?:\\.|[^"\\])*"|-?\d+(?:\.\d+)?(?:[eE][+-]?\d+)?|\b(?:true|false|null)\b/g;
const CODE_TOKEN_PATTERN =
    /(?<comment>\/\/[^\r\n]*|#[^\r\n]*)|(?<string>"(?:\\.|[^"\\])*"|'(?:\\.|[^'\\])*'|`(?:\\.|[^`\\])*`)|(?<flag>--?[a-zA-Z][\w-]*)|(?<number>-?\b\d+(?:\.\d+)?(?:[eE][+-]?\d+)?\b)|(?<identifier>[A-Za-z_$][\w$]*)|(?<punctuation>[{}()[\],.:;])|(?<operator>=>|===?|!==?|&&|\|\||[=+*/<>!-])/g;
const CODE_KEYWORDS = {
    javascript: new Set(["async", "await", "const", "else", "function", "if", "import", "let", "new", "return", "throw", "try"]),
    python: new Set(["as", "def", "except", "False", "from", "import", "None", "True", "try", "with"])
};
const TERMINAL_LABELS = {
    curl: "cURL",
    javascript: "JavaScript",
    python: "Python",
    json: "JSON",
    http: "HTTP",
    text: "Output"
};

function highlightedCode(text, language) {
    const nodes = [];
    const pattern = language === "json" ? JSON_TOKEN_PATTERN : CODE_TOKEN_PATTERN;
    pattern.lastIndex = 0;
    let cursor = 0;
    let match;

    while ((match = pattern.exec(text)) !== null) {
        if (match.index > cursor) nodes.push(text.slice(cursor, match.index));
        const token = match[0];
        let tokenType = "";

        if (language === "json") {
            if (token.startsWith('"')) {
                tokenType = /^\s*:/.test(text.slice(match.index + token.length)) ? "key" : "string";
            } else {
                tokenType = /^\d/.test(token) || token.startsWith("-") ? "number" : "literal";
            }
        } else {
            const groups = match.groups;
            if (groups.comment) tokenType = "comment";
            else if (groups.string) tokenType = "string";
            else if (groups.flag) tokenType = "flag";
            else if (groups.number) tokenType = "number";
            else if (groups.punctuation) tokenType = "punctuation";
            else if (groups.operator) tokenType = "operator";
            else if (groups.identifier) {
                if (language === "curl" && token === "curl") tokenType = "command";
                else if (["GET", "POST", "PUT", "PATCH", "DELETE"].includes(token)) tokenType = "method";
                else if (CODE_KEYWORDS[language]?.has(token)) tokenType = "keyword";
                else if (["fetch", "console", "log", "print", "requests"].includes(token)) tokenType = "builtin";
                else tokenType = "identifier";
            }
        }

        nodes.push(tokenType ? h("span", {
            class: `syntax-${tokenType}`
        }, token) : token);
        cursor = pattern.lastIndex;
    }

    if (cursor < text.length) nodes.push(text.slice(cursor));
    return nodes;
}

function terminalCodeBlock(text, language, copy = true) {
    const code = h("pre", {
        class: "code-block"
    });
    const label = h("span", {
        class: "terminal-label",
        text: TERMINAL_LABELS[language] ?? "Code"
    });
    const copyButton = copy ? h("button", {
        class: "copy",
        type: "button",
        onclick: () => copyText(code.textContent ?? "")
    }, icon("copy"), "Copy") : null;
    const element = h("div", {
            class: "code-terminal",
            "data-language": language
        },
        h("div", {
            class: "code-terminal-bar"
        }, h("span", {
            class: "terminal-dots",
            "aria-hidden": "true"
        }, h("i", {
            class: "terminal-dot red"
        }), h("i", {
            class: "terminal-dot yellow"
        }), h("i", {
            class: "terminal-dot green"
        })), label, copyButton),
        code);

    const setContent = (value, nextLanguage = language) => {
        element.dataset.language = nextLanguage;
        label.textContent = TERMINAL_LABELS[nextLanguage] ?? "Code";
        code.replaceChildren(...highlightedCode(value, nextLanguage));
    };
    setContent(text);
    return {
        element,
        setContent
    };
}

function codeBlock(text, language) {
    const blockLanguage = language ?? (/^(?:Authorization|Content-Type):/im.test(text) ? "http" : "text");
    return h("div", {
        class: "code-wrap"
    }, terminalCodeBlock(text, blockLanguage).element);
}

function docsTable(columns, rows) {
    return h("table", {
        class: "doc-table"
    }, h("thead", {}, h("tr", {}, columns.map((column) => h("th", {
        text: column
    })))), h("tbody", {}, rows));
}

function renderStart() {
    return [
        h("div", {
                class: "panel"
            },
            h("h3", {
                class: "doc-title",
                text: "Getting started"
            }),
            h("p", {
                class: "doc-intro",
                text: "The gateway is a small JSON API that runs inside the Android app. Follow these four steps and you can send an SMS from any language."
            }),
            h("ol", {
                    class: "mini-steps"
                },
                h("li", {}, h("strong", {
                    text: "Start the server"
                }), " in the Android app and allow SMS permission."),
                h("li", {}, h("strong", {
                    text: "Copy the address"
                }), " shown in the app — your base URL. It looks like ", h("code", {
                    text: PLACEHOLDER_URL
                }), "."),
                h("li", {}, h("strong", {
                    text: "Copy the API key"
                }), " shown in the app."),
                h("li", {}, h("strong", {
                    text: "Send it with every request"
                }), " (except /health) in the Authorization header."))),
        h("div", {
                class: "panel"
            },
            h("p", {
                class: "doc-section-title",
                text: "Required headers"
            }),
            codeBlock("Authorization: Bearer YOUR_API_KEY\nContent-Type: application/json   # for POST requests"),
            h("p", {
                class: "doc-section-title",
                text: "Phone number format"
            }),
            h("p", {}, "Numbers must be ", h("strong", {
                text: "E.164"
            }), ": a ", h("code", {
                text: "+"
            }), ", the country code, then the number — 8 to 15 digits in total. Example: India ", h("code", {
                text: "+919876543210"
            }), ", US ", h("code", {
                text: "+14155550123"
            }), "."),
            h("p", {
                class: "doc-section-title",
                text: "What “success” means"
            }),
            h("p", {
                text: "success: Android accepted the SMS for sending. failed: the SIM or Android rejected it, with a reason. Carrier delivery is never confirmed."
            })),
        h("div", {
                class: "panel"
            },
            h("p", {
                class: "doc-section-title",
                text: "Common errors"
            }),
            docsTable(["Status", "Meaning", "Fix"], [
                ["400", "Request is invalid", "Read the error message; fix the number or message."],
                ["401", "API key missing or wrong", "Send Authorization: Bearer <key>."],
                ["415", "Wrong Content-Type", "Use application/json."],
                ["503", "SIM could not send", "Check SIM, signal and SMS permission."]
            ].map((row) => h("tr", {}, row.map((cell) => h("td", {
                text: cell
            })))))),
        h("div", {
            class: "callout"
        }, icon("triangle-exclamation"), h("span", {
            text: "The gateway uses plain HTTP. Only use it on a trusted network and never expose the port to the internet. Each SMS may cost money on your plan."
        }))
    ];
}

function renderTryIt(endpoint) {
    const pathInput = h("input", {
        class: "input",
        type: "text",
        value: endpoint.tryPath ?? endpoint.path,
        "aria-label": "Request path",
        spellcheck: "false"
    });
    const bodyInput = endpoint.body ? h("textarea", {
        "aria-label": "Request body",
        spellcheck: "false"
    }) : null;
    if (bodyInput) bodyInput.value = JSON.stringify(endpoint.body, null, 2);
    const output = terminalCodeBlock("Response will appear here.", "json");
    const status = h("span", {
        class: "response-code",
        text: "—"
    });
    const run = h("button", {
        class: "button button-primary",
        type: "button"
    }, icon("play"), `Send ${endpoint.method} request`);

    run.addEventListener("click", async () => {
        if (!conn.online) {
            openConnectionDialog();
            return;
        }

        if (endpoint.sends && !confirm("This will send a real SMS through your SIM. Continue?")) return;
        run.disabled = true;
        output.setContent("Sending…", "text");
        try {
            const {
                response,
                rawBody,
                json
            } = await apiRequest(pathInput.value.trim(), {
                method: endpoint.method,
                body: bodyInput ? bodyInput.value : undefined,
                auth: endpoint.auth,
                timeoutMs: 60000
            });
            status.textContent = `HTTP ${response.status}`;
            status.dataset.state = response.ok ? "ok" : "error";
            output.setContent(json ? JSON.stringify(json, null, 2) : rawBody || "(empty response)", json ? "json" : "text");
        } catch (error) {
            status.textContent = "Failed";
            status.dataset.state = "error";
            output.setContent(describeError(error), "text");
        } finally {
            run.disabled = false;
        }
    });

    return h("div", {
            class: "panel"
        },
        h("div", {
            class: "toolbar"
        }, h("p", {
            class: "doc-section-title",
            text: "Try it live"
        }), status),
        conn.online ? null : h("p", {
            class: "inline-status",
            text: "Connect your gateway first to run requests."
        }),
        h("div", {
                class: "try-grid"
            },
            h("div", {
                class: "try-row"
            }, h("span", {
                class: `verb ${endpoint.method === "GET" ? "get" : ""}`,
                text: endpoint.method
            }), pathInput),
            bodyInput,
            endpoint.sends ? h("div", {
                class: "callout"
            }, icon("triangle-exclamation"), h("span", {
                text: "This sends a real SMS. Change the number in the body to your own before running."
            })) : null,
            h("div", {}, run),
            output.element));
}

function renderEndpoint(endpoint) {
    const samples = codeSamples(endpoint, endpoint.tryPath ?? endpoint.path);
    const sampleOutput = h("div", {});
    const langButtons = {};
    const showLang = (lang) => {
        for (const [name, button] of Object.entries(langButtons)) button.setAttribute("aria-pressed", String(
            name === lang));
        sampleOutput.replaceChildren(codeBlock(samples[lang], lang));
    };
    for (const [name, label] of [
            ["curl", "cURL"],
            ["javascript", "JavaScript"],
            ["python", "Python"]
        ]) {
        langButtons[name] = h("button", {
            class: "chip",
            type: "button",
            "aria-pressed": "false",
            text: label,
            onclick: () => showLang(name)
        });
    }

    const nodes = [
        h("div", {
                class: "panel"
            },
            h("div", {
                class: "doc-title"
            }, h("span", {
                class: `verb ${endpoint.method === "GET" ? "get" : ""}`,
                text: endpoint.method
            }), h("h3", {
                text: endpoint.title
            })),
            h("p", {
                class: "doc-intro",
                text: endpoint.summary
            }),
            h("div", {
                class: "doc-path"
            }, h("strong", {
                text: endpoint.path
            }), h("span", {
                class: "muted",
                text: endpoint.auth ? "· requires API key" : "· no API key needed"
            })))
    ];

    if (endpoint.params) {
        nodes.push(h("div", {
                class: "panel"
            },
            h("p", {
                class: "doc-section-title",
                text: endpoint.body ? "Body fields (JSON)" : "Query parameters"
            }),
            docsTable(["Name", "Type", "Description"], endpoint.params.map((param) => h("tr", {},
                h("td", {}, h("code", {
                    text: param.name
                }), " ", param.required ? h("span", {
                    class: "req",
                    text: "required"
                }) : h("span", {
                    class: "opt",
                    text: "optional"
                })),
                h("td", {
                    text: param.type
                }),
                h("td", {
                    text: param.desc
                }))))));
    }

    nodes.push(h("div", {
            class: "panel"
        },
        h("p", {
            class: "doc-section-title",
            text: "Code example"
        }),
        h("div", {
            class: "lang-tabs"
        }, Object.values(langButtons)),
        sampleOutput));
    showLang("curl");

    nodes.push(h("div", {
            class: "panel"
        },
        h("p", {
            class: "doc-section-title",
            text: "Responses"
        }),
        ...endpoint.responses.map((item) => h("div", {
                class: "status-row"
            },
            h("span", {
                class: `status-code s${String(item.code)[0]}`,
                text: item.code
            }),
            h("div", {
                style: undefined
            }, h("p", {
                text: item.text
            }), codeBlock(JSON.stringify(item.example, null, 2), "json"))))));

    nodes.push(renderTryIt(endpoint));
    return nodes;
}

function renderDocs() {
    const nav = $("#docs-nav");
    const items = [{
        id: "start",
        method: "",
        title: "Getting started",
        subtitle: "Base URL, key, format"
    }, ...ENDPOINTS.map((endpoint) => ({
        id: endpoint.id,
        method: endpoint.method,
        title: endpoint.title,
        subtitle: endpoint.path
    }))];
    nav.replaceChildren(
        h("p", {
            class: "docs-nav-sep",
            text: "Overview"
        }),
        ...items.flatMap((item, index) => [
            index === 1 ? h("p", {
                class: "docs-nav-sep",
                text: "Endpoints"
            }) : null,
            h("button", {
                    class: "docs-nav-item",
                    type: "button",
                    "aria-current": String(item.id === docsSelected),
                    onclick: () => {
                        docsSelected = item.id;
                        renderDocs();
                        $("#docs-content").scrollIntoView({
                            block: "nearest"
                        });
                    }
                },
                item.method ? h("span", {
                    class: `verb ${item.method === "GET" ? "get" : ""}`,
                    text: item.method
                }) : h("span", {
                    class: "doc-ico"
                }, icon("rocket")),
                h("span", {}, item.title, h("small", {
                    text: item.subtitle
                })))
        ].filter(Boolean)));

    const endpoint = ENDPOINTS.find((candidate) => candidate.id === docsSelected);
    $("#docs-content").replaceChildren(...(endpoint ? renderEndpoint(endpoint) : renderStart()));
}

// ---------- Developer ----------
// Pinned on github.com/kjagdeesh (the REST API cannot read pins). Details are refreshed from the API when reachable.
const PINNED = [{
        name: "TasklyDev",
        lang: "C#",
        icon: "screwdriver-wrench",
        desc: "Open-source Windows developer toolkit built with C#, WPF and .NET 10: diagnostics, clipboard history, JWT, hashing and regex tools."
    },
    {
        name: "JStitch",
        lang: "JavaScript",
        icon: "diagram-project",
        desc: "JSON analysis tool to visualize, explore and convert JSON into tree views, tables, graphs and C# models."
    },
    {
        name: "PrivacyLens",
        lang: "C#",
        icon: "shield-halved",
        desc: "Open-source Android privacy dashboard built with .NET MAUI showing which apps access sensitive permissions, all processed on-device."
    },
    {
        name: "AIPneumoScan",
        lang: "C#",
        icon: "lungs",
        desc: "Cross-platform .NET MAUI app that detects pneumonia from chest X-rays using an offline ONNX model."
    }
];

function renderPinned(details = {}) {
    $("#pinned-grid").replaceChildren(...PINNED.map((item) => {
        const live = details[item.name.toLowerCase()];
        const stars = live?.stargazers_count;
        return h("a", {
                class: "pinned-card",
                href: `https://github.com/kjagdeesh/${item.name}`,
                target: "_blank",
                rel: "noopener noreferrer"
            },
            h("div", {
                class: "pinned-top"
            }, h("span", {
                class: "pinned-ico"
            }, icon(item.icon)), h("i", {
                class: "fa-solid fa-arrow-up-right-from-square pinned-out",
                "aria-hidden": "true"
            })),
            h("h4", {
                text: item.name
            }),
            h("p", {
                text: live?.description || item.desc
            }),
            h("div", {
                    class: "pinned-meta"
                },
                h("span", {}, icon("circle"), live?.language || item.lang),
                stars ? h("span", {}, icon("star"), String(stars)) : null));
    }));
}

let developerLoaded = false;
async function loadDeveloper() {
    if (developerLoaded) return;
    developerLoaded = true;
    renderPinned();
    try {
        fetchWithTimeout("https://api.github.com/users/kjagdeesh/repos?per_page=100", {}, 8000)
            .then((r) => (r.ok ? r.json() : []))
            .then((repos) => renderPinned(Object.fromEntries(repos.map((repo) => [repo.name.toLowerCase(), repo]))))
            .catch(() => {});
        const response = await fetchWithTimeout("https://api.github.com/users/kjagdeesh", {}, 8000);
        if (!response.ok) throw new Error("GitHub profile unavailable");
        const profile = await response.json();
        $("#dev-name").textContent = profile.name || profile.login;
        $("#dev-meta").textContent = ["Developer", profile.location, `@${profile.login}`].filter(Boolean).join(
            " · ");
        if (profile.bio) {
            $("#dev-bio").textContent = profile.bio;
            $("#dev-bio").hidden = false;
        }
        const avatar = $("#dev-avatar");
        avatar.addEventListener("load", () => {
            avatar.hidden = false;
            $("#dev-avatar-fallback").hidden = true;
        });
        avatar.src = profile.avatar_url;
    } catch {
        developerLoaded = false;
    }
}

// ---------- Theme ----------
function applyTheme(theme, persist) {
    document.documentElement.dataset.theme = theme;
    if (persist) localStorage.setItem("simsms.theme", theme);
    const dark = theme === "dark";
    $("#theme-toggle").replaceChildren(icon(dark ? "sun" : "moon"));
    $("#theme-toggle").setAttribute("aria-label", dark ? "Switch to light theme" : "Switch to dark theme");
}

$("#theme-toggle").addEventListener("click", () => {
    applyTheme(document.documentElement.dataset.theme === "dark" ? "light" : "dark", true);
});
window.matchMedia("(prefers-color-scheme: dark)").addEventListener("change", (event) => {
    if (!localStorage.getItem("simsms.theme")) applyTheme(event.matches ? "dark" : "light", false);
});

// ---------- Boot ----------
applyTheme(document.documentElement.dataset.theme, false);
setMode("single");
updateDialPrefix();
renderRecipients();
renderConnection();
renderDocs();
setStats(null);
showRoute();

if (conn.url && conn.key) {
    connect(conn.url, conn.key).then(() => onRouteEntered(currentRoute())).catch(() => {
        conn.online = false;
        renderConnection();
    });
}

setTimeout(() => $("#splash").classList.add("done"), 1100);