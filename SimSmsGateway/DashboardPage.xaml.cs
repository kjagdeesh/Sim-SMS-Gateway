using System.Globalization;
using Microsoft.Data.Sqlite;

namespace SimSmsGateway;

public partial class DashboardPage : ContentPage
{
    public DashboardPage()
    {
        InitializeComponent();
        PopupDialog.Attach(this);
    }

    private GatewayHost Gateway => ((App)Application.Current!).Gateway;

    protected override void OnAppearing()
    {
        base.OnAppearing();
        Gateway.StateChanged += OnGatewayStateChanged;
        RefreshStats();
        RefreshStatus();
    }

    protected override void OnDisappearing()
    {
        Gateway.StateChanged -= OnGatewayStateChanged;
        base.OnDisappearing();
    }

    private void OnGatewayStateChanged(object? sender, EventArgs e) =>
        MainThread.BeginInvokeOnMainThread(RefreshStatus);

    private void RefreshStatus() => StatusPillStyle.Apply(StatusPill, StatusPillLabel, Gateway.IsRunning);

    private void RefreshStats()
    {
        try
        {
            var stats = Gateway.Store.GetStats();
            TotalLabel.Text = stats.Total.ToString(CultureInfo.CurrentCulture);
            SuccessLabel.Text = stats.Success.ToString(CultureInfo.CurrentCulture);
            FailedLabel.Text = stats.Failed.ToString(CultureInfo.CurrentCulture);
        }
        catch (SqliteException)
        {
            TotalLabel.Text = SuccessLabel.Text = FailedLabel.Text = "–";
            _ = PopupDialog.ShowAsync(this, "Couldn't load message totals", "The message database is unavailable. Restart the app and try again.", PopupKind.Error);
        }
    }

    private async void OnTileTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is not string route)
        {
            return;
        }

        try
        {
            await Shell.Current.GoToAsync(route);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            await PopupDialog.ShowAsync(this, "Couldn't open that page", "Please try again.", PopupKind.Error);
        }
    }
}

internal static class StatusPillStyle
{
    public static void Apply(Border pill, Label label, bool online)
    {
        label.Text = online ? "ONLINE" : "OFFLINE";
        pill.BackgroundColor = Color.FromArgb(online ? "#2A2DD4BF" : "#2A8A9BA3");
        label.TextColor = Color.FromArgb(online ? "#14B8A6" : "#8A9BA3");
    }
}
