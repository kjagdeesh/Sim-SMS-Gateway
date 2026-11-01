using System.Net.Sockets;

namespace SimSmsGateway;

public partial class ServerPage : ContentPage
{
    private bool _keyVisible;

    public ServerPage()
    {
        InitializeComponent();
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
        AddressLabel.Text = running ? string.Join(Environment.NewLine, addresses) : "Start the server to see its address";
        ApiKeyLabel.Text = _keyVisible ? Gateway.ApiKey : new string('•', 24);
        RevealButton.Text = _keyVisible ? "Hide key" : "Show key";
        SampleLabel.Text = BuildSampleCommand(addresses);
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

    private async void OnToggleClicked(object? sender, EventArgs e)
    {
        ErrorCard.IsVisible = false;
        ToggleButton.IsEnabled = false;
        try
        {
            if (Gateway.IsRunning)
            {
                await Gateway.StopAsync();
                return;
            }

            var permission = await Permissions.RequestAsync<SmsPermission>();
            if (permission != PermissionStatus.Granted)
            {
                ShowError("SMS permission is required before the gateway can start.");
                return;
            }

            await Gateway.StartAsync();
        }
        catch (SocketException exception)
        {
            ShowError($"Could not start the local server: {exception.Message}");
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

    private async void OnCopyKeyClicked(object? sender, EventArgs e)
    {
        await Clipboard.Default.SetTextAsync(Gateway.ApiKey);
        await DisplayAlertAsync("Copied", "The API key is on your clipboard.", "OK");
    }

    private async void OnCopySampleClicked(object? sender, EventArgs e)
    {
        await Clipboard.Default.SetTextAsync(SampleLabel.Text);
        await DisplayAlertAsync("Copied", "Replace YOUR_API_KEY and the phone number before running it.", "OK");
    }

    private void ShowError(string message)
    {
        ErrorLabel.Text = message;
        ErrorCard.IsVisible = true;
    }
}
