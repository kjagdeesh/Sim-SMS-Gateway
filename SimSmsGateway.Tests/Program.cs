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
