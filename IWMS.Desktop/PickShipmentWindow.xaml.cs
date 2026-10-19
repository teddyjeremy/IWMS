using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using IWMS.Desktop.Services;

namespace IWMS.Desktop;

public partial class PickShipmentWindow : Window
{
    private readonly ApiClient _api;
    private readonly Guid _shipmentId;
    private readonly ObservableCollection<PickLine> _lines = new();

    public PickShipmentWindow(ApiClient api, Guid shipmentId)
    {
        InitializeComponent();
        _api = api;
        _shipmentId = shipmentId;
        LinesGrid.ItemsSource = _lines;
        Loaded += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            var json = await _api.GetJsonAsync($"api/fulfillment/shipments/{_shipmentId}");
            HeadingText.Text = $"Pick shipment {json.GetProperty("shipmentNumber").GetString()}";
            foreach (var item in json.GetProperty("lines").EnumerateArray())
            {
                _lines.Add(new PickLine
                {
                    Id = Guid.Parse(item.GetProperty("id").GetString()!),
                    Sku = item.GetProperty("sku").GetString() ?? "",
                    ProductName = item.GetProperty("productName").GetString() ?? "",
                    Location = item.GetProperty("location").GetString() ?? "",
                    QuantityToPick = item.GetProperty("quantityToPick").GetDecimal(),
                    QuantityPicked = item.GetProperty("quantityPicked").GetDecimal()
                });
            }
        }
        catch (Exception ex) { ErrorText.Text = ex.Message; }
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        LinesGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        LinesGrid.CommitEdit(DataGridEditingUnit.Row, true);
        ErrorText.Text = string.Empty;
        if (_lines.Any(l => l.QuantityPicked < 0 || l.QuantityPicked > l.QuantityToPick))
        {
            ErrorText.Text = "Picked quantity must be between zero and the quantity to pick.";
            return;
        }

        try
        {
            await _api.PostJsonAsync($"api/fulfillment/shipments/{_shipmentId}/pick", new
            {
                lines = _lines.Select(l => new { shipmentLineId = l.Id, quantityPicked = l.QuantityPicked }).ToArray()
            });
            DialogResult = true;
        }
        catch (Exception ex) { ErrorText.Text = ex.Message; }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    public sealed class PickLine
    {
        public Guid Id { get; set; }
        public string Sku { get; set; } = "";
        public string ProductName { get; set; } = "";
        public string Location { get; set; } = "";
        public decimal QuantityToPick { get; set; }
        public decimal QuantityPicked { get; set; }
    }
}