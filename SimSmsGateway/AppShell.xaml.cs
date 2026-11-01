namespace SimSmsGateway;

public partial class AppShell : Shell
{
	public AppShell()
	{
		InitializeComponent();

		Routing.RegisterRoute("server", typeof(ServerPage));
		Routing.RegisterRoute("history", typeof(MessageHistoryPage));
		Routing.RegisterRoute("api", typeof(ApiDocumentationPage));
		Routing.RegisterRoute("settings", typeof(SettingsPage));
		Routing.RegisterRoute("about", typeof(AboutAppPage));

		CurrentItem = HomeTab;
	}
}
