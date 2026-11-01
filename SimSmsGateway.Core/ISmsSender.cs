namespace SimSmsGateway.Core;

public interface ISmsSender
{
    Task SendAsync(string destination, string message, CancellationToken cancellationToken);
}

public sealed class SmsSendException(string message) : Exception(message);
