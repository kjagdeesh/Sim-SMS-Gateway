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
            stack.Add(new Border { StyleClass = ["Inset"], Content = new Label { StyleClass = ["Mono"], FontSize = 11, Text = endpoint.Body } });
        }

        var copy = new Button { StyleClass = ["Secondary"], Text = "Copy cURL example", HeightRequest = 42 };
        copy.Clicked += async (_, _) =>
        {
            await Clipboard.Default.SetTextAsync(endpoint.Curl);
            _ = PopupDialog.ShowAsync(this, $"{endpoint.Title} example copied", "Replace BASE_URL and YOUR_API_KEY before you run it.", PopupKind.Success);
        };
        stack.Add(copy);

        return new Border { StyleClass = ["Card"], Content = stack };
    }

    private async void OnOpenSpecClicked(object? sender, EventArgs e) =>
        await PopupDialog.OpenLinkAsync(this, "https://github.com/kjagdeesh/Sim-SMS-Gateway/blob/main/web/openapi.json");
}
