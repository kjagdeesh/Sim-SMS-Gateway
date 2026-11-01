namespace SimSmsGateway.Core;

public sealed record SmsMessageRequest(string? To, string? Message);
