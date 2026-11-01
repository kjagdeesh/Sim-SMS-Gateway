namespace SimSmsGateway.Core;

public sealed record SmsMessageRequest(string? To, string? Message);

public sealed record SmsBulkRequest(IReadOnlyList<string?>? Recipients, string? Message);
