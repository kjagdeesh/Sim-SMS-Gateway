using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using SimSmsGateway.Core;

namespace SimSmsGateway;

public partial class MainPage : ContentPage
{
    private const int ServerPort = 8765;
    private readonly string _apiKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        .TrimEnd('=')
        .Replace('+', '-')
        .Replace('/', '_');
    private SmsApiServer? _server;

    public MainPage()
    {
        InitializeComponent();
        ApiKeyEntry.Text = _apiKey;
    }

    private async void OnStartClicked(object? sender, EventArgs e)
    {
        ErrorLabel.IsVisible = false;
        StartButton.IsEnabled = false;

        try
        {
            var permission = await Permissions.RequestAsync<SmsPermission>();
            if (permission != PermissionStatus.Granted)
            {
                SetError("SMS permission is required before the gateway can start.");
                return;
            }

            _server = new SmsApiServer(new AndroidSmsSender(), _apiKey);
            await _server.StartAsync(IPAddress.Any, ServerPort);
            AddressLabel.Text = string.Join(
                Environment.NewLine,
                GetServerAddresses(_server.Port));
            StatusLabel.Text = "Running. Keep this app open while using the gateway.";
            StatusPill.BackgroundColor = Color.FromArgb("#E4F3EF");
            StatusPillLabel.Text = "ONLINE";
            StatusPillLabel.TextColor = Color.FromArgb("#147D72");
            StopButton.IsEnabled = true;
        }
        catch (SocketException exception)
        {
            if (_server is not null)
            {
                await _server.DisposeAsync();
                _server = null;
            }

            SetError($"Could not start the local server: {exception.Message}");
        }
        finally
        {
            StartButton.IsEnabled = _server is null;
        }
    }

    private async void OnStopClicked(object? sender, EventArgs e)
    {
        ErrorLabel.IsVisible = false;
        if (_server is not null)
        {
            await _server.DisposeAsync();
            _server = null;
        }

        AddressLabel.Text = "Start the server to see its address";
        StatusLabel.Text = "Stopped";
        StatusPill.BackgroundColor = Color.FromArgb("#F0F3F4");
        StatusPillLabel.Text = "OFFLINE";
        StatusPillLabel.TextColor = Color.FromArgb("#63777D");
        StartButton.IsEnabled = true;
        StopButton.IsEnabled = false;
    }

    private async void OnCopyKeyClicked(object? sender, EventArgs e)
    {
        await Clipboard.Default.SetTextAsync(_apiKey);
        await DisplayAlertAsync("Copied", "The API key is on your clipboard.", "OK");
    }

    private void SetError(string message)
    {
        ErrorLabel.Text = message;
        ErrorLabel.IsVisible = true;
    }

    private static IReadOnlyList<string> GetServerAddresses(int port)
    {
        var addressesToShow = new List<string> { $"This phone: http://localhost:{port}" };

        IPAddress[] addresses;
        try
        {
            addresses = NetworkInterface.GetAllNetworkInterfaces()
                .Where(network => network.OperationalStatus == OperationalStatus.Up)
                .SelectMany(network => network.GetIPProperties().UnicastAddresses)
                .Select(unicast => unicast.Address)
                .Where(address => address.AddressFamily == AddressFamily.InterNetwork &&
                    !IPAddress.IsLoopback(address))
                .Distinct()
                .ToArray();
        }
        catch (NetworkInformationException exception)
        {
            addressesToShow.Add($"Could not list Wi-Fi addresses: {exception.Message}");
            return addressesToShow;
        }

        addressesToShow.AddRange(addresses.Select(address => $"Same Wi-Fi: http://{address}:{port}"));
        return addressesToShow;
    }
}
