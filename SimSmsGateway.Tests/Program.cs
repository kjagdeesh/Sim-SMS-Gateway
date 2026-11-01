using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using SimSmsGateway.Core;

var failures = new List<string>();
var sender = new FakeSmsSender();
await using var server = new SmsApiServer(sender, "test-key");
await server.StartAsync(IPAddress.Loopback, 0);

using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{server.Port}") };

var health = await client.GetAsync("/health");
Check(health.StatusCode == HttpStatusCode.OK, "health returns 200");
Check((await health.Content.ReadAsStringAsync()).Contains("\"status\":\"online\"", StringComparison.Ordinal), "health reports online");

var unauthorized = await PostSmsAsync(client, null, "+14155550123", "hello");
Check(unauthorized.StatusCode == HttpStatusCode.Unauthorized, "send requires bearer authentication");
Check(sender.Messages.Count == 0, "unauthenticated requests do not reach the sender");

var invalidNumber = await PostSmsAsync(client, "test-key", "4155550123", "hello");
Check(invalidNumber.StatusCode == HttpStatusCode.BadRequest, "invalid destination is rejected");
Check(sender.Messages.Count == 0, "invalid requests do not reach the sender");

var oversizedMessage = await PostSmsAsync(client, "test-key", "+14155550123", new string('x', SmsRequestValidator.MaximumMessageLength + 1));
Check(oversizedMessage.StatusCode == HttpStatusCode.BadRequest, "oversized message is rejected");

var wrongContentType = await PostSmsAsync(client, "test-key", "+14155550123", "hello", "application/jsonp");
Check(wrongContentType.StatusCode == HttpStatusCode.UnsupportedMediaType, "non-JSON content type is rejected");

using (var malformedJson = new HttpRequestMessage(HttpMethod.Post, "/api/v1/messages")
{
    Content = new StringContent("{", Encoding.UTF8, "application/json")
})
{
    malformedJson.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "test-key");
    var malformedResponse = await client.SendAsync(malformedJson);
    Check(malformedResponse.StatusCode == HttpStatusCode.BadRequest, "malformed JSON is rejected");
}

var accepted = await PostSmsAsync(client, "test-key", "+14155550123", "Hello from the SIM");
Check(accepted.StatusCode == HttpStatusCode.Accepted, "valid message returns 202");
Check(sender.Messages.Count == 1, "valid message reaches the sender once");
Check(sender.Messages[0] == ("+14155550123", "Hello from the SIM"), "sender receives the requested number and text");
Check((await accepted.Content.ReadAsStringAsync()).Contains("delivery is not confirmed", StringComparison.Ordinal), "response does not claim delivery");

var unavailable = await PostSmsAsync(client, "test-key", "+14155550124", "fail");
Check(unavailable.StatusCode == HttpStatusCode.ServiceUnavailable, "SIM sender errors are returned explicitly");

using var authedGet = new HttpClient { BaseAddress = client.BaseAddress };
authedGet.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-key");

var historyUnauthorized = await client.GetAsync("/api/v1/messages");
Check(historyUnauthorized.StatusCode == HttpStatusCode.Unauthorized, "history requires bearer authentication");

var bulkUnauthorized = await PostJsonAsync(client, null, "/api/v1/messages/bulk", """{"recipients":["+14155550123"],"message":"hi"}""");
Check(bulkUnauthorized.StatusCode == HttpStatusCode.Unauthorized, "bulk send requires bearer authentication");

var bulkEmpty = await PostJsonAsync(client, "test-key", "/api/v1/messages/bulk", """{"recipients":[],"message":"hi"}""");
Check(bulkEmpty.StatusCode == HttpStatusCode.BadRequest, "bulk rejects an empty recipient list");

var bulkInvalid = await PostJsonAsync(client, "test-key", "/api/v1/messages/bulk", """{"recipients":["+14155550123","12345"],"message":"hi"}""");
Check(bulkInvalid.StatusCode == HttpStatusCode.BadRequest, "bulk rejects an invalid recipient without sending any");

var bulkDuplicate = await PostJsonAsync(client, "test-key", "/api/v1/messages/bulk", """{"recipients":["+14155550123","+14155550123"],"message":"hi"}""");
Check(bulkDuplicate.StatusCode == HttpStatusCode.BadRequest, "bulk rejects duplicate recipients");

var tooMany = JsonSerializer.Serialize(new
{
    recipients = Enumerable.Range(0, SmsRequestValidator.MaximumBulkRecipients + 1).Select(i => $"+1415555{i:D4}"),
    message = "hi"
});
var bulkTooMany = await PostJsonAsync(client, "test-key", "/api/v1/messages/bulk", tooMany);
Check(bulkTooMany.StatusCode == HttpStatusCode.BadRequest, "bulk rejects more than the recipient limit");

var beforeBulk = sender.Messages.Count;
var bulk = await PostJsonAsync(client, "test-key", "/api/v1/messages/bulk", """{"recipients":["+14155550201","+14155550202","+14155550203"],"message":"Hi all"}""");
Check(bulk.StatusCode == HttpStatusCode.OK, "bulk returns 200");
using (var bulkJson = JsonDocument.Parse(await bulk.Content.ReadAsStringAsync()))
{
    Check(bulkJson.RootElement.GetProperty("total").GetInt32() == 3, "bulk reports total");
    Check(bulkJson.RootElement.GetProperty("success").GetInt32() == 3, "bulk reports success count");
    Check(bulkJson.RootElement.GetProperty("results").GetArrayLength() == 3, "bulk reports per-recipient results");
}
Check(sender.Messages.Count - beforeBulk == 3, "bulk sends one message per recipient");

var failBulk = await PostJsonAsync(client, "test-key", "/api/v1/messages/bulk", """{"recipients":["+14155550301","+14155550302"],"message":"fail"}""");
using (var failJson = JsonDocument.Parse(await failBulk.Content.ReadAsStringAsync()))
{
    Check(failJson.RootElement.GetProperty("failed").GetInt32() == 2, "bulk reports failed recipients");
    Check(failJson.RootElement.GetProperty("results")[0].GetProperty("error").GetString() == "SIM unavailable.", "bulk includes the failure reason");
}

// Totals so far: 1 single success + 1 single failure + 3 bulk success + 2 bulk failures.
using (var stats = JsonDocument.Parse(await authedGet.GetStringAsync("/api/v1/stats")))
{
    Check(stats.RootElement.GetProperty("total").GetInt32() == 7, "stats count every attempted send");
    Check(stats.RootElement.GetProperty("success").GetInt32() == 4, "stats count successes");
    Check(stats.RootElement.GetProperty("failed").GetInt32() == 3, "stats count failures");
}

using (var all = JsonDocument.Parse(await authedGet.GetStringAsync("/api/v1/messages")))
{
    var items = all.RootElement.GetProperty("items");
    Check(items.GetArrayLength() == 7, "history lists stored messages");
    Check(items[0].GetProperty("destination").GetString() == "+14155550302", "history is newest first");
    Check(items[0].GetProperty("status").GetString() == "failed", "history records failed status");
}

using (var failedOnly = JsonDocument.Parse(await authedGet.GetStringAsync("/api/v1/messages?status=failed&limit=2")))
{
    var items = failedOnly.RootElement.GetProperty("items");
    Check(items.GetArrayLength() == 2 && items.EnumerateArray().All(i => i.GetProperty("status").GetString() == "failed"), "history filters by status and limit");
}

Check((await authedGet.GetAsync("/api/v1/messages?status=bogus")).StatusCode == HttpStatusCode.BadRequest, "history rejects an unknown status filter");

var dbPath = Path.Combine(Path.GetTempPath(), $"sim-sms-gateway-test-{Guid.NewGuid():N}.db");
try
{
    using (var fileStore = new SmsMessageStore(dbPath))
    {
        fileStore.Add(null, "+14155550123", "persisted", SmsStatus.Success, null);
    }

    using var reopened = new SmsMessageStore(dbPath);
    Check(reopened.List(null, 10, 0).Count == 1, "message history persists in the SQLite file");
}
finally
{
    Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    File.Delete(dbPath);
}

using var preflight = new HttpRequestMessage(HttpMethod.Options, "/api/v1/messages");
preflight.Headers.Add("Origin", "http://localhost:8766");
var options = await client.SendAsync(preflight);
Check(options.StatusCode == HttpStatusCode.NoContent, "browser preflight is supported");
Check(options.Headers.TryGetValues("Access-Control-Allow-Origin", out var cors) && cors.Contains("*"), "preflight permits the browser playground origin");

await server.StopAsync();
try
{
    await client.GetAsync("/health");
    failures.Add("stopped server refuses connections");
}
catch (HttpRequestException)
{
    Console.WriteLine("PASS stopped server refuses connections");
}

if (failures.Count > 0)
{
    foreach (var failure in failures)
    {
        Console.Error.WriteLine($"FAIL {failure}");
    }

    return 1;
}

return 0;

void Check(bool condition, string description)
{
    if (condition)
    {
        Console.WriteLine($"PASS {description}");
    }
    else
    {
        failures.Add(description);
    }
}

static async Task<HttpResponseMessage> PostSmsAsync(
    HttpClient client,
    string? apiKey,
    string destination,
    string message,
    string contentType = "application/json")
{
    using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/messages")
    {
        Content = new StringContent(
            JsonSerializer.Serialize(new SmsMessageRequest(destination, message)),
            Encoding.UTF8,
            contentType)
    };

    if (apiKey is not null)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
    }

    return await client.SendAsync(request);
}

static async Task<HttpResponseMessage> PostJsonAsync(HttpClient client, string? apiKey, string path, string json)
{
    using var request = new HttpRequestMessage(HttpMethod.Post, path)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    if (apiKey is not null)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
    }

    return await client.SendAsync(request);
}

sealed class FakeSmsSender : ISmsSender
{
    public List<(string Destination, string Message)> Messages { get; } = [];

    public Task SendAsync(string destination, string message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (message == "fail")
        {
            throw new SmsSendException("SIM unavailable.");
        }

        Messages.Add((destination, message));
        return Task.CompletedTask;
    }
}
