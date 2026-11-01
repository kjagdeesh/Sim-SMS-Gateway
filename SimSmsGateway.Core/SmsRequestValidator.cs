using System.Text.RegularExpressions;

namespace SimSmsGateway.Core;

public static partial class SmsRequestValidator
{
    public const int MaximumMessageLength = 1600;

    public const int MaximumBulkRecipients = 100;

    public static string? Validate(SmsMessageRequest? request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.To))
        {
            return "A destination number is required.";
        }

        return ValidateNumber(request.To) ?? ValidateMessage(request.Message);
    }

    public static string? ValidateBulk(SmsBulkRequest? request)
    {
        if (request?.Recipients is null || request.Recipients.Count == 0)
        {
            return "At least one recipient is required.";
        }

        if (request.Recipients.Count > MaximumBulkRecipients)
        {
            return $"A bulk request can contain at most {MaximumBulkRecipients} recipients.";
        }

        for (var index = 0; index < request.Recipients.Count; index++)
        {
            var number = request.Recipients[index];
            if (string.IsNullOrWhiteSpace(number) || !E164Pattern().IsMatch(number))
            {
                return $"Recipient {index + 1} must be an E.164 number, such as +14155550123.";
            }
        }

        if (request.Recipients.Distinct().Count() != request.Recipients.Count)
        {
            return "Recipients must be unique.";
        }

        return ValidateMessage(request.Message);
    }

    private static string? ValidateNumber(string number) =>
        E164Pattern().IsMatch(number) ? null : "Destination must be an E.164 number, such as +14155550123.";

    private static string? ValidateMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return "A message is required.";
        }

        return message.Length > MaximumMessageLength
            ? $"Message must be {MaximumMessageLength} characters or fewer."
            : null;
    }

    [GeneratedRegex(@"^\+[1-9]\d{7,14}$", RegexOptions.CultureInvariant)]
    private static partial Regex E164Pattern();
}
