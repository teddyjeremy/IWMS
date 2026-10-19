using System.Data;
using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using IWMS.Desktop.Services;

namespace IWMS.Desktop;

public partial class MainWindow : Window
{
    private readonly ApiClient _api = new();
    private bool _showShipments;

    public MainWindow()
    {
        InitializeComponent();
        ShowDashboard();
    }

    private async void SignIn_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SignInWindow(_api) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        ConnectionLabel.Text = $"Connected as {_api.CurrentUserName}";
        StatusText.Text = "Connected to the IWMS API.";
        await LoadDashboardAsync();
    }

    private async void Navigate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string page }) return;
        PageTitle.Text = page;
        if (page == "Fulfillment") _showShipments = false;
        PageSubtitle.Text = page switch
        {
            "Customers" => "Customer records and account details",
            "Sales orders" => "Order entry and fulfillment status",
            "Fulfillment" => "Reservations, pick lists and dispatch",
            "Stock transfers" => "Warehouse-to-warehouse transfer documents",
            "Stock counts" => "Physical counts, variances and stock adjustments",
            "Returns" => "Customer and supplier return records",
            "Inventory" => "Stock position by product and location",
            "Purchasing" => "Purchase orders and approvals",
            "Reports" => "Inventory KPIs and downloadable reports",
            _ => "Inventory position and warehouse activity"
        };

        if (page == "Dashboard")
        {
            await LoadDashboardAsync();
            return;
        }

        await LoadWorkspaceAsync(page);
    }

    private void ShowDashboard()
    {
        PageContent.Content = new TextBlock
        {
            Text = "Connect to the API to view current stock value, low-stock products and order activity.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 16,
            Foreground = System.Windows.Media.Brushes.Gray,
            Margin = new Thickness(0, 8, 0, 0)
        };
    }

    private async Task LoadDashboardAsync()
    {
        if (!_api.IsSignedIn)
        {
            ShowDashboard();
            return;
        }

        try
        {
            var data = await _api.GetAsync<DashboardDto>("api/reports/dashboard");
            var panel = new UniformGrid { Columns = 3, Rows = 2, Margin = new Thickness(0, 4, 0, 0) };
            AddMetric(panel, "Stock value", data.StockValue.ToString("N2", CultureInfo.InvariantCulture));
            AddMetric(panel, "Low-stock products", data.LowStockProducts.ToString("N0"));
            AddMetric(panel, "Open purchase orders", data.OpenPurchaseOrders.ToString("N0"));
            AddMetric(panel, "Pending approvals", data.PendingApprovals.ToString("N0"));
            AddMetric(panel, "Open sales orders", data.OpenSalesOrders.ToString("N0"));
            AddMetric(panel, "Units reserved", data.UnitsReserved.ToString("N2", CultureInfo.InvariantCulture));
            PageContent.Content = panel;
            StatusText.Text = $"Last refreshed {DateTime.Now:T}.";
        }
        catch (Exception ex) { StatusText.Text = ex.Message; }
    }

    private async Task LoadWorkspaceAsync(string page)
    {
        if (!_api.IsSignedIn)
        {
            PageContent.Content = new TextBlock
            {
                Text = "Sign in to the IWMS API to access this workspace.",
                FontSize = 16,
                TextWrapping = TextWrapping.Wrap
            };
            return;
        }

        if (page == "Reports")
        {
            var reportPanel = new StackPanel();
            reportPanel.Children.Add(new TextBlock
            {
                Text = "Download a live inventory snapshot for analysis or record keeping.",
                FontSize = 15,
                Margin = new Thickness(0, 0, 0, 14)
            });
            var csvButton = new Button
            {
                Content = "Download inventory CSV",
                Width = 220,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            csvButton.Click += async (_, _) => await DownloadInventoryCsvAsync();
            reportPanel.Children.Add(csvButton);
            var pdfButton = new Button
            {
                Content = "Download inventory PDF",
                Width = 220,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 10, 0, 0)
            };
            pdfButton.Click += async (_, _) => await DownloadInventoryPdfAsync();
            reportPanel.Children.Add(pdfButton);
            PageContent.Content = reportPanel;
            return;
        }

        var endpoint = page switch
        {
            "Customers" => "api/customers",
            "Sales orders" => "api/sales-orders",
            "Inventory" => "api/products",
            "Purchasing" => "api/purchase-orders",
            "Fulfillment" => _showShipments ? "api/fulfillment/shipments" : "api/sales-orders",
            "Stock transfers" => "api/stock-transfers",
            "Stock counts" => "api/stock-counts",
            "Returns" => "api/returns",
            _ => string.Empty
        };

        if (string.IsNullOrEmpty(endpoint))
        {
            PageContent.Content = new TextBlock { Text = "This workspace is not configured yet." };
            return;
        }

        try
        {
            StatusText.Text = $"Loading {page.ToLowerInvariant()}...";
            var json = await _api.GetJsonAsync(endpoint);
            var dataGrid = CreateDataGrid(json);
            if (page is "Customers" or "Sales orders" or "Fulfillment" or "Stock transfers" or "Stock counts")
            {
                var workspace = new DockPanel();
                var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
                if (page is "Customers" or "Sales orders")
                {
                    var addButton = new Button
                    {
                        Content = page == "Customers" ? "New customer" : "New sales order",
                        Background = System.Windows.Media.Brushes.SteelBlue,
                        Foreground = System.Windows.Media.Brushes.White
                    };
                    addButton.Click += async (_, _) =>
                    {
                        bool? created;
                        if (page == "Customers")
                            created = new NewCustomerWindow(_api) { Owner = this }.ShowDialog();
                        else
                            created = new NewSalesOrderWindow(_api) { Owner = this }.ShowDialog();
                        if (created == true) await LoadWorkspaceAsync(page);
                    };
                    toolbar.Children.Add(addButton);
                    if (page == "Sales orders")
                    {
                        var confirmButton = new Button { Content = "Confirm selected" };
                        confirmButton.Click += async (_, _) => await RunSelectedFulfillmentActionAsync(dataGrid, "confirm-order");
                        var cancelOrderButton = new Button { Content = "Cancel selected" };
                        cancelOrderButton.Click += async (_, _) => await RunSelectedFulfillmentActionAsync(dataGrid, "cancel-order");
                        toolbar.Children.Add(confirmButton);
                        toolbar.Children.Add(cancelOrderButton);
                    }
                }
                else if (page == "Stock transfers")
                {
                    var approveButton = new Button { Content = "Approve selected" };
                    approveButton.Click += async (_, _) => await RunSelectedTransferActionAsync(dataGrid, "approve");
                    var rejectButton = new Button { Content = "Reject selected" };
                    rejectButton.Click += async (_, _) => await RunSelectedTransferActionAsync(dataGrid, "reject");
                    var dispatchTransferButton = new Button { Content = "Dispatch selected" };
                    dispatchTransferButton.Click += async (_, _) => await RunSelectedTransferActionAsync(dataGrid, "dispatch");
                    var receiveTransferButton = new Button { Content = "Receive full shipment" };
                    receiveTransferButton.Click += async (_, _) => await RunSelectedTransferActionAsync(dataGrid, "receive");
                    toolbar.Children.Add(approveButton);
                    toolbar.Children.Add(rejectButton);
                    toolbar.Children.Add(dispatchTransferButton);
                    toolbar.Children.Add(receiveTransferButton);
                }
                else if (page == "Stock counts")
                {
                    var startCountButton = new Button { Content = "Start count" };
                    startCountButton.Click += async (_, _) =>
                    {
                        var warehouseText = Prompt("Start stock count", "Enter warehouse ID (GUID):");
                        if (!Guid.TryParse(warehouseText, out var warehouseId))
                        {
                            StatusText.Text = "Enter a valid warehouse ID.";
                            return;
                        }
                        try
                        {
                            await _api.PostJsonAsync("api/stock-counts", new { warehouseId, notes = "Started from desktop client" });
                            await LoadWorkspaceAsync("Stock counts");
                            StatusText.Text = "Stock count started.";
                        }
                        catch (Exception ex) { StatusText.Text = ex.Message; }
                    };
                    toolbar.Children.Add(startCountButton);
                }
                else if (!_showShipments)
                {
                    var reserveButton = new Button { Content = "Reserve stock" };
                    reserveButton.Click += async (_, _) => await RunSelectedFulfillmentActionAsync(dataGrid, "reserve");
                    var pickListButton = new Button { Content = "Create pick list" };
                    pickListButton.Click += async (_, _) => await RunSelectedFulfillmentActionAsync(dataGrid, "pick-list");
                    var closeShortButton = new Button { Content = "Close order short" };
                    closeShortButton.Click += async (_, _) =>
                    {
                        var confirm = MessageBox.Show(
                            "Close the selected order with its remaining quantity unfulfilled? Active reservations will be released and open pick lists cancelled.",
                            "Close order short",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Warning);
                        if (confirm == MessageBoxResult.Yes)
                            await RunSelectedFulfillmentActionAsync(dataGrid, "close-short");
                    };
                    var shipmentsButton = new Button { Content = "Show pick lists" };
                    shipmentsButton.Click += async (_, _) => { _showShipments = true; await LoadWorkspaceAsync("Fulfillment"); };
                    toolbar.Children.Add(reserveButton);
                    toolbar.Children.Add(pickListButton);
                    toolbar.Children.Add(closeShortButton);
                    toolbar.Children.Add(shipmentsButton);
                }
                else
                {
                    var ordersButton = new Button { Content = "Show sales orders" };
                    ordersButton.Click += async (_, _) => { _showShipments = false; await LoadWorkspaceAsync("Fulfillment"); };
                    var pickButton = new Button { Content = "Record picked quantities" };
                    pickButton.Click += async (_, _) =>
                    {
                        var id = GetSelectedGuid(dataGrid);
                        if (id is null) { StatusText.Text = "Select a shipment first."; return; }
                        var dialog = new PickShipmentWindow(_api, id.Value) { Owner = this };
                        if (dialog.ShowDialog() == true) await LoadWorkspaceAsync("Fulfillment");
                    };
                    var dispatchButton = new Button { Content = "Dispatch shipment" };
                    dispatchButton.Click += async (_, _) => await RunSelectedFulfillmentActionAsync(dataGrid, "dispatch");
                    var cancelButton = new Button { Content = "Cancel shipment" };
                    cancelButton.Click += async (_, _) => await RunSelectedFulfillmentActionAsync(dataGrid, "cancel-shipment");
                    toolbar.Children.Add(ordersButton);
                    toolbar.Children.Add(pickButton);
                    toolbar.Children.Add(dispatchButton);
                    toolbar.Children.Add(cancelButton);
                }
                DockPanel.SetDock(toolbar, Dock.Top);
                workspace.Children.Add(toolbar);
                workspace.Children.Add(dataGrid);
                PageContent.Content = workspace;
            }
            else
            {
                PageContent.Content = dataGrid;
            }
            StatusText.Text = $"Loaded {page.ToLowerInvariant()} from the IWMS API.";
        }
        catch (Exception ex)
        {
            PageContent.Content = new TextBlock { Text = ex.Message, TextWrapping = TextWrapping.Wrap };
            StatusText.Text = "Could not load data.";
        }
    }

    private static DataGrid CreateDataGrid(JsonElement json)
    {
        if (json.ValueKind == JsonValueKind.Object && json.TryGetProperty("items", out var items))
            json = items;

        var table = new DataTable();
        if (json.ValueKind != JsonValueKind.Array || json.GetArrayLength() == 0)
        {
            table.Columns.Add("Result");
            table.Rows.Add(json.ValueKind == JsonValueKind.Array ? "No records found." : json.ToString());
        }
        else
        {
            var records = json.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Object).ToList();
            if (records.Count == 0)
            {
                table.Columns.Add("Result");
                table.Rows.Add("No records found.");
            }
            else
            {
                var keys = records[0].EnumerateObject().Select(p => p.Name).ToList();
                foreach (var key in keys) table.Columns.Add(key);
                foreach (var record in records)
                {
                    var row = table.NewRow();
                    foreach (var key in keys)
                        if (record.TryGetProperty(key, out var value))
                            row[key] = FormatJsonValue(value);
                    table.Rows.Add(row);
                }
            }
        }

        return new DataGrid
        {
            ItemsSource = table.DefaultView,
            AutoGenerateColumns = true,
            IsReadOnly = true,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            AlternatingRowBackground = System.Windows.Media.Brushes.WhiteSmoke
        };
    }

    private static string FormatJsonValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null => string.Empty,
        JsonValueKind.Object or JsonValueKind.Array => value.ToString(),
        _ => value.ToString()
    };



    private async Task DownloadInventoryPdfAsync()
    {
        try
        {
            var bytes = await _api.GetBytesAsync("api/reports/inventory.pdf");
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                FileName = "iwms-inventory.pdf",
                Filter = "PDF files (*.pdf)|*.pdf"
            };
            if (dialog.ShowDialog(this) == true)
            {
                await System.IO.File.WriteAllBytesAsync(dialog.FileName, bytes);
                StatusText.Text = "Inventory PDF saved.";
            }
        }
        catch (Exception ex) { StatusText.Text = ex.Message; }
    }

    private static Guid? GetSelectedGuid(DataGrid grid)
    {
        if (grid.SelectedItem is not DataRowView row || !row.Row.Table.Columns.Contains("id"))
            return null;
        return Guid.TryParse(Convert.ToString(row["id"], CultureInfo.InvariantCulture), out var id) ? id : null;
    }

    private async Task RunSelectedFulfillmentActionAsync(DataGrid grid, string action)
    {
        var id = GetSelectedGuid(grid);
        if (id is null) { StatusText.Text = "Select a row first."; return; }
        try
        {
            if (action == "reserve")
                await _api.PostJsonAsync($"api/fulfillment/sales-orders/{id}/reserve", new { });
            else if (action == "pick-list")
                await _api.PostJsonAsync($"api/fulfillment/sales-orders/{id}/pick-list", new { });
            else if (action == "confirm-order")
                await _api.PostJsonAsync($"api/sales-orders/{id}/confirm", new { });
            else if (action == "cancel-order")
                await _api.PostJsonAsync($"api/fulfillment/sales-orders/{id}/cancel", new { });
            else if (action == "close-short")
                await _api.PostJsonAsync($"api/fulfillment/sales-orders/{id}/close-short", new { });
            else if (action == "dispatch")
                await _api.PostJsonAsync($"api/fulfillment/shipments/{id}/dispatch", new { });
            else if (action == "cancel-shipment")
                await _api.PostJsonAsync($"api/fulfillment/shipments/{id}/cancel", new { });
            StatusText.Text = "Fulfillment action completed.";
            await LoadWorkspaceAsync("Fulfillment");
        }
        catch (Exception ex) { StatusText.Text = ex.Message; }
    }

    private async Task RunSelectedTransferActionAsync(DataGrid grid, string action)
    {
        var id = GetSelectedGuid(grid);
        if (id is null) { StatusText.Text = "Select a transfer first."; return; }
        try
        {
            if (action == "approve")
                await _api.PostJsonAsync($"api/stock-transfers/{id}/approve", new { });
            else if (action == "reject")
            {
                var reason = Prompt("Reject transfer", "Reason for rejection:");
                if (string.IsNullOrWhiteSpace(reason)) return;
                await _api.PostJsonAsync($"api/stock-transfers/{id}/reject", new { reason });
            }
            else if (action == "dispatch")
                await _api.PostJsonAsync($"api/stock-transfers/{id}/dispatch", new { });
            else if (action == "receive")
            {
                var detail = await _api.GetJsonAsync($"api/stock-transfers/{id}");
                if (!detail.TryGetProperty("lines", out var lines) || lines.ValueKind != JsonValueKind.Array)
                    throw new InvalidOperationException("The transfer response did not contain lines.");
                var receiptLines = lines.EnumerateArray().Select(line => new
                {
                    lineId = line.GetProperty("id").GetGuid(),
                    quantityReceived = line.GetProperty("quantityDispatched").GetDecimal(),
                    discrepancyNote = (string?)null
                }).ToList();
                await _api.PostJsonAsync($"api/stock-transfers/{id}/receive", new { lines = receiptLines });
            }
            StatusText.Text = "Transfer action completed.";
            await LoadWorkspaceAsync("Stock transfers");
        }
        catch (Exception ex) { StatusText.Text = ex.Message; }
    }

    private string? Prompt(string title, string message)
    {
        var dialog = new Window
        {
            Title = title,
            Owner = this,
            Width = 460,
            Height = 190,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize
        };
        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock { Text = message, Margin = new Thickness(0, 0, 0, 8) });
        var input = new TextBox { MinWidth = 400, Margin = new Thickness(0, 0, 0, 12) };
        panel.Children.Add(input);
        var submit = new Button { Content = "Continue", Width = 100, HorizontalAlignment = HorizontalAlignment.Right, IsDefault = true };
        submit.Click += (_, _) => dialog.DialogResult = true;
        panel.Children.Add(submit);
        dialog.Content = panel;
        input.Focus();
        return dialog.ShowDialog() == true ? input.Text.Trim() : null;
    }

    private async Task DownloadInventoryCsvAsync()
    {
        try
        {
            var bytes = await _api.GetBytesAsync("api/reports/inventory.csv");
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                FileName = "iwms-inventory.csv",
                Filter = "CSV files (*.csv)|*.csv"
            };
            if (dialog.ShowDialog(this) == true)
            {
                await System.IO.File.WriteAllBytesAsync(dialog.FileName, bytes);
                StatusText.Text = "Inventory CSV saved.";
            }
        }
        catch (Exception ex) { StatusText.Text = ex.Message; }
    }

    private static void AddMetric(Panel panel, string label, string value)
    {
        var card = new Border
        {
            Background = System.Windows.Media.Brushes.White,
            BorderBrush = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(224, 229, 234)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Margin = new Thickness(0, 0, 14, 14),
            Padding = new Thickness(18)
        };
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = label, Foreground = System.Windows.Media.Brushes.Gray, FontSize = 13 });
        stack.Children.Add(new TextBlock
        {
            Text = value,
            Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(23, 33, 43)),
            FontSize = 25,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 12, 0, 0)
        });
        card.Child = stack;
        panel.Children.Add(card);
    }
}
