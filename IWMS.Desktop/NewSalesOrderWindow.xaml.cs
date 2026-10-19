using System.Globalization;
using System.Text.Json;
using System.Windows;
using IWMS.Desktop.Services;

namespace IWMS.Desktop;

public partial class NewSalesOrderWindow : Window
{
    private readonly ApiClient _api;

    public NewSalesOrderWindow(ApiClient api)
    {
        InitializeComponent();
        _api = api;
        Loaded += async (_, _) => await LoadChoicesAsync();
    }

    private async Task LoadChoicesAsync()
    {
        try
        {
            var customers = await _api.GetJsonAsync("api/customers");
            foreach (var item in ItemsFrom(customers))
                CustomerBox.Items.Add(new ChoiceItem(item.GetProperty("id").GetString()!,
                    item.GetProperty("name").GetString() ?? "(unnamed customer)"));

            var products = await _api.GetJsonAsync("api/products?page=1&pageSize=100");
            foreach (var item in ItemsFrom(products))
            {
                var id = item.GetProperty("id").GetString()!;
                var sku = item.TryGetProperty("sku", out var skuValue) ? skuValue.GetString() : "";
                var name = item.TryGetProperty("name", out var nameValue) ? nameValue.GetString() : "(unnamed product)";
                ProductBox.Items.Add(new ChoiceItem(id, $"{sku} — {name}"));
            }

            if (CustomerBox.Items.Count > 0) CustomerBox.SelectedIndex = 0;
            if (ProductBox.Items.Count > 0) ProductBox.SelectedIndex = 0;
            if (CustomerBox.Items.Count == 0 || ProductBox.Items.Count == 0)
                ErrorText.Text = "Add an active customer and product before creating an order.";
        }
        catch (Exception ex) { ErrorText.Text = ex.Message; }
    }

    private static IEnumerable<JsonElement> ItemsFrom(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("items", out var items))
            root = items;
        return root.ValueKind == JsonValueKind.Array ? root.EnumerateArray().ToArray() : Array.Empty<JsonElement>();
    }

    private async void Create_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = string.Empty;
        if (CustomerBox.SelectedValue is not string customerId ||
            ProductBox.SelectedValue is not string productId)
        {
            ErrorText.Text = "Choose a customer and product.";
            return;
        }
        if (!decimal.TryParse(QuantityBox.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out var quantity) ||
            quantity <= 0)
        {
            ErrorText.Text = "Quantity must be greater than zero.";
            return;
        }
        if (!decimal.TryParse(UnitPriceBox.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out var unitPrice) ||
            unitPrice < 0)
        {
            ErrorText.Text = "Unit price must be zero or greater. Use a dot for decimal values.";
            return;
        }

        try
        {
            DateTime? requiredDateUtc = RequiredDatePicker.SelectedDate.HasValue
                ? DateTime.SpecifyKind(RequiredDatePicker.SelectedDate.Value, DateTimeKind.Utc)
                : null;
            await _api.PostJsonAsync("api/sales-orders", new
            {
                customerId,
                lines = new[] { new { productId, quantityOrdered = quantity, unitPrice } },
                requiredDateUtc,
                notes = NotesBox.Text.Trim()
            });
            DialogResult = true;
        }
        catch (Exception ex) { ErrorText.Text = ex.Message; }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private sealed record ChoiceItem(string Id, string Label);
}