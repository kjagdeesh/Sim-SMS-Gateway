using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using SimSmsGateway.Core;

namespace SimSmsGateway;

/// <summary>Owns the local SMS API server so every page shows the same live state.</summary>
public sealed class GatewayHost
{
    public const int ServerPort = 8765;
    private SmsApiServer? _server;

    public GatewayHost(SmsMessageStore store)
    {
        Store = store;
    }

    public SmsMessageStore Store { get; }

    public string ApiKey { get; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        .TrimEnd('=')
        .Replace('+', '-')
        .Replace('/', '_');

    public bool IsRunning => _server is not null;

    public event EventHandler? StateChanged;

    public async Task StartAsync()
    {
        if (_server is not null)
        {
            return;
        }

        var server = new SmsApiServer(new AndroidSmsSender(), ApiKey, Store);
        try
        {
            await server.StartAsync(IPAddress.Any, ServerPort);
        }
        catch
        {
            await server.DisposeAsync();
            throw;
        }

        _server = server;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task StopAsync()
    {
        var server = _server;
        _server = null;
        if (server is not null)
        {
            await server.DisposeAsync();
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public IReadOnlyList<string> GetAddresses()
    {
        var addresses = new List<string> { $"This phone: http://localhost:{ServerPort}" };
        try
        {
            addresses.AddRange(NetworkInterface.GetAllNetworkInterfaces()
                .Where(network => network.OperationalStatus == OperationalStatus.Up)
                .SelectMany(network => network.GetIPProperties().UnicastAddresses)
                .Select(unicast => unicast.Address)
                .Where(address => address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address))
                .Distinct()
                .Select(address => $"Same Wi-Fi: http://{address}:{ServerPort}"));
        }
        catch (NetworkInformationException exception)
        {
            addresses.Add($"Could not list Wi-Fi addresses: {exception.Message}");
        }

        return addresses;
    }
}
