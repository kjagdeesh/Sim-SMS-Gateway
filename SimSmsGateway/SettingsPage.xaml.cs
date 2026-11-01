using System.Globalization;
using Microsoft.Data.Sqlite;

namespace SimSmsGateway;

public partial class SettingsPage : ContentPage
{
    public SettingsPage()
    {
        InitializeComponent();
        PopupDialog.Attach(this);
        VersionLabel.Text = AppInfo.Current.VersionString;
        PortLabel.Text = GatewayHost.ServerPort.ToString(CultureInfo.InvariantCulture);
        HighlightTheme(Preferences.Default.Get(App.ThemePreferenceKey, "system"));
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        var gateway = ((App)Application.Current!).Gateway;
        ServerLabel.Text = gateway.IsRunning ? "Running" : "Stopped";
        try
        {
            StoredLabel.Text = gateway.Store.GetStats().Total.ToString(CultureInfo.CurrentCulture);
        }
        catch (SqliteException)
        {
            StoredLabel.Text = "Unavailable";
        }
    }

    private void OnThemeClicked(object? sender, EventArgs e)
    {
        if (sender is Button { CommandParameter: string theme })
        {
            App.ApplyTheme(theme);
            HighlightTheme(theme);
            var name = theme switch { "light" => "Light theme on", "dark" => "Dark theme on", _ => "Following system theme" };
            _ = PopupDialog.ShowAsync(this, name, "Your choice is saved for next time.", PopupKind.Success);
        }
    }

    private void HighlightTheme(string theme)
    {
        foreach (var (button, key) in new[] { (SystemButton, "system"), (LightButton, "light"), (DarkButton, "dark") })
        {
            var selected = key == theme;
            button.BackgroundColor = selected ? Color.FromArgb("#0F766E") : Color.FromArgb("#1F8A9BA3");
            button.TextColor = selected ? Colors.White : Color.FromArgb("#8A9BA3");
            button.CornerRadius = 12;
            button.HeightRequest = 44;
            button.FontFamily = "OpenSansSemibold";
        }

        ThemeHint.Text = theme switch
        {
            "light" => "Light theme is always used.",
            "dark" => "Dark theme is always used.",
            _ => "Follows your phone's light or dark setting."
        };
    }
}
