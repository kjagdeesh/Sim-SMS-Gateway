using System.Text.Json;
using System.Text.RegularExpressions;

namespace SimSmsGateway;

public partial class ApiDocumentationPage : ContentPage
{
    private sealed record Endpoint(string Method, string Path, string Title, string Summary, string? Auth, string? Body, string Curl);

    private static readonly Regex SourceCodeTokenPattern = new(
        @"(?<string>""(?:\\.|[^""\\])*""|'(?:\\.|[^'\\])*'|`(?:\\.|[^`\\])*`)|(?<comment>//.*|#.*)|(?<keyword>\b(?:const|let|await|async|if|throw|new|import|from|with|as|try|except|print|True|False|None|method|headers|body)\b)|(?<number>\b\d+\b)|(?<punctuation>[{}()\[\],.:])",
        RegexOptions.Compiled);

    private static readonly Endpoint[] Items =
    [
        new("GET", "/health", "Health check", "Confirms the gateway is responding. No API key required.", null, "{ \"status\": \"online\" }",
            "curl BASE_URL/health"),
        new("POST", "/api/v1/messages", "Send one SMS",
            "Sends one message. 202 means Android accepted the request; it does not confirm carrier delivery. Failed sends return 503 and are recorded.",
            "Bearer key", "{\"to\":\"+919876543210\",\"message\":\"Hello\"}",
            "curl -X POST BASE_URL/api/v1/messages -H \"Authorization: Bearer YOUR_API_KEY\" -H \"Content-Type: application/json\" -d '{\"to\":\"+919876543210\",\"message\":\"Hello\"}'"),
        new("POST", "/api/v1/messages/bulk", "Send bulk SMS",
            "Sends one message to 1–100 unique recipients and returns a success or failed result for each number.",
            "Bearer key", "{\"recipients\":[\"+919876543210\",\"+14155550123\"],\"message\":\"Hello\"}",
            "curl -X POST BASE_URL/api/v1/messages/bulk -H \"Authorization: Bearer YOUR_API_KEY\" -H \"Content-Type: application/json\" -d '{\"recipients\":[\"+919876543210\"],\"message\":\"Hello\"}'"),
        new("GET", "/api/v1/messages", "List message history",
            "Returns saved messages, newest first. Query: status=success|failed, limit=1–200, offset=0+.",
            "Bearer key", "?status=failed&limit=25&offset=0",
            "curl \"BASE_URL/api/v1/messages?limit=25\" -H \"Authorization: Bearer YOUR_API_KEY\""),
        new("GET", "/api/v1/stats", "Message totals", "Returns total, success and failed counts from the on-device database.",
            "Bearer key", "{ \"total\": 12, \"success\": 10, \"failed\": 2 }",
            "curl BASE_URL/api/v1/stats -H \"Authorization: Bearer YOUR_API_KEY\"")
    ];

    public ApiDocumentationPage()
    {
        InitializeComponent();
        foreach (var endpoint in Items)
        {
            Endpoints.Add(BuildCard(endpoint));
        }

        PopupDialog.Attach(this);
    }

    private Border BuildCard(Endpoint endpoint)
    {
        var isPost = endpoint.Method == "POST";
        var badge = new Border
        {
            Padding = new Thickness(10, 4),
            StrokeThickness = 0,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 },
            BackgroundColor = Color.FromArgb(isPost ? "#26F59E0B" : "#2622C55E"),
            VerticalOptions = LayoutOptions.Center,
            Content = new Label
            {
                Text = endpoint.Method,
                FontFamily = "OpenSansSemibold",
                FontSize = 11,
                TextColor = Color.FromArgb(isPost ? "#D97706" : "#16A34A")
            }
        };

        var header = new Grid { ColumnDefinitions = [new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star)], ColumnSpacing = 10 };
        header.Add(badge, 0, 0);
        header.Add(new Label { StyleClass = ["Mono"], Text = endpoint.Path, VerticalOptions = LayoutOptions.Center }, 1, 0);

        var stack = new VerticalStackLayout { Spacing = 10 };
        stack.Add(header);
        stack.Add(new Label { StyleClass = ["Heading"], FontSize = 16, Text = endpoint.Title });
        stack.Add(new Label { StyleClass = ["Body"], Text = endpoint.Summary });
        stack.Add(new Label { StyleClass = ["Muted"], Text = endpoint.Auth is null ? "No API key needed" : "Requires API key" });
        stack.Add(new Label { StyleClass = ["Heading"], FontSize = 14, Text = "Request details" });
        stack.Add(new Label { StyleClass = ["Body"], Text = GetRequestDetails(endpoint) });

        if (endpoint.Body is not null)
        {
            if (endpoint.Body.StartsWith('{') || endpoint.Body.StartsWith('['))
            {
                var json = PrettyPrintJson(endpoint.Body);
                var jsonTitle = endpoint.Method == "POST" ? "JSON request body" : "JSON response example";
                stack.Add(BuildCodeWindow(jsonTitle, json, FormatJson(json)));
            }
            else
            {
                stack.Add(BuildCodeWindow("Query parameters", endpoint.Body, FormatPlain(endpoint.Body)));
            }
        }

        stack.Add(BuildCodeWindow("cURL example", endpoint.Curl, FormatCurl(endpoint.Curl)));
        var javaScript = GetJavaScriptExample(endpoint);
        stack.Add(BuildCodeWindow("JavaScript example", javaScript, FormatSourceCode(javaScript)));
        var python = GetPythonExample(endpoint);
        stack.Add(BuildCodeWindow("Python example", python, FormatSourceCode(python)));

        return new Border { StyleClass = ["Card"], Content = stack };
    }

    private static string GetRequestDetails(Endpoint endpoint) => endpoint.Path switch
    {
        "/health" =>
            "Method: GET\nURL: {BASE_URL}/health\nHeaders: None\nRequest body: None\nSuccess: HTTP 200 returns the online status.",
        "/api/v1/messages" when endpoint.Method == "POST" =>
            "Method: POST\nURL: {BASE_URL}/api/v1/messages\nHeaders: Authorization: Bearer YOUR_API_KEY; Content-Type: application/json\nRequest JSON: to is an E.164 number (+ followed by 8-15 digits); message is required and limited to 1600 characters.\nSuccess: HTTP 202 means Android accepted the request, not that the carrier delivered it. Invalid input returns 400, an invalid key 401, and a SIM send failure 503.",
        "/api/v1/messages/bulk" =>
            "Method: POST\nURL: {BASE_URL}/api/v1/messages/bulk\nHeaders: Authorization: Bearer YOUR_API_KEY; Content-Type: application/json\nRequest JSON: recipients must contain 1-100 unique E.164 numbers; message is required and limited to 1600 characters.\nSuccess: HTTP 200 returns batchId, total, success, failed, and a result for each recipient. Invalid input returns 400.",
        "/api/v1/messages" =>
            "Method: GET\nURL: {BASE_URL}/api/v1/messages\nHeaders: Authorization: Bearer YOUR_API_KEY\nQuery parameters: status is optional (success or failed); limit is optional (1-200, default 50); offset is optional (0 or greater, default 0).\nRequest body: None. Success returns items, limit, and offset; invalid query values return 400.",
        "/api/v1/stats" =>
            "Method: GET\nURL: {BASE_URL}/api/v1/stats\nHeaders: Authorization: Bearer YOUR_API_KEY\nRequest body: None\nSuccess: HTTP 200 returns total, success, and failed message counts. An invalid key returns 401.",
        _ => throw new InvalidOperationException($"No request details are defined for {endpoint.Method} {endpoint.Path}.")
    };

    private static string GetJavaScriptExample(Endpoint endpoint) => endpoint.Path switch
    {
        "/health" => """
            const BASE_URL = "http://PHONE_IP:8765";

            const response = await fetch(`${BASE_URL}/health`);
            const data = await response.json();
            if (!response.ok) {
              throw new Error(`HTTP ${response.status}: ${JSON.stringify(data)}`);
            }
            console.log(data);
            """,
        "/api/v1/messages" when endpoint.Method == "POST" => """
            const BASE_URL = "http://PHONE_IP:8765";
            const API_KEY = "YOUR_API_KEY";

            const response = await fetch(`${BASE_URL}/api/v1/messages`, {
              method: "POST",
              headers: {
                Authorization: `Bearer ${API_KEY}`,
                "Content-Type": "application/json",
              },
              body: JSON.stringify({
                to: "+919876543210",
                message: "Hello",
              }),
            });
            const data = await response.json();
            if (!response.ok) {
              throw new Error(`HTTP ${response.status}: ${JSON.stringify(data)}`);
            }
            console.log(data);
            """,
        "/api/v1/messages/bulk" => """
            const BASE_URL = "http://PHONE_IP:8765";
            const API_KEY = "YOUR_API_KEY";

            const response = await fetch(`${BASE_URL}/api/v1/messages/bulk`, {
              method: "POST",
              headers: {
                Authorization: `Bearer ${API_KEY}`,
                "Content-Type": "application/json",
              },
              body: JSON.stringify({
                recipients: ["+919876543210", "+14155550123"],
                message: "Hello",
              }),
            });
            const data = await response.json();
            if (!response.ok) {
              throw new Error(`HTTP ${response.status}: ${JSON.stringify(data)}`);
            }
            console.log(data);
            """,
        "/api/v1/messages" => """
            const BASE_URL = "http://PHONE_IP:8765";
            const API_KEY = "YOUR_API_KEY";
            const query = new URLSearchParams({
              status: "failed",
              limit: "25",
              offset: "0",
            });

            const response = await fetch(
              `${BASE_URL}/api/v1/messages?${query}`,
              { headers: { Authorization: `Bearer ${API_KEY}` } },
            );
            const data = await response.json();
            if (!response.ok) {
              throw new Error(`HTTP ${response.status}: ${JSON.stringify(data)}`);
            }
            console.log(data);
            """,
        "/api/v1/stats" => """
            const BASE_URL = "http://PHONE_IP:8765";
            const API_KEY = "YOUR_API_KEY";

            const response = await fetch(`${BASE_URL}/api/v1/stats`, {
              headers: { Authorization: `Bearer ${API_KEY}` },
            });
            const data = await response.json();
            if (!response.ok) {
              throw new Error(`HTTP ${response.status}: ${JSON.stringify(data)}`);
            }
            console.log(data);
            """,
        _ => throw new InvalidOperationException($"No JavaScript example is defined for {endpoint.Method} {endpoint.Path}.")
    };

    private static string GetPythonExample(Endpoint endpoint) => endpoint.Path switch
    {
        "/health" => """
            import json
            from urllib.request import urlopen

            BASE_URL = "http://PHONE_IP:8765"

            with urlopen(f"{BASE_URL}/health") as response:
                data = json.load(response)
                print(response.status, data)
            """,
        "/api/v1/messages" when endpoint.Method == "POST" => """
            import json
            from urllib.error import HTTPError
            from urllib.request import Request, urlopen

            BASE_URL = "http://PHONE_IP:8765"
            API_KEY = "YOUR_API_KEY"
            payload = {"to": "+919876543210", "message": "Hello"}
            request = Request(
                f"{BASE_URL}/api/v1/messages",
                data=json.dumps(payload).encode("utf-8"),
                headers={
                    "Authorization": f"Bearer {API_KEY}",
                    "Content-Type": "application/json",
                },
                method="POST",
            )
            try:
                with urlopen(request) as response:
                    print(response.status, json.load(response))
            except HTTPError as error:
                print(error.code, json.loads(error.read().decode("utf-8")))
            """,
        "/api/v1/messages/bulk" => """
            import json
            from urllib.error import HTTPError
            from urllib.request import Request, urlopen

            BASE_URL = "http://PHONE_IP:8765"
            API_KEY = "YOUR_API_KEY"
            payload = {
                "recipients": ["+919876543210", "+14155550123"],
                "message": "Hello",
            }
            request = Request(
                f"{BASE_URL}/api/v1/messages/bulk",
                data=json.dumps(payload).encode("utf-8"),
                headers={
                    "Authorization": f"Bearer {API_KEY}",
                    "Content-Type": "application/json",
                },
                method="POST",
            )
            try:
                with urlopen(request) as response:
                    print(response.status, json.load(response))
            except HTTPError as error:
                print(error.code, json.loads(error.read().decode("utf-8")))
            """,
        "/api/v1/messages" => """
            import json
            from urllib.parse import urlencode
            from urllib.request import Request, urlopen

            BASE_URL = "http://PHONE_IP:8765"
            API_KEY = "YOUR_API_KEY"
            query = urlencode({"status": "failed", "limit": 25, "offset": 0})
            request = Request(
                f"{BASE_URL}/api/v1/messages?{query}",
                headers={"Authorization": f"Bearer {API_KEY}"},
            )
            with urlopen(request) as response:
                print(response.status, json.load(response))
            """,
        "/api/v1/stats" => """
            import json
            from urllib.request import Request, urlopen

            BASE_URL = "http://PHONE_IP:8765"
            API_KEY = "YOUR_API_KEY"
            request = Request(
                f"{BASE_URL}/api/v1/stats",
                headers={"Authorization": f"Bearer {API_KEY}"},
            )
            with urlopen(request) as response:
                print(response.status, json.load(response))
            """,
        _ => throw new InvalidOperationException($"No Python example is defined for {endpoint.Method} {endpoint.Path}.")
    };

    private Border BuildCodeWindow(string title, string clipboardText, FormattedString formattedText)
    {
        var copy = new Button
        {
            StyleClass = ["Secondary"],
            Text = "Copy",
            FontSize = 11,
            HeightRequest = 34,
            Padding = new Thickness(12, 0)
        };
        copy.Clicked += async (_, _) =>
        {
            await Clipboard.Default.SetTextAsync(clipboardText);
            await PopupDialog.ShowAsync(this, $"{title} copied", "The full example is ready to paste.", PopupKind.Success);
        };

        var header = new Grid
        {
            ColumnDefinitions = [new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)],
            ColumnSpacing = 10,
            Padding = new Thickness(12, 8),
            BackgroundColor = Color.FromArgb("#131B24")
        };
        var lights = new HorizontalStackLayout { Spacing = 5, VerticalOptions = LayoutOptions.Center };
        lights.Add(BuildWindowLight("#FF5F57"));
        lights.Add(BuildWindowLight("#FEBC2E"));
        lights.Add(BuildWindowLight("#28C840"));
        header.Add(lights, 0, 0);
        header.Add(new Label
        {
            Text = title,
            FontSize = 11,
            TextColor = Color.FromArgb("#9AA7B4"),
            VerticalOptions = LayoutOptions.Center
        }, 1, 0);
        header.Add(copy, 2, 0);

        var code = new Label
        {
            FormattedText = formattedText,
            FontFamily = "monospace",
            FontSize = 12,
            LineBreakMode = LineBreakMode.WordWrap,
            HorizontalOptions = LayoutOptions.Fill
        };
        var contents = new VerticalStackLayout
        {
            Spacing = 0,
            Children =
            {
                header,
                new VerticalStackLayout
                {
                    Padding = new Thickness(12, 10),
                    Children = { code }
                }
            }
        };

        return new Border
        {
            Padding = 0,
            BackgroundColor = Color.FromArgb("#0B1117"),
            Stroke = Color.FromArgb("#263442"),
            StrokeThickness = 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 14 },
            Content = contents
        };
    }

    private static Border BuildWindowLight(string color) => new()
    {
        WidthRequest = 9,
        HeightRequest = 9,
        BackgroundColor = Color.FromArgb(color),
        StrokeThickness = 0,
        StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 5 }
    };

    private static string PrettyPrintJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        return JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions { WriteIndented = true });
    }

    private static FormattedString FormatPlain(string text)
    {
        var formatted = new FormattedString();
        formatted.Spans.Add(new Span { Text = text, TextColor = Color.FromArgb("#A5D6FF") });
        return formatted;
    }

    private static FormattedString FormatCurl(string command)
    {
        var formatted = new FormattedString();
        var tokens = Regex.Matches(command, @"\s+|""[^""]*""|'[^']*'|\S+");
        foreach (Match token in tokens)
        {
            var value = token.Value;
            var color = string.IsNullOrWhiteSpace(value)
                ? "#8B949E"
                : value == "curl"
                    ? "#7EE787"
                    : value.StartsWith("-", StringComparison.Ordinal)
                        ? "#79C0FF"
                        : value.Contains("BASE_URL", StringComparison.OrdinalIgnoreCase) || value.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                            ? "#A5D6FF"
                            : value.StartsWith('\'') || value.StartsWith('"')
                                ? "#D2A8FF"
                                : "#C9D1D9";
            formatted.Spans.Add(new Span { Text = value, TextColor = Color.FromArgb(color) });
        }

        return formatted;
    }

    private static FormattedString FormatSourceCode(string code)
    {
        var formatted = new FormattedString();
        var position = 0;
        foreach (Match token in SourceCodeTokenPattern.Matches(code))
        {
            if (token.Index > position)
            {
                formatted.Spans.Add(new Span
                {
                    Text = code[position..token.Index],
                    TextColor = Color.FromArgb("#C9D1D9")
                });
            }

            var color = token.Groups["string"].Success
                ? "#A5D6FF"
                : token.Groups["comment"].Success
                    ? "#8B949E"
                    : token.Groups["keyword"].Success
                        ? "#FF7B72"
                        : token.Groups["number"].Success
                            ? "#79C0FF"
                            : "#8B949E";
            formatted.Spans.Add(new Span { Text = token.Value, TextColor = Color.FromArgb(color) });
            position = token.Index + token.Length;
        }

        if (position < code.Length)
        {
            formatted.Spans.Add(new Span { Text = code[position..], TextColor = Color.FromArgb("#C9D1D9") });
        }

        return formatted;
    }

    private static FormattedString FormatJson(string json)
    {
        var formatted = new FormattedString();
        var position = 0;
        while (position < json.Length)
        {
            var current = json[position];
            if (current == '"')
            {
                var end = position + 1;
                while (end < json.Length)
                {
                    if (json[end] == '\\')
                    {
                        end += 2;
                    }
                    else if (json[end++] == '"')
                    {
                        break;
                    }
                }

                var next = end;
                while (next < json.Length && char.IsWhiteSpace(json[next]))
                {
                    next++;
                }

                var color = next < json.Length && json[next] == ':' ? "#D2A8FF" : "#A5D6FF";
                AddJsonSpan(formatted, json[position..Math.Min(end, json.Length)], color);
                position = Math.Min(end, json.Length);
            }
            else if (current is '{' or '}' or '[' or ']' or ',' or ':')
            {
                AddJsonSpan(formatted, current.ToString(), "#8B949E");
                position++;
            }
            else if (char.IsWhiteSpace(current))
            {
                var end = position + 1;
                while (end < json.Length && char.IsWhiteSpace(json[end]))
                {
                    end++;
                }

                AddJsonSpan(formatted, json[position..end], "#8B949E");
                position = end;
            }
            else
            {
                var end = position + 1;
                while (end < json.Length && !char.IsWhiteSpace(json[end]) && json[end] is not ('{' or '}' or '[' or ']' or ',' or ':' or '"'))
                {
                    end++;
                }

                AddJsonSpan(formatted, json[position..end], "#79C0FF");
                position = end;
            }
        }

        return formatted;
    }

    private static void AddJsonSpan(FormattedString formatted, string text, string color) =>
        formatted.Spans.Add(new Span { Text = text, TextColor = Color.FromArgb(color) });

    private async void OnOpenSpecClicked(object? sender, EventArgs e) =>
        await PopupDialog.OpenLinkAsync(this, "https://github.com/kjagdeesh/Sim-SMS-Gateway/blob/main/web/openapi.json");

    private async void OnBackClicked(object? sender, EventArgs e)
    {
        try
        {
            await Shell.Current.GoToAsync("..");
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            await PopupDialog.ShowAsync(this, "Couldn't go back", "Please try again.", PopupKind.Error);
        }
    }
}
