using Android.Telephony;
using SimSmsGateway.Core;

namespace SimSmsGateway;

public sealed class AndroidSmsSender : ISmsSender
{
    public Task SendAsync(string destination, string message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var smsManager = GetSmsManager()
                ?? throw new SmsSendException("Android could not access the default SIM.");
            var parts = smsManager.DivideMessage(message);

            if (parts is null || parts.Count == 0)
            {
                throw new SmsSendException("Android could not split the message for SMS submission.");
            }

            if (parts.Count == 1)
            {
                smsManager.SendTextMessage(destination, null, message, null, null);
            }
            else
            {
                smsManager.SendMultipartTextMessage(destination, null, parts, null, null);
            }
        }
        catch (SmsSendException)
        {
            throw;
        }
        catch (Java.Lang.Exception exception)
        {
            throw new SmsSendException($"Android could not submit the SMS: {exception.Message}");
        }

        return Task.CompletedTask;
    }

    private static SmsManager? GetSmsManager()
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(31))
        {
            return Android.App.Application.Context.GetSystemService("sms") as SmsManager;
        }

        if (OperatingSystem.IsAndroidVersionAtLeast(24))
        {
            return SmsManager.GetSmsManagerForSubscriptionId(SubscriptionManager.DefaultSubscriptionId);
        }

        return SmsManager.Default;
    }
}
