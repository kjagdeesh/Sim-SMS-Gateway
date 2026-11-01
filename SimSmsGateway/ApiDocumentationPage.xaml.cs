using System.Text.Json;
using System.Text.RegularExpressions;

namespace SimSmsGateway;

public partial class ApiDocumentationPage : ContentPage
{
    private sealed record Endpoint(string Method, string Path, string Title, string Summary, string? Auth, string? Body, string Curl);

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

        if (endpoint.Body is not null)
        {
            if (endpoint.Body.StartsWith('{') || endpoint.Body.StartsWith('['))
            {
                var json = PrettyPrintJson(endpoint.Body);
                stack.Add(BuildCodeWindow("JSON example", json, FormatJson(json)));
            }
            else
            {
                stack.Add(BuildCodeWindow("Query parameters", endpoint.Body, FormatPlain(endpoint.Body)));
            }
        }

        stack.Add(BuildCodeWindow("cURL example", endpoint.Curl, FormatCurl(endpoint.Curl)));

        return new Border { StyleClass = ["Card"], Content = stack };
    }

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
