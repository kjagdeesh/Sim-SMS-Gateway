using Android;
using Microsoft.Maui.ApplicationModel;

namespace SimSmsGateway;

public sealed class SmsPermission : Permissions.BasePlatformPermission
{
    public override (string androidPermission, bool isRuntime)[] RequiredPermissions =>
    [
        (Manifest.Permission.SendSms, true)
    ];
}
