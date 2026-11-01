namespace SimSmsGateway;

public partial class DeveloperProjectPage : ContentPage
{
    private bool _loaded;

    public DeveloperProjectPage()
    {
        InitializeComponent();
        PopupDialog.Attach(this);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_loaded)
        {
            return;
        }

        var user = await GitHubApi.GetUserAsync();
        if (user is null)
        {
            _ = PopupDialog.ShowAsync(this, "Couldn't load your GitHub profile", "You're offline or GitHub is busy. Showing saved details; links still work.", PopupKind.Warning);
            return;
        }

        _loaded = true;
        if (!string.IsNullOrWhiteSpace(user.Name))
        {
            NameLabel.Text = user.Name;
        }

        MetaLabel.Text = string.Join(" · ", new[] { "Developer", user.Location, $"@{GitHubApi.Owner}" }.Where(part => !string.IsNullOrWhiteSpace(part)));
        if (!string.IsNullOrWhiteSpace(user.Bio))
        {
            BioLabel.Text = user.Bio;
            BioLabel.IsVisible = true;
        }

        if (!string.IsNullOrWhiteSpace(user.AvatarUrl))
        {
            AvatarImage.Source = ImageSource.FromUri(new Uri(user.AvatarUrl));
        }
    }

    private async void OnOpenLinkClicked(object? sender, EventArgs e)
    {
        if (sender is Button { CommandParameter: string url })
        {
            await PopupDialog.OpenLinkAsync(this, url);
        }
    }

    private async void OnProjectTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is string url)
        {
            await PopupDialog.OpenLinkAsync(this, url);
        }
    }
}
