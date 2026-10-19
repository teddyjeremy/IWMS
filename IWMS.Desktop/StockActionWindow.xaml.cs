using System.Globalization;
using System.Text.Json;
using System.Windows;
using IWMS.Desktop.Services;

namespace IWMS.Desktop;

public partial class StockActionWindow : Window
{
    private readonly ApiClient _api;
    private readonly bool _adjust;

    public StockActionWindow(ApiClient api, bool adjust)
    {
        InitializeComponent();
        _api = api;
        _adjust = adjust;
        if (_adjust)
        {
            Title = "Adjust stock";
            HeaderText.Text = "Adjust stock";
            SubtitleText.Text = "Post a counted increase or decrease. A reason is stored on the ledger.";
            ReferenceLabel.Visibility = Visibility.Collapsed;
            ReferenceBox.Visibility = Visibility.Collapsed;
            SaveButton.Content = "Post adjustment";
        }
        else
        {
            Title = "Issue stock";
            HeaderText.Text = "Issue stock";
            SubtitleText.Text = "Issue available stock out of a location. This does not edit history in place.";
            DirectionPanel.Visibility = Visibility.Collapsed;
            SaveButton.Content = "Post issue";
        }
        Loaded += async (_, _) => await LoadChoicesAsync();
    }

    private async Task LoadChoicesAsync()
    {
        try
        {
            var products = new List<ChoiceItem>();
            foreach (var item in Items(await _api.GetListJsonAsync("api/products?pageSize=100")))
            {
                if (item.TryGetProperty("isActive", out var active) && active.ValueKind == JsonValueKind.False)
                    continue;
                products.Add(new ChoiceItem(item.GetProperty("id").GetString()!, $"{item.GetProperty("sku").GetString()} — {item.GetProperty("name").GetString()}"));
            }
            ProductBox.ItemsSource = products;
            if (products.Count > 0) ProductBox.SelectedIndex = 0;

            var locations = new List<ChoiceItem>();
            foreach (var warehouse in (await _api.GetJsonAsync("api/warehouses")).EnumerateArray())
            {
                var warehouseName = warehouse.GetProperty("name").GetString();
                if (!warehouse.TryGetProperty("locations", out var bins))
                    continue;
                foreach (var bin in bins.EnumerateArray())
                {
                    if (bin.TryGetProperty("isActive", out var active) && active.ValueKind == JsonValueKind.False)
                        continue;
                    locations.Add(new ChoiceItem(bin.GetProperty("id").GetString()!, $"{warehouseName} / {bin.GetProperty("code").GetString()}"));
                }
            }
            LocationBox.ItemsSource = locations;
            if (locations.Count > 0) LocationBox.SelectedIndex = 0;
            if (products.Count == 0 || locations.Count == 0)
                ErrorText.Text = "An active product and storage location are required.";
        }
        catch (Exception ex)
        {
            ErrorText.Text = ex.Message;
            SaveButton.IsEnabled = false;
        }
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = string.Empty;
        if (ProductBox.SelectedItem is not ChoiceItem product || !Guid.TryParse(product.Id, out var productId) ||
            LocationBox.SelectedItem is not ChoiceItem location || !Guid.TryParse(location.Id, out var locationId))
        {
            ErrorText.Text = "Choose a product and a storage location.";
            return;
        }
        if (!decimal.TryParse(QuantityBox.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out var quantity) || quantity <= 0)
        {
            ErrorText.Text = "Quantity must be greater than zero.";
            return;
        }
        if (string.IsNullOrWhiteSpace(ReasonBox.Text))
        {
            ErrorText.Text = "A reason is required.";
            return;
        }

        SaveButton.IsEnabled = false;
        try
        {
            if (_adjust)
            {
                await _api.PostJsonAsync("api/stock/adjust", new
                {
                    productId,
                    storageLocationId = locationId,
                    quantity,
                    isIncrease = IncreaseBox.IsChecked == true,
                    reason = ReasonBox.Text.Trim(),
                    performedByUserId = Guid.Empty,
                    idempotencyKey = Guid.NewGuid(),
                    lotNumber = string.IsNullOrWhiteSpace(LotBox.Text) ? null : LotBox.Text.Trim(),
                    serials = string.IsNullOrWhiteSpace(SerialsBox.Text) ? null : SerialsBox.Text.Trim()
                });
            }
            else
            {
                if (string.IsNullOrWhiteSpace(ReferenceBox.Text))
                {
                    ErrorText.Text = "A reference number is required.";
                    SaveButton.IsEnabled = true;
                    return;
                }
                await _api.PostJsonAsync("api/stock/issue", new
                {
                    productId,
                    storageLocationId = locationId,
                    quantity,
                    performedByUserId = Guid.Empty,
                    idempotencyKey = Guid.NewGuid(),
                    referenceNumber = ReferenceBox.Text.Trim(),
                    reason = ReasonBox.Text.Trim(),
                    lotNumber = string.IsNullOrWhiteSpace(LotBox.Text) ? null : LotBox.Text.Trim(),
                    serials = string.IsNullOrWhiteSpace(SerialsBox.Text) ? null : SerialsBox.Text.Trim()
                });
            }
            DialogResult = true;
        }
        catch (Exception ex)
        {
            ErrorText.Text = ex.Message;
            SaveButton.IsEnabled = true;
        }
    }

    private async void Suggest_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = string.Empty;
        if (ProductBox.SelectedItem is not ChoiceItem product || !Guid.TryParse(product.Id, out var productId) ||
            LocationBox.SelectedItem is not ChoiceItem location || !Guid.TryParse(location.Id, out var locationId))
        {
            ErrorText.Text = "Choose a product and a storage location first.";
            return;
        }
        if (!decimal.TryParse(QuantityBox.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out var quantity) || quantity <= 0)
        {
            ErrorText.Text = "Enter the quantity to pick.";
            return;
        }
        try
        {
            var picks = await _api.GetJsonAsync($"api/products/{productId}/fefo?locationId={locationId}&quantity={quantity.ToString(CultureInfo.InvariantCulture)}");
            if (picks.ValueKind != JsonValueKind.Array || picks.GetArrayLength() == 0)
            {
                ErrorText.Text = "No lots are available at this location.";
                return;
            }
            var summary = string.Join(", ", picks.EnumerateArray().Select(p => $"{p.GetProperty("lotNumber").GetString()} × {p.GetProperty("quantity").GetDecimal()}"));
            LotBox.Text = picks[0].GetProperty("lotNumber").GetString();
            ErrorText.Text = "FEFO suggestion: " + summary;
        }
        catch (Exception ex) { ErrorText.Text = ex.Message; }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private static IEnumerable<JsonElement> Items(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("items", out var items))
            root = items;
        return root.ValueKind == JsonValueKind.Array ? root.EnumerateArray().ToArray() : Array.Empty<JsonElement>();
    }

    private sealed record ChoiceItem(string Id, string Label);
}
