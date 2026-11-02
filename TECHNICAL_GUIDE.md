# Sim SMS Gateway — Technical Guide

This document describes the Android SMS API server, request lifecycle, persistence, browser playground, and the main extension points in the source.

## Architecture

```text
Browser UI / API client
        │  HTTP + JSON (LAN, port 8765)
        ▼
Android GatewayHost
        │
        ▼
SimSmsGateway.Core: SmsApiServer
  ├─ HTTP parsing, routing, CORS, bearer authentication
  ├─ SmsRequestValidator
  ├─ ISmsSender ── AndroidSmsSender ── Android SmsManager ── default SIM/carrier
  └─ SmsMessageStore ── messages.db
        ▲
        └── Android Dashboard and Message History use the same store
```

The browser playground is a separate static application. Its JavaScript calls the Android API directly; the Node.js process does not proxy, authenticate, or persist SMS requests.

## Runtime lifecycle

1. `App` creates one `SmsMessageStore` at `FileSystem.AppDataDirectory/messages.db`.
2. `GatewayHost` creates a random URL-safe API key from 32 cryptographically random bytes. It is held in memory and displayed on the Server page. It changes when the app process starts again.
3. When the user starts the gateway, `GatewayHost` creates `SmsApiServer` with an `AndroidSmsSender`, that API key, and the shared message store.
4. `SmsApiServer.StartAsync` creates a `TcpListener` on `IPAddress.Any` and port `8765`. Binding to `IPAddress.Any` makes the listener available on the phone's network interfaces.
5. The host exposes localhost and available non-loopback IPv4 addresses for clients. The UI observes host state to keep the status display current.
6. Stopping the gateway cancels and stops the listener. Closing the app also stops the in-process server; this implementation is not an Android foreground/background service.

Relevant source: [App.xaml.cs](SimSmsGateway/App.xaml.cs), [GatewayHost.cs](SimSmsGateway/GatewayHost.cs), [SmsApiServer.cs](SimSmsGateway.Core/SmsApiServer.cs).

## HTTP server implementation

`SmsApiServer` is a small, hand-built HTTP/1.x server over `TcpListener`, not ASP.NET Core, Kestrel, or a reverse proxy.

- It accepts a client and handles that request before accepting the next client. The TCP listener backlog is 16; request processing is sequential.
- Each request has a 60-second timeout. The server reads one request per connection and responds with `Connection: close`.
- Request headers are limited to 16 KiB; request bodies are limited to 32 KiB.
- The parser reads the request line and headers, then reads the body according to `Content-Length`. Send JSON with a content length; chunked request bodies are not implemented.
- Responses include JSON content type where applicable and CORS headers allowing any origin, `GET`, `POST`, `OPTIONS`, and the `Authorization` and `Content-Type` headers. `OPTIONS` receives HTTP 204.
- The transport is plain HTTP. There is no TLS, certificate management, rate limiting, or request queue beyond the TCP listener backlog in this server.

These are implementation characteristics, not production security guarantees. In particular, CORS does not protect the API key or message data from interception on the network.

## Authentication and validation

All API endpoints except `GET /health` require:

```http
Authorization: Bearer YOUR_API_KEY
```

The server first checks the Bearer scheme, then compares equal-length UTF-8 key values with `CryptographicOperations.FixedTimeEquals`. The key is random per app process and is not persisted to the database. CORS preflight `OPTIONS` requests are handled without API-key authentication.

Validation in `SmsRequestValidator` enforces:

- E.164 destinations matching `^\+[1-9]\d{7,14}$`.
- Nonblank message text, up to 1,600 characters.
- Bulk batches of 1–100 unique E.164 recipients.
- Full bulk validation before any recipient is sent.

POST endpoints require `Content-Type: application/json`. Malformed JSON or invalid request fields return HTTP 400. A single-send Android/SIM submission failure returns 503; bulk sends report individual failures in the response while returning 200 for a validated and processed batch.

## API contract

Base URL: `http://PHONE_IP:8765`

| Method and path | Key | Request | Result |
|---|---:|---|---|
| `GET /health` | No | None | HTTP 200, `{"status":"online"}`. |
| `POST /api/v1/messages` | Yes | `{"to":"+14155550123","message":"Hello"}` | HTTP 202 with accepted status and record ID, or HTTP 503 with failed status and record ID. |
| `POST /api/v1/messages/bulk` | Yes | `{"recipients":["+14155550123"],"message":"Hello"}` | HTTP 200 with batch ID, counts, and each recipient's ID, destination, status, and error. |
| `GET /api/v1/messages` | Yes | Optional `status=success|failed`, `limit=1..200`, `offset=0+` | HTTP 200 with `items`, `limit`, and `offset`; default page is 50. |
| `GET /api/v1/stats` | Yes | None | HTTP 200 with `total`, `success`, and `failed` counts. |

History is returned newest first. Query values outside the documented range return HTTP 400.

Other relevant responses are HTTP 401 for a missing/invalid key, 404 for an unknown route, 413 for a body larger than 32 KiB, 415 for an unsupported POST content type, and 431 for oversized headers. Malformed HTTP requests can also return 400.

## Send and status flow

### Single SMS

1. Authenticate and require JSON content type.
2. Deserialize the request and validate destination and message.
3. Call `ISmsSender.SendAsync`.
4. `AndroidSmsSender` obtains Android's default SMS manager, splits the message with `DivideMessage`, and submits either one text part or multipart text parts.
5. If Android accepts the API call without throwing, store a `success` record and return HTTP 202.
6. If Android submission raises `SmsSendException`, store a `failed` record with its error and return HTTP 503.

The Android sender does not wait for a carrier delivery receipt. Therefore, `success` means accepted by Android for submission, not delivered to the recipient.

### Bulk SMS

After validating every recipient and the shared message, the server creates a GUID batch ID and processes recipients sequentially. Every recipient receives an independent database record with the shared batch ID. A send failure is captured for that recipient and does not stop later recipients. The response includes total, successful, and failed counts plus one result per recipient.

The single-send and bulk-send status behavior is implemented in [SmsApiServer.cs](SimSmsGateway.Core/SmsApiServer.cs); input rules are in [SmsRequestValidator.cs](SimSmsGateway.Core/SmsRequestValidator.cs), and the Android adapter is [AndroidSmsSender.cs](SimSmsGateway/Platforms/Android/AndroidSmsSender.cs).

## SQLite persistence

The app's `SmsMessageStore` creates the `messages` table if needed:

| Column | Meaning |
|---|---|
| `id` | Auto-incrementing record ID. |
| `batch_id` | Nullable bulk batch ID; single sends use null. |
| `destination` | E.164 destination. |
| `message` | Message text. |
| `status` | `success` or `failed`, constrained by the schema. |
| `error` | Nullable failure explanation. |
| `created_at` | ISO 8601 UTC timestamp. |

Indexes support status and newest-first history lookups. A lock serializes access to the shared SQLite connection. Each attempted recipient is inserted after the sender reports acceptance or a handled send failure. Invalid requests rejected before sending are not recorded. No automatic history-retention or pruning policy is implemented.

Both the Android pages and API use the same `SmsMessageStore` instance, so totals and history reflect the same records. The database is in the app's private data directory; uninstalling the app removes that private data.

Source: [SmsMessageStore.cs](SimSmsGateway.Core/SmsMessageStore.cs) and [App.xaml.cs](SimSmsGateway/App.xaml.cs).

## Browser playground and network behavior

`web/server.js` serves the static browser files with Node's built-in HTTP server. It serves GET requests only, uses port `8766` by default (overridable with `PORT`), binds to `127.0.0.1`, and applies a content security policy. It is for local playground hosting, not the phone's SMS API.

The browser performs direct `fetch` calls to the phone's address. The phone's CORS headers permit these cross-origin calls, but:

- `localhost` in a browser means the machine running that browser. Use the phone's reachable LAN address from another device.
- Both devices need network connectivity to the phone; guest Wi-Fi or client isolation can prevent access.
- A page served over HTTPS may be blocked by browser mixed-content rules when it calls the phone over HTTP.
- The API key is stored in browser `sessionStorage`; the server URL, country preference, and theme are stored in `localStorage`. Do not use a shared browser for sensitive credentials.

Source: [server.js](web/server.js) and [app.js](web/app.js). The API's machine-readable contract is in [openapi.json](web/openapi.json).

## Source map and extension points

| Source | Responsibility |
|---|---|
| `SimSmsGateway/App.xaml.cs` | MAUI app startup, theme preference, and shared gateway/store construction. |
| `SimSmsGateway/AppShell.xaml` | Android bottom tabs; `AppShell.xaml.cs` registers detail routes. |
| `SimSmsGateway/GatewayHost.cs` | API key, port, listener lifecycle, and local address discovery. |
| `SimSmsGateway.Core/SmsApiServer.cs` | HTTP parser, routes, auth, CORS, request/response behavior, and send orchestration. |
| `SimSmsGateway.Core/SmsRequestValidator.cs` | E.164, message length, and bulk rules. |
| `SimSmsGateway.Core/ISmsSender.cs` | Platform-neutral SMS sender interface and request/result types. |
| `SimSmsGateway/Platforms/Android/AndroidSmsSender.cs` | Android `SmsManager` integration. |
| `SimSmsGateway/Platforms/Android/SmsPermission.cs` and `AndroidManifest.xml` | Android SMS permission declaration and runtime request. |
| `SimSmsGateway.Core/SmsMessageStore.cs` | SQLite schema, writes, history, and aggregates. |
| `SimSmsGateway.Tests/` | Offline API test harness; tests use a fake sender and do not send real SMS. |
| `web/` | Static browser playground, API documentation, and OpenAPI specification. |

The platform boundary is `ISmsSender`: the core API can be tested with a fake sender without Android hardware. The mobile app supplies `AndroidSmsSender` for actual SMS submission.

## Build, install, and test

From the project directory:

```powershell
dotnet build .\SimSmsGateway.sln
dotnet build .\SimSmsGateway\SimSmsGateway.csproj -t:Install -f net10.0-android
dotnet run --project .\SimSmsGateway.Tests\SimSmsGateway.Tests.csproj
```

Run the browser playground from its directory:

```powershell
npm start
```

The offline API tests use a fake sender and should not be used as evidence of carrier delivery. Sending a real SMS requires the Android app, SMS permission, a usable SIM, and an authorized recipient.
