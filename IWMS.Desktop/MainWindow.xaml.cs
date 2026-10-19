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
        PageSubtitle.Text = page switch
        {
            "Customers" => "Customer records and account details",
            "Sales orders" => "Order entry and fulfillment status",
            "Fulfillment" => "Reservations, pick lists and dispatch",
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
            PageContent.Content = reportPanel;
            return;
        }

        var endpoint = page switch
        {
            "Customers" => "api/customers",
            "Sales orders" => "api/sales-orders",
            "Inventory" => "api/products",
            "Purchasing" => "api/purchase-orders",
            "Fulfillment" => "api/sales-orders",
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
            if (page is "Customers" or "Sales orders")
            {
                var workspace = new DockPanel();
                var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
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
                    if (created == true)
                        await LoadWorkspaceAsync(page);
                };
                toolbar.Children.Add(addButton);
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
