namespace SimSmsGateway;

public partial class HomePage : ContentPage
{
    public HomePage()
    {
        InitializeComponent();
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
        if (sender is Button { CommandParameter: string route })
        {
            await Shell.Current.GoToAsync(route);
        }
    }
}
