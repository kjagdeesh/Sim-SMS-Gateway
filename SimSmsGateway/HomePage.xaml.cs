namespace SimSmsGateway;

public partial class HomePage : ContentPage
{
    public HomePage()
    {
        InitializeComponent();
        PopupDialog.Attach(this);
    }

    private GatewayHost Gateway => ((App)Application.Current!).Gateway;

    protected override void OnAppearing()
    {
        base.OnAppearing();
        Gateway.StateChanged += OnGatewayStateChanged;
        StatusPillStyle.Apply(StatusPill, StatusPillLabel, Gateway.IsRunning);
    }

    protected override void OnDisappearing()
    {
        Gateway.StateChanged -= OnGatewayStateChanged;
        base.OnDisappearing();
    }

    private void OnGatewayStateChanged(object? sender, EventArgs e) =>
        MainThread.BeginInvokeOnMainThread(() => StatusPillStyle.Apply(StatusPill, StatusPillLabel, Gateway.IsRunning));

    private async void OnNavigateClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { CommandParameter: string route })
        {
            return;
        }

        if (route == "server" && !Gateway.IsRunning)
        {
            await PopupDialog.ShowAsync(this, "Server is offline", "Tap Start server on the next page to begin accepting messages.", PopupKind.Info);
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
