using System.Globalization;
using System.Text.Json;
using System.Windows;
using IWMS.Desktop.Services;

namespace IWMS.Desktop;

public partial class EditProductWindow : Window
{
    private readonly ApiClient _api;
    private readonly Guid _productId;
    private readonly bool _creating;
    private uint _version;

    public EditProductWindow(ApiClient api, Guid productId) : this(api, productId, creating: false)
    {
    }

    public EditProductWindow(ApiClient api) : this(api, Guid.Empty, creating: true)
    {
    }

    private EditProductWindow(ApiClient api, Guid productId, bool creating)
    {
        InitializeComponent();
        _api = api;
        _productId = productId;
        _creating = creating;
        BaseUnitBox.ItemsSource = new[] { "Each", "Box", "Carton", "Kilogram", "Litre", "Metre" };
        if (_creating)
        {
            Title = "New product";
            HeaderText.Text = "New product";
            SubtitleText.Text = "Add a catalogue item. Stock quantities are posted later through receipts, issues and adjustments.";
            SaveButton.Content = "Create product";
            ActiveBox.IsChecked = true;
            CostPriceBox.Text = "0";
            ReorderLevelBox.Text = "0";
        }
        Loaded += async (_, _) => await LoadProductAsync();
    }

    private async Task LoadProductAsync()
    {
        SaveButton.IsEnabled = false;
        try
        {
            var categories = await _api.GetJsonAsync("api/categories");
            foreach (var item in categories.EnumerateArray())
            {
                var id = item.GetProperty("id").GetString()!;
                var name = item.GetProperty("name").GetString() ?? "(unnamed category)";
                CategoryBox.Items.Add(new CategoryChoice(id, name));
            }

            if (_creating)
            {
                if (CategoryBox.Items.Count > 0) CategoryBox.SelectedIndex = 0;
                if (BaseUnitBox.Items.Count > 0) BaseUnitBox.SelectedIndex = 0;
                SaveButton.IsEnabled = CategoryBox.Items.Count > 0;
                if (CategoryBox.Items.Count == 0)
                    ErrorText.Text = "Create a category before adding a product.";
                return;
            }

            var product = await _api.GetJsonAsync($"api/products/{_productId}");
            SkuBox.Text = product.GetProperty("sku").GetString() ?? "";
            NameBox.Text = product.GetProperty("name").GetString() ?? "";
            DescriptionBox.Text = product.GetProperty("description").GetString() ?? "";
            BarcodeBox.Text = product.TryGetProperty("barcode", out var barcode) && barcode.ValueKind != JsonValueKind.Null
                ? barcode.GetString() ?? "" : "";
            CategoryBox.SelectedValue = product.GetProperty("categoryId").GetString();
            BaseUnitBox.SelectedItem = product.GetProperty("baseUnit").GetString();
            CostPriceBox.Text = product.GetProperty("costPrice").ToString();
            ReorderLevelBox.Text = product.GetProperty("reorderLevel").ToString();
            ActiveBox.IsChecked = product.GetProperty("isActive").GetBoolean();
            _version = product.GetProperty("version").GetUInt32();
            SaveButton.IsEnabled = true;
        }
        catch (Exception ex)
        {
            ErrorText.Text = ex.Message;
        }
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = string.Empty;
        if (string.IsNullOrWhiteSpace(SkuBox.Text) || string.IsNullOrWhiteSpace(NameBox.Text))
        {
            ErrorText.Text = "SKU and product name are required.";
            return;
        }

        if (CategoryBox.SelectedValue is not string categoryText ||
            !Guid.TryParse(categoryText, out var categoryId))
        {
            ErrorText.Text = "Choose a category.";
            return;
        }

        if (BaseUnitBox.SelectedItem is not string baseUnit)
        {
            ErrorText.Text = "Choose a base unit.";
            return;
        }

        if (!decimal.TryParse(CostPriceBox.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out var costPrice) || costPrice < 0)
        {
            ErrorText.Text = "Cost price must be a non-negative number (use a dot for decimals).";
            return;
        }

        if (!decimal.TryParse(ReorderLevelBox.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out var reorderLevel) || reorderLevel < 0)
        {
            ErrorText.Text = "Reorder level must be a non-negative number (use a dot for decimals).";
            return;
        }

        SaveButton.IsEnabled = false;
        try
        {
            if (_creating)
                await _api.PostJsonAsync("api/products", new
                {
                    sku = SkuBox.Text.Trim(),
                    name = NameBox.Text.Trim(),
                    description = DescriptionBox.Text.Trim(),
                    barcode = string.IsNullOrWhiteSpace(BarcodeBox.Text) ? null : BarcodeBox.Text.Trim(),
                    categoryId,
                    baseUnit,
                    costPrice,
                    reorderLevel,
                    isActive = ActiveBox.IsChecked == true,
                    version = 0
                });
            else
                await _api.PutJsonAsync($"api/products/{_productId}", new
                {
                    sku = SkuBox.Text.Trim(),
                    name = NameBox.Text.Trim(),
                    description = DescriptionBox.Text.Trim(),
                    barcode = string.IsNullOrWhiteSpace(BarcodeBox.Text) ? null : BarcodeBox.Text.Trim(),
                    categoryId,
                    baseUnit,
                    costPrice,
                    reorderLevel,
                    isActive = ActiveBox.IsChecked == true,
                    version = _version
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

    private sealed record CategoryChoice(string Id, string Name);
}