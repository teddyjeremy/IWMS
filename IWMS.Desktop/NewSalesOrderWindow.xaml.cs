using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using System.Windows;
using IWMS.Desktop.Services;

namespace IWMS.Desktop;

public partial class NewSalesOrderWindow : Window
{
    private readonly ApiClient _api;
    private readonly Guid? _orderId;
    private uint _version;
    private readonly List<ChoiceItem> _customers = new();
    private readonly List<ChoiceItem> _products = new();
    private readonly ObservableCollection<OrderLineRow> _lines = new();

    public NewSalesOrderWindow(ApiClient api, Guid? orderId = null)
    {
        InitializeComponent();
        _api = api;
        _orderId = orderId;
        LinesGrid.ItemsSource = _lines;
        if (_orderId.HasValue)
        {
            Title = "Edit draft sales order";
            HeaderText.Text = "Edit draft sales order";
            SaveButton.Content = "Save changes";
        }
        Loaded += async (_, _) => await LoadChoicesAsync();
    }

    private async Task LoadChoicesAsync()
    {
        try
        {
            var customers = await _api.GetJsonAsync("api/customers");
            foreach (var item in ItemsFrom(customers))
            {
                if (item.TryGetProperty("isActive", out var active) && active.ValueKind == JsonValueKind.False)
                    continue;
                var id = item.GetProperty("id").GetString()!;
                var name = item.TryGetProperty("name", out var n) ? n.GetString() : "(unnamed customer)";
                _customers.Add(new ChoiceItem(id, name ?? "(unnamed customer)"));
            }
            CustomerBox.ItemsSource = _customers;

            var products = await _api.GetJsonAsync("api/products?page=1&pageSize=100");
            foreach (var item in ItemsFrom(products))
            {
                if (item.TryGetProperty("isActive", out var active) && active.ValueKind == JsonValueKind.False)
                    continue;
                var id = item.GetProperty("id").GetString()!;
                var sku = item.TryGetProperty("sku", out var skuValue) ? skuValue.GetString() : "";
                var name = item.TryGetProperty("name", out var nameValue) ? nameValue.GetString() : "(unnamed product)";
                _products.Add(new ChoiceItem(id, $"{sku} — {name}"));
            }
            ProductBox.ItemsSource = _products;

            if (_orderId.HasValue)
                await LoadDraftAsync(_orderId.Value);
            else
            {
                if (_customers.Count > 0) CustomerBox.SelectedIndex = 0;
                if (_products.Count > 0) ProductBox.SelectedIndex = 0;
            }

            if (_customers.Count == 0 || _products.Count == 0)
                ErrorText.Text = "Add an active customer and product before creating an order.";
        }
        catch (Exception ex) { ErrorText.Text = ex.Message; }
    }

    private async Task LoadDraftAsync(Guid id)
    {
        var order = await _api.GetJsonAsync($"api/sales-orders/{id}");
        var status = order.GetProperty("status").GetInt32();
        if (status != 1)
            throw new InvalidOperationException("Only draft sales orders can be edited.");
        _version = order.GetProperty("version").GetUInt32();
        var customerId = order.GetProperty("customerId").GetString();
        CustomerBox.SelectedValue = customerId;
        if (CustomerBox.SelectedItem is null)
            throw new InvalidOperationException("The order's customer is no longer active or available.");

        if (order.TryGetProperty("requiredDateUtc", out var requiredDate) &&
            requiredDate.ValueKind == JsonValueKind.String &&
            DateTime.TryParse(requiredDate.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date))
            RequiredDatePicker.SelectedDate = date.ToLocalTime().Date;

        NotesBox.Text = order.TryGetProperty("notes", out var notes) ? notes.GetString() ?? "" : "";
        foreach (var line in order.GetProperty("lines").EnumerateArray())
        {
            var productId = line.GetProperty("productId").GetString()!;
            var product = _products.FirstOrDefault(p => p.Id == productId);
            if (product is null)
                throw new InvalidOperationException("An order product is inactive or missing. The draft cannot be safely edited.");
            _lines.Add(new OrderLineRow(productId, product.Label,
                line.GetProperty("quantityOrdered").GetDecimal(),
                line.GetProperty("unitPrice").GetDecimal()));
        }
        RefreshTotal();
    }

    private static IEnumerable<JsonElement> ItemsFrom(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("items", out var items))
            root = items;
        return root.ValueKind == JsonValueKind.Array ? root.EnumerateArray().ToArray() : Array.Empty<JsonElement>();
    }

    private void AddLine_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = string.Empty;
        if (ProductBox.SelectedValue is not string productId ||
            !_products.FirstOrDefault(p => p.Id == productId).IsValid())
        {
            ErrorText.Text = "Choose a product.";
            return;
        }
        if (!decimal.TryParse(QuantityBox.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out var quantity) || quantity <= 0)
        {
            ErrorText.Text = "Quantity must be greater than zero.";
            return;
        }
        if (!decimal.TryParse(UnitPriceBox.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out var unitPrice) || unitPrice < 0)
        {
            ErrorText.Text = "Unit price must be zero or greater. Use a dot for decimal values.";
            return;
        }

        var product = _products.First(p => p.Id == productId);
        _lines.Add(new OrderLineRow(productId, product.Label, quantity, unitPrice));
        RefreshTotal();
        QuantityBox.Text = "1";
    }

    private void RemoveLine_Click(object sender, RoutedEventArgs e)
    {
        if (LinesGrid.SelectedItem is not OrderLineRow selected)
        {
            ErrorText.Text = "Select a line to remove.";
            return;
        }
        _lines.Remove(selected);
        RefreshTotal();
    }

    private void RefreshTotal() =>
        TotalText.Text = _lines.Sum(line => line.LineTotal).ToString("N2", CultureInfo.InvariantCulture);

    private async void Create_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = string.Empty;
        if (CustomerBox.SelectedValue is not string customerId)
        {
            ErrorText.Text = "Choose a customer.";
            return;
        }
        if (_lines.Count == 0)
        {
            ErrorText.Text = "Add at least one order line.";
            return;
        }

        try
        {
            SaveButton.IsEnabled = false;
            DateTime? requiredDateUtc = RequiredDatePicker.SelectedDate.HasValue
                ? DateTime.SpecifyKind(RequiredDatePicker.SelectedDate.Value.Date, DateTimeKind.Utc)
                : null;
            var payload = new
            {
                customerId,
                lines = _lines.Select(line => new
                {
                    productId = line.ProductId,
                    quantityOrdered = line.Quantity,
                    unitPrice = line.UnitPrice
                }).ToArray(),
                requiredDateUtc,
                notes = NotesBox.Text.Trim()
            };

            if (_orderId.HasValue)
                await _api.PutJsonAsync($"api/sales-orders/{_orderId.Value}", new
                {
                    payload.customerId,
                    payload.lines,
                    version = _version,
                    payload.requiredDateUtc,
                    payload.notes
                });
            else
                await _api.PostJsonAsync("api/sales-orders", payload);

            DialogResult = true;
        }
        catch (Exception ex) { ErrorText.Text = ex.Message; }
        finally { SaveButton.IsEnabled = true; }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private sealed record ChoiceItem(string Id, string Label);
    private sealed record OrderLineRow(string ProductId, string ProductLabel, decimal Quantity, decimal UnitPrice)
    {
        public decimal LineTotal => Quantity * UnitPrice;
    }
}

internal static class SalesOrderChoiceExtensions
{
    public static bool IsValid(this object? value) => value is NewSalesOrderWindow.ChoiceItem;
}