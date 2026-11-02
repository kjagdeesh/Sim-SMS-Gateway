# Sim SMS Gateway — App and Functionality Guide

This guide covers the Android app, browser dashboard, and the end-to-end steps for sending and reviewing SMS messages.

## What the app does

Sim SMS Gateway runs an HTTP API on an Android phone and submits SMS messages through that phone's default SIM. The browser dashboard is a client of that API; it does not relay messages through a cloud SMS provider. Send attempts and their results are stored in the app's private SQLite database on the phone.

An API result of `success` means Android accepted the SMS request. It does **not** confirm that the carrier delivered the message to the recipient.

## Requirements

- An Android phone with a SIM that can send SMS.
- SMS permission granted to Sim SMS Gateway when Android requests it.
- The app kept open while using the gateway; the current server is not a background service.
- For the browser playground, Node.js 18+ and a computer browser.
- For development or installation from source, .NET 10 SDK with the Android workload.

## Android app navigation

The bottom navigation has three tabs, in this order:

1. **Dashboard** — message totals and quick-access cards.
2. **Home** — the default tab when the app opens; project overview and getting-started steps.
3. **Developer** — developer profile and project links.

The Dashboard cards open the following pages:

### Dashboard

The overview shows total, successful, and failed message counts. Use the cards to open **Server**, **Message History**, **API Documentation**, **Settings**, or **About App**.

### Home

Home displays the gateway status, a short feature overview, and shortcuts to Server and API Documentation. Its three-step checklist is: start the server, copy its address and key, then send through the website or an API client.

### Server

1. Open **Server** from the Dashboard or Home.
2. Tap **Start server**. Grant SMS permission if Android asks.
3. Use a **Same Wi-Fi** address from the page when connecting from another device. `localhost` is only for a client running on the phone itself.
4. Reveal or copy the generated API key. The key is regenerated when the app starts, so reconnect clients after restarting the app.
5. Use **Copy sample command** to copy the cURL example. The address rows can also be opened or copied.
6. Tap **Stop server** when you no longer want the API listening.

The server listens on port `8765`. Keep the phone on a trusted network; the API uses plain HTTP, not TLS.

### Message History

History shows saved attempts, newest first. Filter by **All**, **Success**, or **Failed**. The app loads 50 records at a time; tap **Load older** when available. Each record shows the destination, message, local time, status, and a failure reason when present.

Successful submissions and handled Android/SIM send failures are recorded. Invalid API input rejected before an SMS attempt does not create a history record.

### API Documentation

Browse the health, send, bulk-send, history, and stats endpoints. Each endpoint includes request details, JSON examples, and language tabs for cURL, JavaScript, and Python. Copy controls copy the displayed code or JSON.

The **Try it** section sends a live request to the connected phone. For a send endpoint, confirm the prompt carefully: it submits a real SMS and may incur carrier charges. Connect and start the Android server first.

### Settings

- Choose **System**, **Light**, or **Dark** appearance. The selection is saved on the device.
- Review the app version, server port, stored-message count, and server status.
- Message history remains in the app's private database. Uninstalling the app removes its private app data.

### About App

The page attempts to load the Sim SMS Gateway project description and README summary from GitHub. If GitHub is unavailable, it shows the built-in project description and offers a retry. Project and issue links open externally.

### Developer

The third bottom tab shows the developer profile and project links. Profile information is loaded from GitHub when available; saved page details and links remain useful when offline.

## Browser dashboard navigation

The browser playground has a responsive side navigation:

- **Home** — overview and feature summary.
- **Dashboard** — message totals, status filters, and message history.
- **Send Messages** — single and bulk send forms.
- **API Documentation** — endpoint reference, code examples, and live tester.
- **Developer & Project** — developer profile and featured GitHub projects.

Use **Connect** to enter the phone's server address and API key. The dashboard checks `/health` and then makes an authenticated stats request. Once connected, the URL is saved in browser local storage and the API key in session storage for the current browser tab. The selected country and theme are also saved in local storage.

The website's Node.js server serves static files only and binds to `127.0.0.1` by default on port `8766`. It is not the SMS API server and does not proxy API calls. The browser connects directly to the phone.

## End-to-end setup and first message

### 1. Install and start the Android gateway

Build and install from the project directory:

```powershell
dotnet build .\SimSmsGateway.sln
dotnet build .\SimSmsGateway\SimSmsGateway.csproj -t:Install -f net10.0-android
```

Open the app, go to **Dashboard → Server**, tap **Start server**, and allow SMS permission. Leave the app open.

### 2. Open and connect the browser playground

From the `web` directory, start the static playground:

```powershell
npm start
```

Open `http://localhost:8766`. On the phone's Server page, copy a **Same Wi-Fi** address and the API key. In the browser, choose **Connect**, paste both values, and verify that the connection succeeds. Both devices must be able to reach each other on the trusted network.

### 3. Send a single message

1. Open **Send Messages** and select **Single message**.
2. Choose the default country. The country list shows each country with its calling code. Local numbers use that code; values beginning with `+` or `00` are treated as international numbers.
3. Enter the phone number and check the international-format preview.
4. Enter a message of up to 1,600 characters.
5. Send the message and review the HTTP response and result status.

The destination sent to the API must be in E.164 format: `+`, a non-zero country code, and 8–15 digits overall. For example, `+14155550123`.

### 4. Send a bulk message

1. Select **Bulk messages**.
2. Type or paste recipients separated by new lines, commas, semicolons, or spaces. Press Enter to add typed values.
3. Alternatively, import a `.csv` or `.txt` file up to 512 KiB. For CSV rows, the playground looks for a phone-number-like value in the row.
4. Review the recipient chips. Invalid numbers are highlighted; duplicate numbers are skipped.
5. Fix or remove invalid numbers, and keep the list to at most 100 unique valid recipients.
6. Enter the shared message and submit. The same text is sent to every recipient.
7. Review each recipient's result. One recipient's send failure does not stop the rest of the batch.

The server validates the entire bulk request before sending. If the request contains an invalid, duplicate, or out-of-range recipient, it rejects the request without sending any messages.

### 5. Review the outcome

Open **Dashboard** in the website or **Message History** in the Android app. Refresh the browser dashboard if needed. Filter the history by status, inspect failure reasons, and load older records as necessary. The website loads 25 history rows per page; the Android app loads 50.

## API quick reference

All URLs below are relative to the phone's base address, for example `http://192.168.1.20:8765`.

| Method and path | Authentication | Purpose |
|---|---|---|
| `GET /health` | None | Check that the gateway is responding. |
| `POST /api/v1/messages` | Bearer key | Send one SMS; accepted sends return HTTP 202. |
| `POST /api/v1/messages/bulk` | Bearer key | Send one message to 1–100 unique recipients; returns per-recipient results. |
| `GET /api/v1/messages` | Bearer key | Read history; optional `status=success\|failed`, `limit=1..200`, and `offset=0+`. |
| `GET /api/v1/stats` | Bearer key | Read total, successful, and failed counts. |

Authenticated calls use:

```http
Authorization: Bearer YOUR_API_KEY
```

POST requests require `Content-Type: application/json`. See [API documentation in the browser](web/index.html) and the machine-readable [OpenAPI specification](web/openapi.json) for request and response examples.

## Troubleshooting

| Symptom | What to check |
|---|---|
| Browser cannot connect | Start the phone server; use its **Same Wi-Fi** address, not `localhost` on another device; check both devices' network and firewall settings. |
| HTTP 401 | Copy the current API key again and send it as a Bearer token. The key changes after an app restart. |
| HTTP 400 | Check JSON, E.164 number formatting, message text, and bulk uniqueness/count constraints. |
| HTTP 415 | Set `Content-Type: application/json` on POST requests. |
| HTTP 503 for a single send | Android or the SIM rejected the send. Check SMS permission, default SIM, signal, and the ability to send SMS. |
| Bulk result has failed recipients | The HTTP request can still return 200; inspect each result's `status` and `error`. |
| API examples time out or cannot reach the phone | Confirm the server is running and reachable at the selected address. A browser page served over HTTPS may be blocked from calling this plain-HTTP API by browser mixed-content rules. |

## Safety and privacy

- Use the gateway only to contact recipients who have agreed to receive messages. Carrier charges may apply.
- The phone API uses plain HTTP. The bearer key does not encrypt the key, message, or phone numbers in transit. Do not expose port `8765` to the public internet or use an untrusted network.
- The browser talks directly to the phone. The Node.js playground does not store message history; history is in the phone's SQLite database.
- The browser saves the server URL and preferences in local storage, and the API key in session storage. Disconnect or clear the relevant browser storage on shared computers.
- The app's About and Developer pages may contact GitHub for public project/profile information; SMS message content is not sent to GitHub.
