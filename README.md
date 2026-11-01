# Sim SMS Gateway

Sim SMS Gateway is a self-hosted Android SMS gateway. The Android app exposes a small local HTTP API and submits SMS messages through the phone's default SIM using Android's `SmsManager`. It does not use a cloud SMS service.

## Requirements

- .NET 10 SDK with the Android workload
- An Android phone with a SIM that can send SMS
- Node.js 18+ to serve the browser playground

## Run the Android gateway

From the repository root:

```powershell
dotnet build .\build\sim-sms-gateway\SimSmsGateway.sln
dotnet build .\build\sim-sms-gateway\SimSmsGateway\SimSmsGateway.csproj -t:Install -f net10.0-android
```

Open `build/sim-sms-gateway/SimSmsGateway.sln` in Visual Studio to work with the Android app, shared core library, and offline test harness together.

Open Sim SMS Gateway on the phone and tap **Start server**. Grant SMS permission when prompted. Keep the app open while using the gateway. The app shows localhost and available Wi-Fi addresses, plus a generated API key.

## Run the API playground

From `build/sim-sms-gateway/web/`:

```powershell
npm start
```

Open `http://localhost:8766`. Enter the server URL and API key from the Android app. If the browser is on a different device, use the phone's Wi-Fi address rather than `localhost`; both devices must be on the same trusted network.

You can also open `web/index.html` with VS Code Live Server. Its CSS, JavaScript, and favicon use paths relative to the playground folder.

## API

`GET /health` responds with `{"status":"online"}`.

`POST /api/v1/messages` requires `Authorization: Bearer <key>` and an `application/json` body:

```json
{
  "to": "+14155550123",
  "message": "Hello from my SIM"
}
```

The destination must be an E.164 number. A `202 Accepted` response means Android accepted the request for SMS submission; carrier delivery is not confirmed.

`POST /api/v1/messages/bulk` sends one message to up to 100 unique recipients (`{"recipients": ["+14155550123", ...], "message": "..."}`) and returns per-recipient `success`/`failed` results.

`GET /api/v1/messages?status=&limit=&offset=` returns the stored history, and `GET /api/v1/stats` returns total/success/failed counts. Every attempted send is stored in a SQLite file (`messages.db`) in the app's private storage.

The website has a side menu with Home, Dashboard (history and totals), Send Messages (single and bulk), API Documentation (per-endpoint docs, code examples and live try-it), and Developer & Project. An OpenAPI spec is at `web/openapi.json`.

## Safety and limitations

- SMS permission is required. Messages use the phone's default SMS subscription and carrier plan; charges may apply.
- The server listens on the phone's network interfaces on port `8765`. The bearer key protects the send endpoint, but HTTP does not encrypt traffic. Use a trusted Wi-Fi network and never port-forward or expose this server to the public internet.
- The browser playground does not save the server URL or API key.
- The first version runs while the app stays open; stopping the server or closing the app disables the API.
- Android app distribution stores may restrict SMS permissions. Install only on devices and accounts you are authorized to use.
- Delivery reports, scheduled sends, and cloud providers are intentionally out of scope.

## Offline API tests

```powershell
dotnet run --project .\build\sim-sms-gateway\SimSmsGateway.Tests\SimSmsGateway.Tests.csproj
```

The tests use a fake sender and never send a real SMS.
