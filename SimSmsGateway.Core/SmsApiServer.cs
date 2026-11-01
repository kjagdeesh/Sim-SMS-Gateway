using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SimSmsGateway.Core;

public sealed class SmsApiServer : IAsyncDisposable
{
    private const int MaximumHeaderLength = 16 * 1024;
    private const int MaximumBodyLength = 32 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ISmsSender _smsSender;
    private readonly SmsMessageStore _store;
    private readonly bool _ownsStore;
    private readonly byte[] _apiKeyBytes;
    private TcpListener? _listener;
    private CancellationTokenSource? _shutdown;
    private Task? _acceptTask;

    public SmsApiServer(ISmsSender smsSender, string apiKey, SmsMessageStore? store = null)
    {
        ArgumentNullException.ThrowIfNull(smsSender);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);

        _smsSender = smsSender;
        _apiKeyBytes = Encoding.UTF8.GetBytes(apiKey);
        _ownsStore = store is null;
        _store = store ?? new SmsMessageStore();
    }

    public int Port { get; private set; }

    public Task StartAsync(IPAddress address, int port, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (_listener is not null)
        {
            throw new InvalidOperationException("The SMS API server is already running.");
        }

        _listener = new TcpListener(address, port);
        _listener.Start(16);
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _shutdown = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _acceptTask = AcceptClientsAsync(_listener, _shutdown.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        var listener = _listener;
        var shutdown = _shutdown;
        var acceptTask = _acceptTask;
        if (listener is null || shutdown is null || acceptTask is null)
        {
            return;
        }

        _listener = null;
        _shutdown = null;
        _acceptTask = null;
        shutdown.Cancel();
        listener.Stop();

        try
        {
            await acceptTask.ConfigureAwait(false);
        }
        finally
        {
            shutdown.Dispose();
            Port = 0;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        if (_ownsStore)
        {
            _store.Dispose();
        }
    }

    private async Task AcceptClientsAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (SocketException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            using (client)
            using (var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                requestTimeout.CancelAfter(TimeSpan.FromSeconds(60));
                try
                {
                    await HandleClientAsync(client.GetStream(), requestTimeout.Token).ConfigureAwait(false);
                }
                catch (IOException)
                {
                }
                catch (SocketException)
                {
                }
                catch (OperationCanceledException) when (requestTimeout.IsCancellationRequested)
                {
                }
            }
        }
    }

    private async Task HandleClientAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var request = await ReadRequestAsync(stream, cancellationToken).ConfigureAwait(false);
        if (request is null)
        {
            return;
        }

        if (request.Method == "OPTIONS")
        {
            await WriteResponseAsync(stream, 204, "No Content", string.Empty, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (request.Method == "GET" && request.Path == "/health")
        {
            await WriteJsonAsync(stream, 200, new { status = "online" }, cancellationToken).ConfigureAwait(false);
            return;
        }

        var isSend = request.Method == "POST" && request.Path == "/api/v1/messages";
        var isBulk = request.Method == "POST" && request.Path == "/api/v1/messages/bulk";
        var isHistory = request.Method == "GET" && request.Path == "/api/v1/messages";
        var isStats = request.Method == "GET" && request.Path == "/api/v1/stats";
        if (!isSend && !isBulk && !isHistory && !isStats)
        {
            await WriteJsonAsync(stream, 404, new { error = "Endpoint not found." }, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!HasValidApiKey(request.Headers))
        {
            await WriteJsonAsync(stream, 401, new { error = "A valid bearer API key is required." }, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (isStats)
        {
            await WriteJsonAsync(stream, 200, _store.GetStats(), cancellationToken).ConfigureAwait(false);
            return;
        }

        if (isHistory)
        {
            await HandleHistoryAsync(stream, request.Query, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!request.Headers.TryGetValue("Content-Type", out var contentType) ||
            !contentType.Split(';', 2)[0].Trim().Equals("application/json", StringComparison.OrdinalIgnoreCase))
        {
            await WriteJsonAsync(stream, 415, new { error = "Content-Type must be application/json." }, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (isBulk)
        {
            await HandleBulkAsync(stream, request.Body, cancellationToken).ConfigureAwait(false);
            return;
        }

        SmsMessageRequest? messageRequest;
        try
        {
            messageRequest = JsonSerializer.Deserialize<SmsMessageRequest>(request.Body, JsonOptions);
        }
        catch (JsonException)
        {
            await WriteJsonAsync(stream, 400, new { error = "Request body must be valid JSON." }, cancellationToken).ConfigureAwait(false);
            return;
        }

        var validationError = SmsRequestValidator.Validate(messageRequest);
        if (validationError is not null)
        {
            await WriteJsonAsync(stream, 400, new { error = validationError }, cancellationToken).ConfigureAwait(false);
            return;
        }

        var record = await TrySendAsync(null, messageRequest!.To!, messageRequest.Message!, cancellationToken).ConfigureAwait(false);
        if (record.Status == SmsStatus.Failed)
        {
            await WriteJsonAsync(stream, 503, new { error = record.Error, id = record.Id, status = record.Status }, cancellationToken).ConfigureAwait(false);
            return;
        }

        await WriteJsonAsync(
            stream,
            202,
            new { status = "accepted", id = record.Id, detail = "Android accepted the SMS request; carrier delivery is not confirmed." },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task HandleBulkAsync(Stream stream, byte[] body, CancellationToken cancellationToken)
    {
        SmsBulkRequest? bulk;
        try
        {
            bulk = JsonSerializer.Deserialize<SmsBulkRequest>(body, JsonOptions);
        }
        catch (JsonException)
        {
            await WriteJsonAsync(stream, 400, new { error = "Request body must be valid JSON." }, cancellationToken).ConfigureAwait(false);
            return;
        }

        var validationError = SmsRequestValidator.ValidateBulk(bulk);
        if (validationError is not null)
        {
            await WriteJsonAsync(stream, 400, new { error = validationError }, cancellationToken).ConfigureAwait(false);
            return;
        }

        var batchId = Guid.NewGuid().ToString("N");
        var results = new List<object>();
        var succeeded = 0;
        foreach (var recipient in bulk!.Recipients!)
        {
            var record = await TrySendAsync(batchId, recipient!, bulk.Message!, cancellationToken).ConfigureAwait(false);
            if (record.Status == SmsStatus.Success)
            {
                succeeded++;
            }

            results.Add(new { id = record.Id, to = record.Destination, status = record.Status, error = record.Error });
        }

        await WriteJsonAsync(
            stream,
            200,
            new
            {
                batchId,
                total = results.Count,
                success = succeeded,
                failed = results.Count - succeeded,
                detail = "Success means Android accepted the SMS request; carrier delivery is not confirmed.",
                results
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task HandleHistoryAsync(Stream stream, string query, CancellationToken cancellationToken)
    {
        var parameters = ParseQuery(query);
        string? status = null;
        if (parameters.TryGetValue("status", out var statusText) && statusText.Length > 0)
        {
            if (statusText != SmsStatus.Success && statusText != SmsStatus.Failed)
            {
                await WriteJsonAsync(stream, 400, new { error = "status must be 'success' or 'failed'." }, cancellationToken).ConfigureAwait(false);
                return;
            }

            status = statusText;
        }

        var limit = 50;
        var offset = 0;
        if ((parameters.TryGetValue("limit", out var limitText) && (!int.TryParse(limitText, out limit) || limit < 1 || limit > 200)) ||
            (parameters.TryGetValue("offset", out var offsetText) && (!int.TryParse(offsetText, out offset) || offset < 0)))
        {
            await WriteJsonAsync(stream, 400, new { error = "limit must be 1-200 and offset must be 0 or greater." }, cancellationToken).ConfigureAwait(false);
            return;
        }

        var items = _store.List(status, limit, offset);
        await WriteJsonAsync(stream, 200, new { items, limit, offset }, cancellationToken).ConfigureAwait(false);
    }

    private async Task<SmsMessageRecord> TrySendAsync(string? batchId, string destination, string message, CancellationToken cancellationToken)
    {
        try
        {
            await _smsSender.SendAsync(destination, message, cancellationToken).ConfigureAwait(false);
            return _store.Add(batchId, destination, message, SmsStatus.Success, null);
        }
        catch (SmsSendException exception)
        {
            return _store.Add(batchId, destination, message, SmsStatus.Failed, exception.Message);
        }
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            result[Uri.UnescapeDataString(parts[0])] = parts.Length > 1 ? Uri.UnescapeDataString(parts[1].Replace('+', ' ')) : string.Empty;
        }

        return result;
    }

    private bool HasValidApiKey(IReadOnlyDictionary<string, string> headers)
    {
        if (!headers.TryGetValue("Authorization", out var authorization) ||
            !authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var supplied = Encoding.UTF8.GetBytes(authorization["Bearer ".Length..]);
        return supplied.Length == _apiKeyBytes.Length &&
            CryptographicOperations.FixedTimeEquals(supplied, _apiKeyBytes);
    }

    private static async Task<HttpRequest?> ReadRequestAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var headerBuffer = new MemoryStream();
        var oneByte = new byte[1];
        var foundTerminator = false;
        while (headerBuffer.Length < MaximumHeaderLength)
        {
            var read = await stream.ReadAsync(oneByte, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return null;
            }

            headerBuffer.WriteByte(oneByte[0]);
            if (headerBuffer.Length >= 4 &&
                oneByte[0] == '\n' &&
                HasHeaderTerminator(headerBuffer))
            {
                foundTerminator = true;
                break;
            }
        }

        if (!foundTerminator)
        {
            await WriteJsonAsync(stream, 431, new { error = "Request headers are too large." }, cancellationToken).ConfigureAwait(false);
            return null;
        }

        var headerBytes = headerBuffer.ToArray();
        var headerText = Encoding.ASCII.GetString(headerBytes, 0, headerBytes.Length - 4);
        var lines = headerText.Split("\r\n", StringSplitOptions.None);
        var requestLine = lines[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (requestLine.Length != 3 || !requestLine[2].StartsWith("HTTP/", StringComparison.Ordinal))
        {
            await WriteJsonAsync(stream, 400, new { error = "Malformed HTTP request line." }, cancellationToken).ConfigureAwait(false);
            return null;
        }

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines.Skip(1))
        {
            var separator = line.IndexOf(':');
            if (separator <= 0)
            {
                await WriteJsonAsync(stream, 400, new { error = "Malformed HTTP header." }, cancellationToken).ConfigureAwait(false);
                return null;
            }

            var name = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            if (!headers.TryAdd(name, value))
            {
                await WriteJsonAsync(stream, 400, new { error = "Duplicate HTTP headers are not supported." }, cancellationToken).ConfigureAwait(false);
                return null;
            }
        }

        var contentLength = 0;
        if (headers.TryGetValue("Content-Length", out var contentLengthText) &&
            (!int.TryParse(contentLengthText, out contentLength) || contentLength < 0))
        {
            await WriteJsonAsync(stream, 400, new { error = "Invalid Content-Length." }, cancellationToken).ConfigureAwait(false);
            return null;
        }

        if (contentLength > MaximumBodyLength)
        {
            await WriteJsonAsync(stream, 413, new { error = "Request body is too large." }, cancellationToken).ConfigureAwait(false);
            return null;
        }

        var body = new byte[contentLength];
        var bodyRead = 0;
        while (bodyRead < body.Length)
        {
            var read = await stream.ReadAsync(body.AsMemory(bodyRead), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                await WriteJsonAsync(stream, 400, new { error = "Request body ended unexpectedly." }, cancellationToken).ConfigureAwait(false);
                return null;
            }

            bodyRead += read;
        }

        var target = requestLine[1].Split('?', 2);
        return new HttpRequest(requestLine[0].ToUpperInvariant(), target[0], target.Length > 1 ? target[1] : string.Empty, headers, body);
    }

    private static bool HasHeaderTerminator(MemoryStream buffer)
    {
        var bytes = buffer.GetBuffer();
        var length = (int)buffer.Length;
        return bytes[length - 4] == '\r' &&
            bytes[length - 3] == '\n' &&
            bytes[length - 2] == '\r' &&
            bytes[length - 1] == '\n';
    }

    private static Task WriteJsonAsync(Stream stream, int statusCode, object value, CancellationToken cancellationToken) =>
        WriteResponseAsync(stream, statusCode, ReasonPhrase(statusCode), JsonSerializer.Serialize(value, JsonOptions), cancellationToken, "application/json; charset=utf-8");

    private static async Task WriteResponseAsync(
        Stream stream,
        int statusCode,
        string reasonPhrase,
        string body,
        CancellationToken cancellationToken,
        string contentType = "text/plain; charset=utf-8")
    {
        var bodyBytes = Encoding.UTF8.GetBytes(body);
        var headers = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {statusCode} {reasonPhrase}\r\n" +
            $"Content-Type: {contentType}\r\n" +
            $"Content-Length: {bodyBytes.Length}\r\n" +
            "Access-Control-Allow-Origin: *\r\n" +
            "Access-Control-Allow-Methods: GET, POST, OPTIONS\r\n" +
            "Access-Control-Allow-Headers: Authorization, Content-Type\r\n" +
            "Access-Control-Max-Age: 600\r\n" +
            "Connection: close\r\n\r\n");

        await stream.WriteAsync(headers, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(bodyBytes, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string ReasonPhrase(int statusCode) => statusCode switch
    {
        200 => "OK",
        202 => "Accepted",
        204 => "No Content",
        400 => "Bad Request",
        401 => "Unauthorized",
        404 => "Not Found",
        413 => "Content Too Large",
        415 => "Unsupported Media Type",
        431 => "Request Header Fields Too Large",
        503 => "Service Unavailable",
        _ => "Error"
    };

    private sealed record HttpRequest(
        string Method,
        string Path,
        string Query,
        IReadOnlyDictionary<string, string> Headers,
        byte[] Body);
}
