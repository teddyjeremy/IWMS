using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using System.Windows;
using IWMS.Desktop.Services;

namespace IWMS.Desktop;

public partial class PurchaseOrderWindow : Window
{
    private readonly ApiClient _api;
    private readonly List<ChoiceItem> _suppliers = new();
    private readonly List<ChoiceItem> _products = new();
    private readonly ObservableCollection<PoLineRow> _lines = new();

    public PurchaseOrderWindow(ApiClient api)
    {
        InitializeComponent();
        _api = api;
        LinesGrid.ItemsSource = _lines;
        Loaded += async (_, _) => await LoadChoicesAsync();
    }

    private async Task LoadChoicesAsync()
    {
        try
        {
            var suppliers = await _api.GetListJsonAsync("api/suppliers?pageSize=100&includeInactive=false");
            foreach (var item in Items(suppliers))
            {
                _suppliers.Add(new ChoiceItem(item.GetProperty("id").GetString()!, item.GetProperty("name").GetString() ?? "Supplier"));
            }
            SupplierBox.ItemsSource = _suppliers;

            var products = await _api.GetListJsonAsync("api/products?pageSize=100");
            foreach (var item in Items(products))
            {
                if (item.TryGetProperty("isActive", out var active) && active.ValueKind == JsonValueKind.False)
                    continue;
                var sku = item.GetProperty("sku").GetString();
                var name = item.GetProperty("name").GetString();
                _products.Add(new ChoiceItem(item.GetProperty("id").GetString()!, $"{sku} — {name}"));
            }
            ProductBox.ItemsSource = _products;
            if (_suppliers.Count > 0) SupplierBox.SelectedIndex = 0;
            if (_products.Count > 0) ProductBox.SelectedIndex = 0;
            if (_suppliers.Count == 0 || _products.Count == 0)
                ErrorText.Text = "An active supplier and product are required before a purchase order can be created.";
        }
        catch (Exception ex)
        {
            ErrorText.Text = ex.Message;
        }
    }

    private void AddLine_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = string.Empty;
        if (ProductBox.SelectedItem is not ChoiceItem product)
        {
            ErrorText.Text = "Choose a product.";
            return;
        }
        if (!decimal.TryParse(QuantityBox.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out var quantity) || quantity <= 0)
        {
            ErrorText.Text = "Quantity must be a positive number.";
            return;
        }
        if (!decimal.TryParse(PriceBox.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out var price) || price < 0)
        {
            ErrorText.Text = "Expected unit price must be zero or greater.";
            return;
        }
        _lines.Add(new PoLineRow(product.Id, product.Label, quantity, price));
    }

    private void RemoveLine_Click(object sender, RoutedEventArgs e)
    {
        if (LinesGrid.SelectedItem is PoLineRow row)
            _lines.Remove(row);
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = string.Empty;
        if (SupplierBox.SelectedItem is not ChoiceItem supplier || !Guid.TryParse(supplier.Id, out var supplierId))
        {
            ErrorText.Text = "Choose a supplier.";
            return;
        }
        if (_lines.Count == 0)
        {
            ErrorText.Text = "Add at least one line.";
            return;
        }

        DateTime? expected = DeliveryPicker.SelectedDate?.ToUniversalTime();
        SaveButton.IsEnabled = false;
        try
        {
            await _api.PostJsonAsync("api/purchase-orders", new
            {
                supplierId,
                expectedDeliveryUtc = expected,
                notes = NotesBox.Text.Trim(),
                lines = _lines.Select(line => new { productId = line.ProductId, quantity = line.Quantity, expectedUnitPrice = line.UnitPrice }).ToArray(),
                version = 0
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

    private static IEnumerable<JsonElement> Items(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("items", out var items))
            root = items;
        return root.ValueKind == JsonValueKind.Array ? root.EnumerateArray().ToArray() : Array.Empty<JsonElement>();
    }

    private sealed record ChoiceItem(string Id, string Label);
    private sealed record PoLineRow(string ProductId, string ProductLabel, decimal Quantity, decimal UnitPrice)
    {
        public decimal LineTotal => Quantity * UnitPrice;
    }
}
