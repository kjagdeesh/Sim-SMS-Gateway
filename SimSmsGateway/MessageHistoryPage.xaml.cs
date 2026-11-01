using System.Collections.ObjectModel;
using System.Globalization;
using Microsoft.Data.Sqlite;
using SimSmsGateway.Core;

namespace SimSmsGateway;

public partial class MessageHistoryPage : ContentPage
{
    private const int PageSize = 50;
    private string _filter = "all";
    private int _offset;

    public ObservableCollection<MessageHistoryItem> Messages { get; } = [];

    public MessageHistoryPage()
    {
        InitializeComponent();
        PopupDialog.Attach(this);
        BindingContext = this;
        UpdateFilterButtons();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        Reload();
    }

    private SmsMessageStore Store => ((App)Application.Current!).MessageStore;

    private void OnFilterClicked(object? sender, EventArgs e)
    {
        if (sender is Button { CommandParameter: string filter })
        {
            _filter = filter;
            UpdateFilterButtons();
            Reload();
        }
    }

    private void OnLoadOlderClicked(object? sender, EventArgs e) => LoadPage();

    private void UpdateFilterButtons()
    {
        foreach (var (button, key) in new[] { (FilterAll, "all"), (FilterSuccess, "success"), (FilterFailed, "failed") })
        {
            var selected = key == _filter;
            button.BackgroundColor = selected ? Color.FromArgb("#0F766E") : Color.FromArgb("#1F8A9BA3");
            button.TextColor = selected ? Colors.White : Color.FromArgb("#8A9BA3");
            button.CornerRadius = 12;
            button.HeightRequest = 42;
            button.FontFamily = "OpenSansSemibold";
        }
    }

    private void Reload()
    {
        _offset = 0;
        Messages.Clear();
        try
        {
            var stats = Store.GetStats();
            TotalLabel.Text = stats.Total.ToString(CultureInfo.CurrentCulture);
            SuccessLabel.Text = stats.Success.ToString(CultureInfo.CurrentCulture);
            FailedLabel.Text = stats.Failed.ToString(CultureInfo.CurrentCulture);
        }
        catch (SqliteException exception)
        {
            HistoryStatusLabel.Text = $"Could not load message history: {exception.Message}";
            _ = PopupDialog.ShowAsync(this, "Couldn't load message totals", "The message database is unavailable. Restart the app and try again.", PopupKind.Error);
            return;
        }

        LoadPage();
    }

    private void LoadPage()
    {
        try
        {
            var status = _filter switch
            {
                "success" => SmsStatus.Success,
                "failed" => SmsStatus.Failed,
                _ => null
            };
            var records = Store.List(status, PageSize, _offset);
            foreach (var record in records)
            {
                Messages.Add(MessageHistoryItem.From(record));
            }

            _offset += records.Count;
            LoadOlderButton.IsVisible = records.Count == PageSize;
            EmptyCard.IsVisible = _offset == 0;
            HistoryStatusLabel.Text = _offset == 0 ? string.Empty : $"Showing {_offset} message(s) stored on this device.";
        }
        catch (SqliteException exception)
        {
            HistoryStatusLabel.Text = $"Could not load message history: {exception.Message}";
            _ = PopupDialog.ShowAsync(this, "Couldn't load messages", "Try the filter again or restart the app.", PopupKind.Error);
        }
    }

    private async void OnBackClicked(object? sender, EventArgs e)
    {
        try
        {
            await Shell.Current.GoToAsync("..");
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            await PopupDialog.ShowAsync(this, "Couldn't go back", "Please try again.", PopupKind.Error);
        }
    }

    public sealed record MessageHistoryItem(
        string Destination,
        string Message,
        string StatusText,
        Color BadgeBackground,
        Color BadgeForeground,
        string CreatedAtText,
        string? Error)
    {
        public bool HasError => !string.IsNullOrWhiteSpace(Error);

        public static MessageHistoryItem From(SmsMessageRecord record)
        {
            var success = record.Status == SmsStatus.Success;
            return new MessageHistoryItem(
                record.Destination,
                record.Message,
                success ? "SUCCESS" : "FAILED",
                Color.FromArgb(success ? "#2622C55E" : "#26EF4444"),
                Color.FromArgb(success ? "#16A34A" : "#DC2626"),
                record.CreatedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture),
                record.Error);
        }
    }
}
