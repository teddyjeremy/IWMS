using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using IWMS.Desktop.Services;

namespace IWMS.Desktop;

public partial class ReceiveGoodsWindow : Window
{
    private readonly ApiClient _api;
    private readonly Guid _purchaseOrderId;
    private readonly List<ReceiptLineEditor> _editors = new();

    public ReceiveGoodsWindow(ApiClient api, Guid purchaseOrderId)
    {
        InitializeComponent();
        _api = api;
        _purchaseOrderId = purchaseOrderId;
        Loaded += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        SaveButton.IsEnabled = false;
        try
        {
            var locations = new List<ChoiceItem>();
            var warehouses = await _api.GetJsonAsync("api/warehouses");
            foreach (var warehouse in warehouses.EnumerateArray())
            {
                var warehouseName = warehouse.GetProperty("name").GetString() ?? "Warehouse";
                if (!warehouse.TryGetProperty("locations", out var bins))
                    continue;
                foreach (var bin in bins.EnumerateArray())
                {
                    if (bin.TryGetProperty("isActive", out var active) && active.ValueKind == JsonValueKind.False)
                        continue;
                    locations.Add(new ChoiceItem(bin.GetProperty("id").GetString()!, $"{warehouseName} / {bin.GetProperty("code").GetString()}"));
                }
            }

            var order = await _api.GetJsonAsync($"api/purchase-orders/{_purchaseOrderId}");
            HeaderText.Text = order.GetProperty("orderNumber").GetString() ?? "Receive goods";
            var status = order.GetProperty("status").GetString();
            SubtitleText.Text = $"Status: {status}. Only approved or partially received orders can be received.";
            if (status is not ("Approved" or "PartiallyReceived"))
            {
                ErrorText.Text = "This order is not ready to receive. Approve it first, or choose an order that still has outstanding quantity.";
                return;
            }

            foreach (var line in order.GetProperty("lines").EnumerateArray())
            {
                var outstanding = line.GetProperty("quantityOutstanding").GetDecimal();
                if (outstanding <= 0)
                    continue;
                _editors.Add(AddEditor(line, outstanding, locations));
            }

            if (_editors.Count == 0)
                ErrorText.Text = "Every line on this order is already fully received.";
            else if (locations.Count == 0)
                ErrorText.Text = "No active storage locations were found. Add a warehouse location before receiving.";
            else
                SaveButton.IsEnabled = true;
        }
        catch (Exception ex)
        {
            ErrorText.Text = ex.Message;
        }
    }

    private ReceiptLineEditor AddEditor(JsonElement line, decimal outstanding, List<ChoiceItem> locations)
    {
        var card = new Border
        {
            Background = System.Windows.Media.Brushes.White,
            BorderBrush = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#E1E8EE")!,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 10)
        };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var title = new TextBlock
        {
            Text = $"{line.GetProperty("sku").GetString()} — {line.GetProperty("productName").GetString()}  ·  outstanding {outstanding}",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 8, 6),
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetColumnSpan(title, 4);
        grid.Children.Add(title);

        var accepted = new TextBox { Text = outstanding.ToString(CultureInfo.InvariantCulture), Margin = new Thickness(0, 0, 8, 0) };
        var rejected = new TextBox { Text = "0", Margin = new Thickness(0, 0, 8, 0) };
        var location = new ComboBox { ItemsSource = locations, DisplayMemberPath = "Label", Margin = new Thickness(0, 0, 0, 0) };
        if (locations.Count > 0) location.SelectedIndex = 0;
        var note = new TextBox { Margin = new Thickness(0, 8, 0, 0) };
        var lot = new TextBox { Margin = new Thickness(0, 4, 8, 0) };
        var expiry = new TextBox { Margin = new Thickness(0, 4, 8, 0) };
        var serials = new TextBox { Margin = new Thickness(0, 4, 0, 0) };
        Grid.SetRow(accepted, 1);
        Grid.SetRow(rejected, 1);
        Grid.SetColumn(rejected, 1);
        Grid.SetRow(location, 1);
        Grid.SetColumn(location, 2);
        Grid.SetColumnSpan(location, 2);
        grid.Children.Add(accepted);
        grid.Children.Add(rejected);
        grid.Children.Add(location);
        var extras = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        extras.ColumnDefinitions.Add(new ColumnDefinition());
        extras.ColumnDefinitions.Add(new ColumnDefinition());
        extras.ColumnDefinitions.Add(new ColumnDefinition());
        extras.Children.Add(new StackPanel { Children = { new TextBlock { Text = "Lot / batch" }, lot } });
        Grid.SetColumn(extras.Children[0], 0);
        var expiryPanel = new StackPanel { Margin = new Thickness(8, 0, 8, 0), Children = { new TextBlock { Text = "Expiry UTC" }, expiry } };
        Grid.SetColumn(expiryPanel, 1);
        extras.Children.Add(expiryPanel);
        var serialPanel = new StackPanel { Children = { new TextBlock { Text = "Serials" }, serials } };
        Grid.SetColumn(serialPanel, 2);
        extras.Children.Add(serialPanel);
        card.Child = new StackPanel
        {
            Children =
            {
                grid,
                extras,
                new TextBlock { Text = "Discrepancy note (required if rejected or short)", Margin = new Thickness(0, 8, 0, 2), Foreground = System.Windows.Media.Brushes.Gray },
                note
            }
        };
        LinesHost.Children.Add(card);
        return new ReceiptLineEditor(line.GetProperty("id").GetGuid(), outstanding, accepted, rejected, location, note, lot, expiry, serials);
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = string.Empty;
        var payload = new List<object>();
        foreach (var editor in _editors)
        {
            if (!decimal.TryParse(editor.Accepted.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out var accepted) || accepted < 0 ||
                !decimal.TryParse(editor.Rejected.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out var rejected) || rejected < 0)
            {
                ErrorText.Text = "Accepted and rejected quantities must be zero or greater.";
                return;
            }
            if (accepted == 0 && rejected == 0)
                continue;
            if (editor.Location.SelectedItem is not ChoiceItem location || !Guid.TryParse(location.Id, out var locationId))
            {
                ErrorText.Text = "Choose a storage location for every line you are receiving.";
                return;
            }
            if ((rejected > 0 || accepted < editor.Outstanding) && string.IsNullOrWhiteSpace(editor.Note.Text))
            {
                ErrorText.Text = "Add a discrepancy note when a line is short or has rejected quantity.";
                return;
            }
            payload.Add(new
            {
                purchaseOrderLineId = editor.LineId,
                storageLocationId = locationId,
                quantityAccepted = accepted,
                quantityRejected = rejected,
                discrepancyNote = editor.Note.Text.Trim(),
                lotNumber = string.IsNullOrWhiteSpace(editor.Lot.Text) ? null : editor.Lot.Text.Trim(),
                expiryUtc = string.IsNullOrWhiteSpace(editor.Expiry.Text) ? null : editor.Expiry.Text.Trim(),
                serials = string.IsNullOrWhiteSpace(editor.Serials.Text) ? null : editor.Serials.Text.Trim()
            });
        }

        if (payload.Count == 0)
        {
            ErrorText.Text = "Enter a quantity on at least one line.";
            return;
        }

        SaveButton.IsEnabled = false;
        try
        {
            await _api.PostJsonAsync("api/receipts", new
            {
                purchaseOrderId = _purchaseOrderId,
                receivedByUserId = Guid.Empty,
                idempotencyKey = Guid.NewGuid(),
                allowOverReceipt = false,
                notes = NotesBox.Text.Trim(),
                lines = payload
            });
            DialogResult = true;
        }
        catch (Exception ex)
        {
            ErrorText.Text = ex.Message;
            SaveButton.IsEnabled = true;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private sealed record ChoiceItem(string Id, string Label);
    private sealed record ReceiptLineEditor(Guid LineId, decimal Outstanding, TextBox Accepted, TextBox Rejected, ComboBox Location, TextBox Note, TextBox Lot, TextBox Expiry, TextBox Serials);
}
