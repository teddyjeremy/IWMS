using System.Globalization;
using System.Windows;
using System.Windows.Controls;
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

        PageContent.Content = BuildSectionPage(page);
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
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
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
        stack.Children.Add(new TextBlock { Text = value, Foreground = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(23, 33, 43)), FontSize = 25, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 0) });
        card.Child = stack;
        panel.Children.Add(card);
    }

    private UIElement BuildSectionPage(string page)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = $"{page} workspace",
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 10)
        });
        stack.Children.Add(new TextBlock
        {
            Text = _api.IsSignedIn
                ? "This workspace is being connected to the existing IWMS API. Use the API endpoints documented in the repository README while this screen is completed."
                : "Sign in to the IWMS API to access operational data.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = System.Windows.Media.Brushes.Gray,
            FontSize = 14
        });
        if (page == "Reports")
        {
            var button = new Button { Content = "Download inventory CSV", Width = 210, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 18, 0, 0) };
            button.Click += async (_, _) =>
            {
                try
                {
                    var bytes = await _api.GetBytesAsync("api/reports/inventory.csv");
                    var dialog = new Microsoft.Win32.SaveFileDialog { FileName = "iwms-inventory.csv", Filter = "CSV files (*.csv)|*.csv" };
                    if (dialog.ShowDialog(this) == true)
                        await System.IO.File.WriteAllBytesAsync(dialog.FileName, bytes);
                }
                catch (Exception ex) { StatusText.Text = ex.Message; }
            };
            stack.Children.Add(button);
        }
        return stack;
    }
}
