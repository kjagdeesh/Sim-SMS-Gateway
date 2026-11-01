using System.Text.RegularExpressions;

namespace SimSmsGateway.Core;

public static partial class SmsRequestValidator
{
    public const int MaximumMessageLength = 1600;

    public static string? Validate(SmsMessageRequest? request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.To))
        {
            return "A destination number is required.";
        }

        if (!E164Pattern().IsMatch(request.To))
        {
            return "Destination must be an E.164 number, such as +14155550123.";
        }

        if (string.IsNullOrWhiteSpace(request.Message))
        {
            return "A message is required.";
        }

        if (request.Message.Length > MaximumMessageLength)
        {
            return $"Message must be {MaximumMessageLength} characters or fewer.";
        }

        return null;
    }

    [GeneratedRegex(@"^\+[1-9]\d{7,14}$", RegexOptions.CultureInvariant)]
    private static partial Regex E164Pattern();
}
