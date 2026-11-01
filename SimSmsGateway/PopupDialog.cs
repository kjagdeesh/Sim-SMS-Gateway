using System.Runtime.CompilerServices;
using Microsoft.Maui.Controls.Shapes;

namespace SimSmsGateway;

public enum PopupKind
{
    Success,
    Info,
    Warning,
    Error
}

public static class PopupDialog
{
    private static readonly ConditionalWeakTable<Page, PopupView> Views = new();

    public static void Attach(ContentPage page)
    {
        if (Views.TryGetValue(page, out _) || page.Content is null)
        {
            return;
        }

        var original = page.Content;
        page.Content = null;
        var view = new PopupView();
        page.Content = new Grid { Children = { original, view } };
        Views.Add(page, view);
    }

    public static Task ShowAsync(
        Page page,
        string title,
        string? message = null,
        PopupKind kind = PopupKind.Info,
        string? actionText = null,
        Func<Task>? action = null) =>
        Views.TryGetValue(page, out var view)
            ? view.ShowAsync(title, message, kind, actionText, action)
            : Task.CompletedTask;

    public static async Task OpenLinkAsync(Page page, string url)
    {
        try
        {
            if (!await Launcher.Default.OpenAsync(url))
            {
                await ShowAsync(page, "Couldn't open the link", "No app on this phone can open it. Copy it from the project page instead.", PopupKind.Error);
            }
        }
        catch (Exception exception) when (exception is FeatureNotSupportedException or InvalidOperationException or UriFormatException)
        {
            await ShowAsync(page, "Couldn't open the link", "Check that a web browser is installed and try again.", PopupKind.Error);
        }
    }

    private sealed class PopupView : Grid
    {
        private readonly Border _card;
        private readonly Border _iconBox;
        private readonly Label _icon;
        private readonly Label _title;
        private readonly Label _message;
        private readonly Button _cancel;
        private readonly Button _primary;
        private readonly SemaphoreSlim _showLock = new(1, 1);
        private TaskCompletionSource<bool>? _completion;
        private Func<Task>? _action;

        public PopupView()
        {
            IsVisible = false;
            Opacity = 0;
            ZIndex = 100;
            Padding = new Thickness(24, 28);
            BackgroundColor = Color.FromArgb("#B30A2430");

            var closeButton = new Button
            {
                Text = "×",
                FontFamily = "OpenSansSemibold",
                FontSize = 22,
                TextColor = Colors.White,
                WidthRequest = 36,
                HeightRequest = 36,
                CornerRadius = 18,
                Padding = 0,
                BackgroundColor = Color.FromArgb("#33FFFFFF"),
                HorizontalOptions = LayoutOptions.End,
                VerticalOptions = LayoutOptions.Start,
                Margin = new Thickness(0, 12, 12, 0)
            };
            closeButton.Clicked += (_, _) => _completion?.TrySetResult(false);

            _icon = new Label
            {
                FontFamily = "FASolid",
                FontSize = 36,
                TextColor = Colors.White,
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center
            };
            _iconBox = new Border
            {
                WidthRequest = 76,
                HeightRequest = 76,
                Padding = 0,
                BackgroundColor = Color.FromArgb("#2EFFFFFF"),
                Stroke = Color.FromArgb("#55FFFFFF"),
                StrokeThickness = 1,
                StrokeShape = new RoundRectangle { CornerRadius = 24 },
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
                Content = _icon
            };

            var heroContent = new Grid
            {
                HeightRequest = 142,
                RowDefinitions = [new RowDefinition(GridLength.Star)],
                ColumnDefinitions = [new ColumnDefinition(GridLength.Star)]
            };
            var heroBackground = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new(Color.FromArgb("#137E75"), 0),
                    new(Color.FromArgb("#0C5B59"), 1)
                },
                new Point(0, 0),
                new Point(1, 1));
            var lowerDecoration = new Border
            {
                WidthRequest = 96,
                HeightRequest = 96,
                BackgroundColor = Color.FromArgb("#18FFFFFF"),
                StrokeThickness = 0,
                StrokeShape = new RoundRectangle { CornerRadius = 48 },
                HorizontalOptions = LayoutOptions.Start,
                VerticalOptions = LayoutOptions.End,
                TranslationX = -28,
                TranslationY = 36
            };
            heroContent.Add(lowerDecoration, 0, 0);

            var upperDecoration = new Border
            {
                WidthRequest = 70,
                HeightRequest = 70,
                BackgroundColor = Color.FromArgb("#16FFFFFF"),
                StrokeThickness = 0,
                StrokeShape = new RoundRectangle { CornerRadius = 35 },
                HorizontalOptions = LayoutOptions.End,
                VerticalOptions = LayoutOptions.Start,
                TranslationX = 14,
                TranslationY = -18
            };
            heroContent.Add(upperDecoration, 0, 0);
            heroContent.Add(_iconBox, 0, 0);
            heroContent.Add(closeButton, 0, 0);

            var hero = new Border
            {
                HeightRequest = 142,
                Padding = 0,
                StrokeThickness = 0,
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(26, 26, 0, 0) },
                Background = heroBackground,
                Content = heroContent
            };

            _title = new Label { FontFamily = "OpenSansSemibold", FontSize = 21, HorizontalTextAlignment = TextAlignment.Center };
            _message = new Label { FontSize = 14, HorizontalTextAlignment = TextAlignment.Center, LineBreakMode = LineBreakMode.WordWrap };

            _cancel = new Button
            {
                Text = "Not now",
                FontFamily = "OpenSansSemibold",
                FontSize = 14,
                HeightRequest = 48,
                CornerRadius = 14,
                BackgroundColor = Color.FromArgb("#EDF2F5")
            };
            _cancel.Clicked += (_, _) => _completion?.TrySetResult(false);

            _primary = new Button
            {
                Text = "Got it",
                FontFamily = "OpenSansSemibold",
                FontSize = 14,
                HeightRequest = 48,
                CornerRadius = 14,
                BackgroundColor = Color.FromArgb("#16A995"),
                TextColor = Colors.White
            };
            _primary.Clicked += (_, _) => _completion?.TrySetResult(true);

            var buttons = new Grid
            {
                ColumnDefinitions = [new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star)],
                ColumnSpacing = 10
            };
            buttons.Add(_cancel, 0, 0);
            buttons.Add(_primary, 1, 0);

            var content = new VerticalStackLayout
            {
                Padding = new Thickness(22, 23, 22, 22),
                Spacing = 12,
                Children = { _title, _message, buttons }
            };

            _card = new Border
            {
                MaximumWidthRequest = 420,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
                Padding = 0,
                BackgroundColor = Colors.White,
                StrokeThickness = 0,
                StrokeShape = new RoundRectangle { CornerRadius = 26 },
                Shadow = new Shadow { Brush = Brush.Black, Opacity = 0.22f, Radius = 26, Offset = new Point(0, 10) },
                Content = new VerticalStackLayout { Spacing = 0, Children = { hero, content } }
            };

            Children.Add(_card);
            ApplyTheme();
        }

        public async Task ShowAsync(string title, string? message, PopupKind kind, string? actionText, Func<Task>? action)
        {
            await _showLock.WaitAsync();
            try
            {
                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    ApplyTheme();
                    _completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    _action = action;
                    _title.Text = title;
                    _message.Text = message;
                    _message.IsVisible = !string.IsNullOrWhiteSpace(message);
                    _cancel.IsVisible = actionText is not null;
                    _primary.Text = actionText ?? "Got it";
                    _icon.Text = kind switch
                    {
                        PopupKind.Success => "\uf058",
                        PopupKind.Warning => "\uf071",
                        PopupKind.Error => "\uf057",
                        _ => "\uf05a"
                    };
                    _iconBox.BackgroundColor = Color.FromArgb(kind switch
                    {
                        PopupKind.Success => "#284ADE80",
                        PopupKind.Warning => "#28FBBF24",
                        PopupKind.Error => "#28F87171",
                        _ => "#285EEAD4"
                    });
                    IsVisible = true;
                    Opacity = 0;
                    _card.Scale = 0.94;
                    await Task.WhenAll(this.FadeToAsync(1, 160), _card.ScaleToAsync(1, 180, Easing.CubicOut));
                });

                var accepted = await _completion!.Task;
                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    await Task.WhenAll(this.FadeToAsync(0, 120), _card.ScaleToAsync(0.97, 120));
                    IsVisible = false;
                });

                if (accepted && _action is not null)
                {
                    await _action();
                }
            }
            finally
            {
                _completion = null;
                _action = null;
                _showLock.Release();
            }
        }

        private void ApplyTheme()
        {
            var dark = App.IsDark;
            _card.BackgroundColor = Color.FromArgb(dark ? "#111B21" : "#FFFFFF");
            _title.TextColor = Color.FromArgb(dark ? "#E8F0F3" : "#0F1D26");
            _message.TextColor = Color.FromArgb(dark ? "#B7C6CD" : "#66798A");
            _cancel.BackgroundColor = Color.FromArgb(dark ? "#1E2E36" : "#EDF2F5");
            _cancel.TextColor = Color.FromArgb(dark ? "#B7C6CD" : "#536773");
            _primary.BackgroundColor = Color.FromArgb(dark ? "#2DD4BF" : "#16A995");
            _primary.TextColor = Color.FromArgb(dark ? "#06201D" : "#FFFFFF");
        }
    }
}
