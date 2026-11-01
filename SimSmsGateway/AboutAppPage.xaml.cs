using System.Globalization;

namespace SimSmsGateway;

public partial class AboutAppPage : ContentPage
{
    private const string FallbackDescription =
        "Sim SMS Gateway turns an Android phone with an active SIM into a self-hosted SMS sending platform. Apps and services can send single or bulk messages through a REST API for automation, notifications, OTP delivery and system alerts.";
    private const string RepoUrl = "https://github.com/kjagdeesh/Sim-SMS-Gateway";

    private bool _loaded;

    public AboutAppPage()
    {
        InitializeComponent();
        PopupDialog.Attach(this);
        VersionLabel.Text = $"Version {AppInfo.Current.VersionString} (build {AppInfo.Current.BuildString})";
        TaglineLabel.Text = "Your phone. Your SIM. Your SMS API.";
        DescriptionLabel.Text = FallbackDescription;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (!_loaded)
        {
            await LoadAsync();
        }
    }

    private async Task LoadAsync()
    {
        Spinner.IsRunning = Spinner.IsVisible = true;
        RetryButton.IsVisible = false;
        SourceLabel.Text = "Loading from GitHub…";

        var repoTask = GitHubApi.GetRepoAsync();
        var readmeTask = GitHubApi.GetReadmeSummaryAsync();
        var repo = await repoTask;
        var readme = await readmeTask;

        Spinner.IsRunning = Spinner.IsVisible = false;
        if (repo is null && readme is null)
        {
            SourceLabel.Text = "Offline — showing the built-in description";
            RetryButton.IsVisible = true;
            _ = PopupDialog.ShowAsync(this, "Couldn't reach GitHub", "Check your internet connection. Showing the built-in description instead.", PopupKind.Warning);
            return;
        }

        _loaded = true;
        SourceLabel.Text = "Fetched from github.com/kjagdeesh/Sim-SMS-Gateway";
        if (repo is not null)
        {
            DescriptionLabel.Text = string.IsNullOrWhiteSpace(repo.Description) ? FallbackDescription : repo.Description;
            StarsLabel.Text = repo.Stars.ToString(CultureInfo.CurrentCulture);
            ForksLabel.Text = repo.Forks.ToString(CultureInfo.CurrentCulture);
            LanguageLabel.Text = repo.Language ?? "C#";
            if (repo.PushedAt is { } pushed)
            {
                UpdatedLabel.Text = $"Last updated {pushed.ToLocalTime().ToString("d MMM yyyy", CultureInfo.CurrentCulture)}";
                UpdatedLabel.IsVisible = true;
            }

            TopicsLayout.Clear();
            foreach (var topic in repo.Topics.Take(8))
            {
                TopicsLayout.Add(new Border
                {
                    Margin = new Thickness(0, 0, 6, 6),
                    Padding = new Thickness(10, 4),
                    StrokeThickness = 0,
                    StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 },
                    BackgroundColor = Color.FromArgb("#2A14B8A6"),
                    Content = new Label { Text = topic, FontSize = 11, FontFamily = "OpenSansSemibold", TextColor = Color.FromArgb("#14B8A6") }
                });
            }
        }

        if (!string.IsNullOrWhiteSpace(readme))
        {
            ReadmeLabel.Text = readme;
            ReadmeLabel.IsVisible = true;
        }
    }

    private async void OnRetryClicked(object? sender, EventArgs e) => await LoadAsync();

    private async void OnOpenRepoClicked(object? sender, EventArgs e) => await PopupDialog.OpenLinkAsync(this, RepoUrl);

    private async void OnOpenIssuesClicked(object? sender, EventArgs e) => await PopupDialog.OpenLinkAsync(this, $"{RepoUrl}/issues");
}
