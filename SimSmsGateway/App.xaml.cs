using Microsoft.Extensions.DependencyInjection;
using SimSmsGateway.Core;

namespace SimSmsGateway;

public partial class App : Application
{
	public const string ThemePreferenceKey = "theme";

	public GatewayHost Gateway { get; }

	public SmsMessageStore MessageStore => Gateway.Store;

	public App()
	{
		InitializeComponent();
		Gateway = new GatewayHost(new SmsMessageStore(Path.Combine(FileSystem.AppDataDirectory, "messages.db")));
		ApplyTheme(Preferences.Default.Get(ThemePreferenceKey, "system"));
	}

	public static void ApplyTheme(string theme)
	{
		if (Current is null)
		{
			return;
		}

		Current.UserAppTheme = theme switch
		{
			"light" => AppTheme.Light,
			"dark" => AppTheme.Dark,
			_ => AppTheme.Unspecified
		};
		Preferences.Default.Set(ThemePreferenceKey, theme);
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		return new Window(new AppShell());
	}
}
