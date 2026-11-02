using System.Net.Sockets;

namespace SimSmsGateway;

public partial class ServerPage : ContentPage
{
    private bool _keyVisible;
    private string _sampleCommand = string.Empty;

    public ServerPage()
    {
        InitializeComponent();
        PopupDialog.Attach(this);
    }

    private GatewayHost Gateway => ((App)Application.Current!).Gateway;

    protected override void OnAppearing()
    {
        base.OnAppearing();
        Gateway.StateChanged += OnGatewayStateChanged;
        Render();
    }

    protected override void OnDisappearing()
    {
        Gateway.StateChanged -= OnGatewayStateChanged;
        base.OnDisappearing();
    }

    private void OnGatewayStateChanged(object? sender, EventArgs e) => MainThread.BeginInvokeOnMainThread(Render);

    private async void OnOpenWebsiteClicked(object? sender, EventArgs e)
    {
        if (sender is Button { CommandParameter: string url })
        {
            await PopupDialog.OpenLinkAsync(this, url);
        }
    }

    private async void OnWebsiteUrlTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is string url)
        {
            await PopupDialog.OpenLinkAsync(this, url);
        }
    }

    private async void OnCopyWebsiteUrlClicked(object? sender, EventArgs e)
    {
        if (sender is Button { CommandParameter: string url })
        {
            await Clipboard.Default.SetTextAsync(url);
            await PopupDialog.ShowAsync(this, "URL copied", "The website link is ready to paste.", PopupKind.Success);
        }
    }

    private void Render()
    {
        var running = Gateway.IsRunning;
        StatusPillStyle.Apply(StatusPill, StatusPillLabel, running);
        StatusTitle.Text = running ? "Running" : "Stopped";
        StatusLabel.Text = running
            ? "Your gateway is live. Keep this app open while using the API."
            : "Start the server to let your apps send SMS through this phone's SIM.";
        ToggleButton.Text = running ? "Stop server" : "Start server";
        ToggleButton.BackgroundColor = Color.FromArgb(running ? "#33FFFFFF" : "#2DD4BF");
        ToggleButton.TextColor = running ? Colors.White : Color.FromArgb("#06201D");

        var addresses = Gateway.GetAddresses();
        RenderAddresses(running ? addresses : []);
        ApiKeyLabel.Text = _keyVisible ? Gateway.ApiKey : new string('•', 24);
        RevealButton.Text = _keyVisible ? "Hide key" : "Show key";
        _sampleCommand = BuildSampleCommand(addresses);
        SampleLabel.FormattedText = FormatSampleCommand(_sampleCommand);
    }

    private void RenderAddresses(IReadOnlyList<string> addresses)
    {
        AddressListLayout.Clear();
        if (addresses.Count == 0)
        {
            AddressListLayout.Add(new Label
            {
                StyleClass = ["Muted"],
                Text = "Start the server to see and open its addresses."
            });
            return;
        }

        foreach (var address in addresses)
        {
            var separator = address.IndexOf(": ", StringComparison.Ordinal);
            if (separator <= 0 || separator + 2 >= address.Length)
            {
                AddressListLayout.Add(new Label { StyleClass = ["Mono"], Text = address });
                continue;
            }

            var label = address[..separator];
            var url = address[(separator + 2)..];
            var link = new Label
            {
                Text = url,
                FontFamily = "OpenSansSemibold",
                FontSize = 12,
                TextColor = Color.FromArgb(App.IsDark ? "#5EEAD4" : "#0F766E"),
                TextDecorations = TextDecorations.Underline
            };
            var tap = new TapGestureRecognizer();
            tap.Tapped += async (_, _) => await PopupDialog.OpenLinkAsync(this, url);
            link.GestureRecognizers.Add(tap);

            var copy = new Button
            {
                StyleClass = ["Secondary"],
                Text = "\uf0c5",
                FontFamily = "FASolid",
                FontSize = 14,
                WidthRequest = 42,
                HeightRequest = 42,
                Padding = 0
            };
            SemanticProperties.SetDescription(copy, $"Copy {label} address");
            copy.Clicked += async (_, _) =>
            {
                await Clipboard.Default.SetTextAsync(url);
                await PopupDialog.ShowAsync(this, "Address copied", $"{label} address is ready to paste.", PopupKind.Success);
            };

            var details = new VerticalStackLayout { Spacing = 3, VerticalOptions = LayoutOptions.Center };
            details.Add(new Label { StyleClass = ["Muted"], Text = label });
            details.Add(link);

            var row = new Grid
            {
                ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)],
                ColumnSpacing = 10
            };
            row.Add(details, 0, 0);
            row.Add(copy, 1, 0);
            AddressListLayout.Add(row);
        }
    }

    private string BuildSampleCommand(IReadOnlyList<string> addresses)
    {
        var wifi = addresses.FirstOrDefault(a => a.StartsWith("Same Wi-Fi: ", StringComparison.Ordinal));
        var baseUrl = wifi is null ? "http://PHONE_IP:8765" : wifi["Same Wi-Fi: ".Length..];
        return $"curl -X POST {baseUrl}/api/v1/messages \\{Environment.NewLine}" +
            $"  -H \"Authorization: Bearer YOUR_API_KEY\" \\{Environment.NewLine}" +
            $"  -H \"Content-Type: application/json\" \\{Environment.NewLine}" +
            "  -d '{\"to\":\"+91XXXXXXXXXX\",\"message\":\"Hello\"}'";
    }

    private static FormattedString FormatSampleCommand(string command)
    {
        var green = Color.FromArgb("#7EE787");
        var blue = Color.FromArgb("#79C0FF");
        var cyan = Color.FromArgb("#A5D6FF");
        var purple = Color.FromArgb("#D2A8FF");
        var muted = Color.FromArgb("#8B949E");

        var formatted = new FormattedString();
        var lines = command.Split(Environment.NewLine);
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            if (index == 0 && line.StartsWith("curl -X POST ", StringComparison.Ordinal))
            {
                var prefix = "curl -X POST ";
                var urlEnd = line.EndsWith(" \\", StringComparison.Ordinal) ? line.Length - 2 : line.Length;
                formatted.Spans.Add(new Span { Text = "$ ", TextColor = green });
                formatted.Spans.Add(new Span { Text = "curl", TextColor = green });
                formatted.Spans.Add(new Span { Text = " -X POST ", TextColor = blue });
                formatted.Spans.Add(new Span { Text = line[prefix.Length..urlEnd], TextColor = cyan });
                formatted.Spans.Add(new Span { Text = line[urlEnd..], TextColor = muted });
            }
            else if (line.Contains("-H ", StringComparison.Ordinal))
            {
                var flag = line.IndexOf("-H ", StringComparison.Ordinal);
                formatted.Spans.Add(new Span { Text = line[..flag], TextColor = muted });
                formatted.Spans.Add(new Span { Text = "-H", TextColor = blue });
                formatted.Spans.Add(new Span { Text = line[(flag + 2)..], TextColor = purple });
            }
            else if (line.Contains("-d ", StringComparison.Ordinal))
            {
                var flag = line.IndexOf("-d ", StringComparison.Ordinal);
                formatted.Spans.Add(new Span { Text = line[..flag], TextColor = muted });
                formatted.Spans.Add(new Span { Text = "-d", TextColor = blue });
                formatted.Spans.Add(new Span { Text = line[(flag + 2)..], TextColor = green });
            }
            else
            {
                formatted.Spans.Add(new Span { Text = line, TextColor = muted });
            }

            if (index < lines.Length - 1)
            {
                formatted.Spans.Add(new Span { Text = Environment.NewLine, TextColor = muted });
            }
        }

        return formatted;
    }

    private async void OnToggleClicked(object? sender, EventArgs e)
    {
        ToggleButton.IsEnabled = false;
        try
        {
            if (Gateway.IsRunning)
            {
                await Gateway.StopAsync();
                await PopupDialog.ShowAsync(this, "Server stopped", "Apps can no longer send SMS through this phone.", PopupKind.Info);
                return;
            }

            var permission = await Permissions.RequestAsync<SmsPermission>();
            if (permission != PermissionStatus.Granted)
            {
                await PopupDialog.ShowAsync(
                    this,
                    "SMS permission needed",
                    "Sim SMS Gateway can't send messages without the SMS permission. Allow it in the app's settings, then try again.",
                    PopupKind.Error,
                    "Open app settings",
                    () =>
                    {
                        AppInfo.Current.ShowSettingsUI();
                        return Task.CompletedTask;
                    });
                return;
            }

            await Gateway.StartAsync();
            await PopupDialog.ShowAsync(this, "Server is running", $"Listening on port {GatewayHost.ServerPort}. Keep this app open.", PopupKind.Success);
        }
        catch (SocketException exception)
        {
            await PopupDialog.ShowAsync(
                this,
                "Couldn't start the server",
                $"Port {GatewayHost.ServerPort} is unavailable ({exception.Message}). Close other apps using it and try again.",
                PopupKind.Error);
        }
        finally
        {
            ToggleButton.IsEnabled = true;
            Render();
        }
    }

    private void OnRevealClicked(object? sender, EventArgs e)
    {
        _keyVisible = !_keyVisible;
        Render();
    }

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

    private async void OnCopyKeyClicked(object? sender, EventArgs e)
    {
        await Clipboard.Default.SetTextAsync(Gateway.ApiKey);
        await PopupDialog.ShowAsync(this, "API key copied", "Treat it like a password and share it only with apps you trust.", PopupKind.Success);
    }

    private async void OnCopySampleClicked(object? sender, EventArgs e)
    {
        await Clipboard.Default.SetTextAsync(_sampleCommand);
        await PopupDialog.ShowAsync(this, "Command copied", "Replace YOUR_API_KEY and the phone number before running it.", PopupKind.Success);
    }
}
