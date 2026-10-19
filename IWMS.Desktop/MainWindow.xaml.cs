using System.Data;
using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using IWMS.Desktop.Controls;
using IWMS.Desktop.Services;

namespace IWMS.Desktop;

public partial class MainWindow : Window
{
    private readonly ApiClient _api = new();
    private bool _showShipments;
    private string _currentPage = "Dashboard";
    private int _reportDays = 30;
    private string _listSearch = "";

    public MainWindow()
    {
        InitializeComponent();
        _api.SetBaseUrl(Environment.GetEnvironmentVariable("IWMS_API_BASE_URL") ?? _api.BaseUrl);
        ShellRoot.Visibility = Visibility.Collapsed;
        LoginRoot.Visibility = Visibility.Visible;
        ShowDashboard();
        Loaded += (_, _) => LoginEmailBox.Focus();
    }

    private async void SignIn_Click(object sender, RoutedEventArgs e)
    {
        LoginErrorText.Text = string.Empty;
        LoginButton.IsEnabled = false;
        LoginButton.Content = "Signing in...";
        try
        {
            await _api.SignInAsync(LoginEmailBox.Text.Trim(), LoginPasswordBox.Password);
            LoginPasswordBox.Clear();
            ConnectionLabel.Text = $"Signed in as {_api.CurrentUserName}";
            ApplyRoleBasedWorkspace();
            _currentPage = "Dashboard";
            HighlightNav("Dashboard");
            LoginRoot.Visibility = Visibility.Collapsed;
            ShellRoot.Visibility = Visibility.Visible;
            StatusText.Text = "Securely connected to the IWMS API.";
            await LoadDashboardAsync();
        }
        catch (Exception ex)
        {
            _api.SignOut();
            LoginErrorText.Text = ex.Message;
        }
        finally
        {
            LoginButton.IsEnabled = true;
            LoginButton.Content = "Sign in securely";
        }
    }

    private void SignOut_Click(object sender, RoutedEventArgs e)
    {
        _api.SignOut();
        RoleLabel.Text = "Workspace";
        LoginPasswordBox.Clear();
        LoginErrorText.Text = string.Empty;
        ShellRoot.Visibility = Visibility.Collapsed;
        LoginRoot.Visibility = Visibility.Visible;
        StatusText.Text = "Sign in to connect to the IWMS API.";
        LoginEmailBox.Focus();
    }

    private async void Navigate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string page ||
            button.Visibility != Visibility.Visible) return;
        if (page == "Fulfillment") _showShipments = false;
        if (page != _currentPage) _listSearch = "";
        await OpenPageAsync(page);
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (!_api.IsSignedIn) return;
        if (_currentPage == "Dashboard") await LoadDashboardAsync();
        else if (_currentPage == "Reports") await ShowReportsAsync();
        else if (_currentPage == "Alerts") await ShowAlertsAsync();
        else if (_currentPage == "Warehouses") await ShowWarehousesAsync();
        else await LoadWorkspaceAsync(_currentPage);
    }

    private async Task OpenPageAsync(string page)
    {
        _currentPage = page;
        PageTitle.Text = page switch
        {
            "Reports" => "Reports & exports",
            "Alerts" => "Alerts",
            "Warehouses" => "Warehouses and bins",
            "Stock transfers" => "Stock transfers",
            _ => page
        };
        PageSubtitle.Text = page switch
        {
            "Customers" => "Customer records and account details",
            "Sales orders" => "Order entry, confirmation and draft editing",
            "Fulfillment" => "Reservations, pick lists and dispatch",
            "Stock transfers" => "Warehouse-to-warehouse transfer documents",
            "Stock counts" => "Physical counts, variances and stock adjustments",
            "Returns" => "Customer and supplier return records",
            "Inventory" => "Catalogue, on-hand stock and adjustments",
            "Purchasing" => "Draft, approve and receive purchase orders",
            "Suppliers" => "Supplier records used by purchasing",
            "Receipts" => "Goods receipts posted against purchase orders",
            "Movements" => "Immutable ledger of every stock change",
            "Audit" => "Who changed what, and why",
            "Reports" => "Movement charts, stock value, dead stock, ABC and supplier fill rate",
            "Alerts" => "Low stock, purchase orders waiting for approval, and overdue deliveries",
            "Warehouses" => "Create warehouses and storage bins",
            "Users" => "Manage user accounts, roles and access",
            _ => "Inventory position, workload and movement trend"
        };
        HighlightNav(page);
        if (page == "Dashboard") await LoadDashboardAsync();
        else if (page == "Reports") await ShowReportsAsync();
        else if (page == "Alerts") await ShowAlertsAsync();
        else if (page == "Warehouses") await ShowWarehousesAsync();
        else await LoadWorkspaceAsync(page);
    }


    private void ApplyRoleBasedWorkspace()
    {
        var admin = _api.IsInRole("Administrator");
        var manager = _api.IsInRole("Warehouse Manager");
        var purchaser = _api.IsInRole("Purchaser");
        var operatorRole = _api.IsInRole("Warehouse Operator");
        var sales = _api.IsInRole("Sales Officer");
        var viewer = _api.IsInRole("Viewer") || _api.IsInRole("Auditor") || _api.IsInRole("Viewer/Auditor");

        DashboardNavButton.Visibility = Visibility.Visible;
        AlertsNavButton.Visibility = Visibility.Visible;
        WarehousesNavButton.Visibility = admin || manager || operatorRole || purchaser ? Visibility.Visible : Visibility.Collapsed;
        CustomersNavButton.Visibility = admin || sales ? Visibility.Visible : Visibility.Collapsed;
        SalesOrdersNavButton.Visibility = admin || sales || manager || operatorRole ? Visibility.Visible : Visibility.Collapsed;
        SuppliersNavButton.Visibility = admin || purchaser || manager ? Visibility.Visible : Visibility.Collapsed;
        PurchasingNavButton.Visibility = admin || manager || purchaser || operatorRole ? Visibility.Visible : Visibility.Collapsed;
        FulfillmentNavButton.Visibility = admin || manager || operatorRole ? Visibility.Visible : Visibility.Collapsed;
        TransfersNavButton.Visibility = admin || manager || operatorRole ? Visibility.Visible : Visibility.Collapsed;
        CountsNavButton.Visibility = admin || manager || operatorRole ? Visibility.Visible : Visibility.Collapsed;
        ReturnsNavButton.Visibility = admin || manager || operatorRole ? Visibility.Visible : Visibility.Collapsed;
        ReceiptsNavButton.Visibility = admin || manager || operatorRole || purchaser ? Visibility.Visible : Visibility.Collapsed;
        MovementsNavButton.Visibility = Visibility.Visible;
        InventoryNavButton.Visibility = Visibility.Visible;
        ReportsNavButton.Visibility = admin || manager || purchaser || viewer || sales ? Visibility.Visible : Visibility.Collapsed;
        AuditNavButton.Visibility = admin || manager || viewer ? Visibility.Visible : Visibility.Collapsed;
        UsersNavButton.Visibility = admin ? Visibility.Visible : Visibility.Collapsed;
        CommercialHeading.Visibility = AnyVisible(CustomersNavButton, SalesOrdersNavButton, SuppliersNavButton, PurchasingNavButton);
        WarehouseHeading.Visibility = Visibility.Visible;
        AdminHeading.Visibility = AnyVisible(AuditNavButton, UsersNavButton);

        RoleLabel.Text = admin ? "Administrator workspace"
            : manager ? "Warehouse manager workspace"
            : purchaser ? "Purchasing workspace"
            : operatorRole ? "Warehouse operations workspace"
            : sales ? "Sales workspace"
            : viewer ? "Read-only / audit workspace"
            : "Standard workspace";
    }

    private void ShowDashboard()
    {
        PageContent.Content = new Border
        {
            Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#F5F8FC")!,
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(24),
            Child = new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = "Operations overview", FontSize = 25, FontWeight = FontWeights.Bold, Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#142B45")! },
                    new TextBlock { Text = "Sign in to view live inventory, orders, purchasing and warehouse activity.", FontSize = 14, Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#63758A")!, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap }
                }
            }
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
            var admin = _api.IsInRole("Administrator");
            var manager = _api.IsInRole("Warehouse Manager");
            var purchaser = _api.IsInRole("Purchaser");
            var operatorRole = _api.IsInRole("Warehouse Operator");
            var sales = _api.IsInRole("Sales Officer");
            var viewer = _api.IsInRole("Viewer") || _api.IsInRole("Auditor") || _api.IsInRole("Viewer/Auditor");

            var shell = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
            shell.Children.Add(new TextBlock
            {
                Text = "Good " + (DateTime.Now.Hour < 12 ? "morning" : DateTime.Now.Hour < 18 ? "afternoon" : "evening") + ", " + (_api.CurrentUserName ?? "team"),
                FontSize = 27, FontWeight = FontWeights.Bold,
                Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#142B45")!,
                Margin = new Thickness(0, 0, 0, 4)
            });
            shell.Children.Add(new TextBlock
            {
                Text = string.Join(" · ", _api.CurrentUserRoles),
                FontSize = 13, Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#64748B")!,
                Margin = new Thickness(0, 0, 0, 18)
            });

            var metrics = new List<(string Label, string Value, string Color)>
            {
                ("Units on hand", data.UnitsOnHand.ToString("N2", CultureInfo.InvariantCulture), "#174A68"),
                ("Available", (data.UnitsOnHand - data.UnitsReserved).ToString("N2", CultureInfo.InvariantCulture), "#0F766E")
            };
            if (admin || manager || purchaser || viewer) metrics.Add(("Stock value", data.StockValue.ToString("N2", CultureInfo.InvariantCulture), "#2563EB"));
            if (admin || manager || purchaser || operatorRole || viewer) metrics.Add(("Low-stock products", data.LowStockProducts.ToString("N0"), "#DC5A45"));
            if (admin || manager || purchaser) metrics.Add(("Open purchase orders", data.OpenPurchaseOrders.ToString("N0"), "#0D9488"));
            if (admin || manager || purchaser) metrics.Add(("Pending approvals", data.PendingApprovals.ToString("N0"), "#7C3AED"));
            if (admin || manager || operatorRole || sales || viewer) metrics.Add(("Open sales orders", data.OpenSalesOrders.ToString("N0"), "#D97706"));
            if (admin || manager || operatorRole || sales || viewer) metrics.Add(("Units reserved", data.UnitsReserved.ToString("N2", CultureInfo.InvariantCulture), "#475569"));

            var cards = new UniformGrid { Columns = 3, Margin = new Thickness(0, 0, 0, 18) };
            foreach (var metric in metrics)
            {
                var body = new StackPanel();
                body.Children.Add(new TextBlock { Text = metric.Label.ToUpperInvariant(), FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#64748B")!, TextWrapping = TextWrapping.Wrap });
                body.Children.Add(new TextBlock { Text = metric.Value, FontSize = 25, FontWeight = FontWeights.Bold, Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString(metric.Color)!, Margin = new Thickness(0, 8, 0, 0) });
                cards.Children.Add(new Border { Background = System.Windows.Media.Brushes.White, BorderBrush = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#E2E8F0")!, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(16), Margin = new Thickness(0, 0, 10, 10), Child = body });
            }
            shell.Children.Add(cards);
            shell.Children.Add(await BuildTrendCardAsync(30, "Movement trend · last 30 days", "Teal bars are receipts. Orange bars are issues. Values come from the inventory ledger."));
            shell.Children.Add(await BuildAlertsPanelAsync());

            var contentGrid = new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, 0, 18) };
            var workload = new StackPanel();
            workload.Children.Add(new TextBlock { Text = "CURRENT WORKLOAD", FontSize = 12, FontWeight = FontWeights.Bold, Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#142B45")!, Margin = new Thickness(0, 0, 0, 12) });
            var bars = new List<(string Name, double Value, string Color)>();
            if (admin || manager || purchaser) bars.Add(("Open purchase orders", data.OpenPurchaseOrders, "#0D9488"));
            if (admin || manager || purchaser) bars.Add(("Pending approvals", data.PendingApprovals, "#7C3AED"));
            if (admin || manager || operatorRole || sales || viewer) bars.Add(("Open sales orders", data.OpenSalesOrders, "#2563EB"));
            if (admin || manager || purchaser || operatorRole || viewer) bars.Add(("Low-stock products", data.LowStockProducts, "#DC5A45"));
            var max = Math.Max(1, bars.Select(b => b.Value).DefaultIfEmpty(1).Max());
            foreach (var bar in bars)
            {
                workload.Children.Add(new TextBlock { Text = bar.Name + "   " + bar.Value.ToString("N0"), FontSize = 12, Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#334155")!, Margin = new Thickness(0, 6, 0, 4) });
                workload.Children.Add(new ProgressBar { Minimum = 0, Maximum = max, Value = bar.Value, Height = 9, Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString(bar.Color)!, Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#E8EEF5")!, BorderThickness = new Thickness(0) });
            }
            contentGrid.Children.Add(new Border { Background = System.Windows.Media.Brushes.White, BorderBrush = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#E2E8F0")!, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(18), Margin = new Thickness(0, 0, 10, 0), Child = workload });

            var actions = new StackPanel();
            actions.Children.Add(new TextBlock { Text = "QUICK ACTIONS", FontSize = 12, FontWeight = FontWeights.Bold, Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#142B45")!, Margin = new Thickness(0, 0, 0, 12) });
            void AddAction(string label, string page, bool allowed)
            {
                if (!allowed) return;
                var button = new Button { Content = label, Tag = page, HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(12, 10, 12, 10), Margin = new Thickness(0, 0, 0, 8), Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#F1F6FC")!, Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#1D4268")!, BorderBrush = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#D9E5F2")! };
                button.Click += async (_, _) => await OpenPageAsync(page);
                actions.Children.Add(button);
            }
            AddAction("View inventory", "Inventory", true);
            AddAction("Sales orders", "Sales orders", admin || manager || operatorRole || sales);
            AddAction("Purchase orders", "Purchasing", admin || manager || purchaser);
            AddAction("Transfers & warehouse work", "Stock transfers", admin || manager || operatorRole);
            AddAction("Reports & exports", "Reports", admin || manager || purchaser || viewer);
            AddAction("Users & roles", "Users", admin);
            contentGrid.Children.Add(new Border { Background = System.Windows.Media.Brushes.White, BorderBrush = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#E2E8F0")!, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(18), Margin = new Thickness(10, 0, 0, 0), Child = actions });
            shell.Children.Add(contentGrid);

            shell.Children.Add(new TextBlock { Text = "Operational data", FontSize = 16, FontWeight = FontWeights.Bold, Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#142B45")!, Margin = new Thickness(0, 0, 0, 8) });
            shell.Children.Add(new TextBlock { Text = "Metrics and workload bars reflect the current API response. Use the workspace pages for editable records, approvals, receiving, transfers and user administration.", FontSize = 12, Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#64748B")!, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });

            PageContent.Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = shell };
            StatusText.Text = "Updated " + DateTime.Now.ToString("HH:mm:ss") + " · " + string.Join(", ", _api.CurrentUserRoles);
        }
        catch (Exception ex)
        {
            PageContent.Content = new Border { Background = System.Windows.Media.Brushes.White, CornerRadius = new CornerRadius(10), Padding = new Thickness(20), Child = new TextBlock { Text = "Dashboard could not be loaded. " + ex.Message, Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#B42318")!, TextWrapping = TextWrapping.Wrap } };
        }
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

        if (page == "Users" && !_api.IsInRole("Administrator"))
        {
            StatusText.Text = "Administrator access is required to manage users.";
            return;
        }

        if (page == "Reports")
        {
            await ShowReportsAsync();
            return;
        }

        var endpoint = page switch
        {
            "Customers" => "api/customers",
            "Sales orders" => "api/sales-orders",
            "Inventory" => "api/products",
            "Purchasing" => "api/purchase-orders",
            "Suppliers" => "api/suppliers?includeInactive=true",
            "Fulfillment" => _showShipments ? "api/fulfillment/shipments" : "api/sales-orders",
            "Stock transfers" => "api/stock-transfers",
            "Stock counts" => "api/stock-counts",
            "Returns" => "api/returns",
            "Receipts" => "api/receipts",
            "Movements" => "api/movements",
            "Audit" => "api/audit-events",
            "Users" => "api/users",
            _ => string.Empty
        };
        endpoint = WithSearch(page, endpoint);

        if (string.IsNullOrEmpty(endpoint))
        {
            PageContent.Content = new TextBlock { Text = "This workspace is not configured yet." };
            return;
        }

        try
        {
            StatusText.Text = $"Loading {page.ToLowerInvariant()}…";
            PageContent.Content = new TextBlock
            {
                Text = $"Loading {page.ToLowerInvariant()}…",
                FontSize = 15,
                Foreground = System.Windows.Media.Brushes.Gray,
                Margin = new Thickness(0, 8, 0, 0)
            };
            var json = await _api.GetListJsonAsync(endpoint);
            var dataGrid = CreateDataGrid(json);
            if (page is "Customers" or "Sales orders" or "Inventory" or "Fulfillment" or "Stock transfers" or "Stock counts" or "Returns" or "Users" or "Purchasing" or "Suppliers" or "Receipts" or "Movements" or "Audit")
            {
                var workspace = new DockPanel();
                var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
                AddSearch(toolbar, page);
                if (page is "Customers" or "Sales orders")
                {
                    var canManageMasterData = _api.IsInRole("Administrator") || _api.IsInRole("Warehouse Manager");
                    var canManageSalesOrders = _api.IsInRole("Administrator") || _api.IsInRole("Warehouse Manager") || _api.IsInRole("Sales Officer");
                    var addButton = new Button
                    {
                        Content = page == "Customers" ? "New customer" : "New sales order",
                        Background = System.Windows.Media.Brushes.SteelBlue,
                        Foreground = System.Windows.Media.Brushes.White,
                        Visibility = page == "Customers"
                            ? (canManageMasterData ? Visibility.Visible : Visibility.Collapsed)
                            : (canManageSalesOrders ? Visibility.Visible : Visibility.Collapsed)
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
                    if (page == "Sales orders" && canManageSalesOrders)
                    {
                        var editOrderButton = new Button { Content = "Edit selected draft", Margin = new Thickness(8, 0, 0, 0) };
                        editOrderButton.Click += async (_, _) =>
                        {
                            var orderId = GetSelectedGuid(dataGrid);
                            if (orderId is null)
                            {
                                StatusText.Text = "Select a sales order first.";
                                return;
                            }
                            try
                            {
                                var detail = await _api.GetJsonAsync($"api/sales-orders/{orderId.Value}");
                                if (!detail.TryGetProperty("status", out var status) || !string.Equals(status.GetString(), "Draft", StringComparison.OrdinalIgnoreCase))
                                {
                                    StatusText.Text = "Only draft sales orders can be edited.";
                                    return;
                                }
                                var dialog = new NewSalesOrderWindow(_api, orderId.Value) { Owner = this };
                                if (dialog.ShowDialog() == true)
                                {
                                    StatusText.Text = "Draft sales order updated.";
                                    await LoadWorkspaceAsync("Sales orders");
                                }
                            }
                            catch (Exception ex) { StatusText.Text = ex.Message; }
                        };
                        toolbar.Children.Add(editOrderButton);
                    }
                    if (page == "Customers" && canManageMasterData)
                    {
                        var editCustomerButton = new Button { Content = "Edit selected" };
                        editCustomerButton.Click += async (_, _) =>
                        {
                            var customerId = GetSelectedGuid(dataGrid);
                            if (customerId is null)
                            {
                                StatusText.Text = "Select a customer first.";
                                return;
                            }
                            var dialog = new NewCustomerWindow(_api, customerId.Value) { Owner = this };
                            if (dialog.ShowDialog() == true) await LoadWorkspaceAsync("Customers");
                        };
                        var deactivateCustomerButton = new Button { Content = "Deactivate selected" };
                        deactivateCustomerButton.Click += async (_, _) =>
                        {
                            var customerId = GetSelectedGuid(dataGrid);
                            if (customerId is null)
                            {
                                StatusText.Text = "Select a customer first.";
                                return;
                            }
                            if (MessageBox.Show("Deactivate this customer? Existing order history will be retained.",
                                "Deactivate customer", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                                return;
                            try
                            {
                                await _api.PostJsonAsync($"api/customers/{customerId.Value}/deactivate", new { });
                                StatusText.Text = "Customer deactivated.";
                                await LoadWorkspaceAsync("Customers");
                            }
                            catch (Exception ex) { StatusText.Text = ex.Message; }
                        };
                        toolbar.Children.Add(editCustomerButton);
                        toolbar.Children.Add(deactivateCustomerButton);
                    }
                    if (page == "Sales orders")
                    {
                        var confirmButton = new Button { Content = "Confirm selected", Visibility = canManageSalesOrders ? Visibility.Visible : Visibility.Collapsed };
                        confirmButton.Click += async (_, _) => await RunSelectedFulfillmentActionAsync(dataGrid, "confirm-order", "Sales orders");
                        var cancelOrderButton = new Button { Content = "Cancel selected", Visibility = canManageSalesOrders ? Visibility.Visible : Visibility.Collapsed };
                        cancelOrderButton.Click += async (_, _) => await RunSelectedFulfillmentActionAsync(dataGrid, "cancel-draft", "Sales orders");
                        toolbar.Children.Add(confirmButton);
                        toolbar.Children.Add(cancelOrderButton);
                        var deliveryNote = ActionButton("Delivery note PDF");
                        deliveryNote.Click += async (_, _) => await DownloadSelectedPdfAsync(dataGrid, "api/sales-orders/{id}/delivery-note", "delivery-note.pdf");
                        toolbar.Children.Add(deliveryNote);
                    }
                }
                else if (page == "Inventory")
                {
                    var canManageCatalogue = _api.IsInRole("Administrator") || _api.IsInRole("Warehouse Manager");
                    var canIssue = canManageCatalogue || _api.IsInRole("Warehouse Operator");
                    if (canManageCatalogue)
                    {
                        var newProductButton = ActionButton("New product", primary: true);
                        newProductButton.Click += async (_, _) =>
                        {
                            if (new EditProductWindow(_api) { Owner = this }.ShowDialog() == true)
                            {
                                StatusText.Text = "Product created.";
                                await LoadWorkspaceAsync("Inventory");
                            }
                        };
                        var editProductButton = ActionButton("Edit selected product");
                        editProductButton.Click += async (_, _) =>
                        {
                            var productId = GetSelectedGuid(dataGrid);
                            if (productId is null)
                            {
                                StatusText.Text = "Select a product first.";
                                return;
                            }
                            var dialog = new EditProductWindow(_api, productId.Value) { Owner = this };
                            if (dialog.ShowDialog() == true)
                            {
                                StatusText.Text = "Product details saved.";
                                await LoadWorkspaceAsync("Inventory");
                            }
                        };
                        toolbar.Children.Add(newProductButton);
                        toolbar.Children.Add(editProductButton);
                    }
                    if (canIssue)
                    {
                        var issueButton = ActionButton("Issue stock");
                        issueButton.Click += async (_, _) =>
                        {
                            if (new StockActionWindow(_api, adjust: false) { Owner = this }.ShowDialog() == true)
                            {
                                StatusText.Text = "Stock issue posted.";
                                await LoadWorkspaceAsync("Inventory");
                            }
                        };
                        toolbar.Children.Add(issueButton);
                    }
                    if (canManageCatalogue)
                    {
                        var adjustButton = ActionButton("Adjust stock");
                        adjustButton.Click += async (_, _) =>
                        {
                            if (new StockActionWindow(_api, adjust: true) { Owner = this }.ShowDialog() == true)
                            {
                                StatusText.Text = "Stock adjustment posted.";
                                await LoadWorkspaceAsync("Inventory");
                            }
                        };
                        toolbar.Children.Add(adjustButton);
                    }
                    var movementsButton = ActionButton("Open movements");
                    movementsButton.Click += async (_, _) => await OpenPageAsync("Movements");
                    var scanBox = new TextBox { Width = 180, Margin = new Thickness(0, 0, 8, 8), ToolTip = "USB scanner input lands here. Press Find." };
                    var findBarcode = ActionButton("Find barcode");
                    findBarcode.Click += async (_, _) =>
                    {
                        if (string.IsNullOrWhiteSpace(scanBox.Text)) { StatusText.Text = "Scan or type a barcode first."; return; }
                        try
                        {
                            var product = await _api.GetJsonAsync("api/products/lookup?barcode=" + Uri.EscapeDataString(scanBox.Text.Trim()));
                            StatusText.Text = $"Barcode matches {product.GetProperty("sku").GetString()} — {product.GetProperty("name").GetString()}.";
                            if (canManageCatalogue && Guid.TryParse(product.GetProperty("id").GetString(), out var foundId))
                            {
                                if (new EditProductWindow(_api, foundId) { Owner = this }.ShowDialog() == true)
                                    await LoadWorkspaceAsync("Inventory");
                            }
                        }
                        catch (Exception ex) { StatusText.Text = ex.Message; }
                    };
                    toolbar.Children.Add(movementsButton);
                    toolbar.Children.Add(scanBox);
                    toolbar.Children.Add(findBarcode);
                    if (canManageCatalogue)
                    {
                        var importButton = ActionButton("Import CSV");
                        importButton.Click += async (_, _) =>
                        {
                            if (new CsvImportWindow(_api) { Owner = this }.ShowDialog() == true)
                            {
                                StatusText.Text = "Import finished. Review the message in the import window if any rows were skipped.";
                                await LoadWorkspaceAsync("Inventory");
                            }
                        };
                        toolbar.Children.Add(importButton);
                    }
                }
                else if (page == "Users")
                {
                    var createUserButton = new Button { Content = "Create user" };
                    createUserButton.Click += async (_, _) => await CreateUserFromDesktopAsync();
                    var resetPasswordButton = new Button { Content = "Reset password" };
                    resetPasswordButton.Click += async (_, _) => await ResetSelectedUserPasswordAsync(dataGrid);
                    var rolesButton = new Button { Content = "Change role" };
                    rolesButton.Click += async (_, _) => await ChangeSelectedUserRoleAsync(dataGrid);
                    var statusButton = new Button { Content = "Activate / deactivate" };
                    statusButton.Click += async (_, _) => await ToggleSelectedUserStatusAsync(dataGrid);
                    toolbar.Children.Add(createUserButton);
                    toolbar.Children.Add(resetPasswordButton);
                    toolbar.Children.Add(rolesButton);
                    toolbar.Children.Add(statusButton);
                }
                else if (page == "Stock transfers")
                {
                    var createTransferButton = new Button { Content = "New transfer request" };
                    createTransferButton.Click += async (_, _) =>
                    {
                        if (new TransferRequestWindow(_api) { Owner = this }.ShowDialog() == true)
                        {
                            StatusText.Text = "Transfer request submitted.";
                            await LoadWorkspaceAsync("Stock transfers");
                        }
                    };
                    toolbar.Children.Add(createTransferButton);
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
                    var recordCountButton = new Button { Content = "Blind count / review" };
                    recordCountButton.Click += async (_, _) =>
                    {
                        var id = GetSelectedGuid(dataGrid);
                        if (id is null) { StatusText.Text = "Select a stock count first."; return; }
                        if (new BlindCountWindow(_api, id.Value) { Owner = this }.ShowDialog() == true)
                        {
                            StatusText.Text = "Stock count updated.";
                            await LoadWorkspaceAsync("Stock counts");
                        }
                    };
                    var postCountButton = new Button { Content = "Post selected count" };
                    postCountButton.Click += async (_, _) => await PostSelectedStockCountAsync(dataGrid);
                    toolbar.Children.Add(recordCountButton);
                    toolbar.Children.Add(postCountButton);
                    var startCountButton = new Button { Content = "Start count" };
                    startCountButton.Click += async (_, _) =>
                    {
                        try
                        {
                            var warehouses = await _api.GetJsonAsync("api/warehouses");
                            var dialog = new Window { Title = "Start stock count", Width = 460, Height = 220, Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner };
                            var panel = new StackPanel { Margin = new Thickness(16) };
                            panel.Children.Add(new TextBlock { Text = "Warehouse", Margin = new Thickness(0, 0, 0, 6) });
                            var combo = new ComboBox { DisplayMemberPath = "Label" };
                            foreach (var warehouse in warehouses.EnumerateArray())
                                combo.Items.Add(new CountWarehouseChoice(warehouse.GetProperty("id").GetString()!, $"{warehouse.GetProperty("code").GetString()} — {warehouse.GetProperty("name").GetString()}"));
                            if (combo.Items.Count > 0) combo.SelectedIndex = 0;
                            panel.Children.Add(combo);
                            var go = new Button { Content = "Start blind count", Margin = new Thickness(0, 12, 0, 0), HorizontalAlignment = HorizontalAlignment.Right };
                            go.Click += (_, _) => dialog.DialogResult = true;
                            panel.Children.Add(go);
                            dialog.Content = panel;
                            if (dialog.ShowDialog() != true || combo.SelectedItem is not CountWarehouseChoice selected || !Guid.TryParse(selected.Id, out var warehouseId))
                                return;
                            await _api.PostJsonAsync("api/stock-counts", new { warehouseId, notes = "Blind count started from desktop" });
                            await LoadWorkspaceAsync("Stock counts");
                            StatusText.Text = "Blind stock count started. Book quantities stay hidden until review.";
                        }
                        catch (Exception ex) { StatusText.Text = ex.Message; }
                    };
                    toolbar.Children.Add(startCountButton);
                }
                else if (page == "Returns")
                {
                    var customerReturnButton = new Button { Content = "New customer return" };
                    customerReturnButton.Click += async (_, _) =>
                    {
                        if (new ReturnRequestWindow(_api, customer: true) { Owner = this }.ShowDialog() == true)
                            await LoadWorkspaceAsync("Returns");
                    };
                    var supplierReturnButton = new Button { Content = "New supplier return" };
                    supplierReturnButton.Click += async (_, _) =>
                    {
                        if (new ReturnRequestWindow(_api, customer: false) { Owner = this }.ShowDialog() == true)
                            await LoadWorkspaceAsync("Returns");
                    };
                    toolbar.Children.Add(customerReturnButton);
                    toolbar.Children.Add(supplierReturnButton);
                }
                else if (page == "Purchasing")
                {
                    var canManagePo = _api.IsInRole("Administrator") || _api.IsInRole("Purchaser");
                    var canApprovePo = _api.IsInRole("Administrator") || _api.IsInRole("Warehouse Manager");
                    var canCancelPo = canManagePo || _api.IsInRole("Warehouse Manager");
                    var canReceive = _api.IsInRole("Administrator") || _api.IsInRole("Warehouse Manager") || _api.IsInRole("Warehouse Operator");
                    if (canManagePo)
                    {
                        var newPo = ActionButton("New purchase order", primary: true);
                        newPo.Click += async (_, _) =>
                        {
                            if (new PurchaseOrderWindow(_api) { Owner = this }.ShowDialog() == true)
                            {
                                StatusText.Text = "Draft purchase order created.";
                                await LoadWorkspaceAsync("Purchasing");
                            }
                        };
                        var submitPo = ActionButton("Submit selected");
                        submitPo.Click += async (_, _) => await RunPurchaseOrderActionAsync(dataGrid, "submit");
                        toolbar.Children.Add(newPo);
                        toolbar.Children.Add(submitPo);
                    }
                    if (canApprovePo)
                    {
                        var approvePo = ActionButton("Approve selected");
                        approvePo.Click += async (_, _) => await RunPurchaseOrderActionAsync(dataGrid, "approve");
                        var rejectPo = ActionButton("Reject selected");
                        rejectPo.Click += async (_, _) => await RunPurchaseOrderActionAsync(dataGrid, "reject");
                        toolbar.Children.Add(approvePo);
                        toolbar.Children.Add(rejectPo);
                    }
                    if (canCancelPo)
                    {
                        var cancelPo = ActionButton("Cancel selected");
                        cancelPo.Click += async (_, _) => await RunPurchaseOrderActionAsync(dataGrid, "cancel");
                        toolbar.Children.Add(cancelPo);
                    }
                    if (canReceive)
                    {
                        var receive = ActionButton("Receive goods", primary: true);
                        receive.Click += async (_, _) =>
                        {
                            var id = GetSelectedGuid(dataGrid);
                            if (id is null) { StatusText.Text = "Select a purchase order first."; return; }
                            if (new ReceiveGoodsWindow(_api, id.Value) { Owner = this }.ShowDialog() == true)
                            {
                                StatusText.Text = "Goods receipt posted.";
                                await LoadWorkspaceAsync("Purchasing");
                            }
                        };
                        toolbar.Children.Add(receive);
                    }
                    var poPdf = ActionButton("PO document");
                    poPdf.Click += async (_, _) => await DownloadSelectedPdfAsync(dataGrid, "api/purchase-orders/{id}/pdf", "purchase-order.pdf");
                    toolbar.Children.Add(poPdf);
                }
                else if (page == "Suppliers")
                {
                    if (_api.IsInRole("Administrator") || _api.IsInRole("Purchaser"))
                    {
                        var addSupplier = ActionButton("New supplier", primary: true);
                        addSupplier.Click += async (_, _) =>
                        {
                            if (new SupplierWindow(_api) { Owner = this }.ShowDialog() == true)
                            {
                                StatusText.Text = "Supplier created.";
                                await LoadWorkspaceAsync("Suppliers");
                            }
                        };
                        toolbar.Children.Add(addSupplier);
                    }
                }
                else if (page == "Movements")
                {
                    if (_api.IsInRole("Administrator") || _api.IsInRole("Warehouse Manager"))
                    {
                        var reverse = ActionButton("Reverse selected");
                        reverse.Click += async (_, _) =>
                        {
                            var id = GetSelectedGuid(dataGrid);
                            if (id is null) { StatusText.Text = "Select a movement first."; return; }
                            var reason = Prompt("Reverse movement", "Reason for the reversing entry:");
                            if (string.IsNullOrWhiteSpace(reason)) return;
                            try
                            {
                                await _api.PostJsonAsync($"api/movements/{id}/reverse", new { reason });
                                StatusText.Text = "Reversing movement posted.";
                                await LoadWorkspaceAsync("Movements");
                            }
                            catch (Exception ex) { StatusText.Text = ex.Message; }
                        };
                        toolbar.Children.Add(reverse);
                    }
                }
                else if (page == "Receipts")
                {
                    if (_api.IsInRole("Administrator") || _api.IsInRole("Warehouse Manager"))
                    {
                        var reverseReceipt = ActionButton("Reverse selected receipt");
                        reverseReceipt.Click += async (_, _) =>
                        {
                            var id = GetSelectedGuid(dataGrid);
                            if (id is null) { StatusText.Text = "Select a receipt first."; return; }
                            var reason = Prompt("Reverse receipt", "Reason for reversing this receipt:");
                            if (string.IsNullOrWhiteSpace(reason)) return;
                            try
                            {
                                await _api.PostJsonAsync($"api/receipts/{id}/reverse", new { reason });
                                StatusText.Text = "Receipt reversed.";
                                await LoadWorkspaceAsync("Receipts");
                            }
                            catch (Exception ex) { StatusText.Text = ex.Message; }
                        };
                        toolbar.Children.Add(reverseReceipt);
                    }
                }
                else if (page == "Audit")
                {
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
                if (page is not ("Customers" or "Sales orders" or "Inventory" or "Purchasing" or "Suppliers"))
                    ApplyClientFilter(dataGrid);
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
            PageContent.Content = new Border
            {
                Background = System.Windows.Media.Brushes.White,
                BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(238, 190, 190)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(18),
                Child = new StackPanel
                {
                    Children =
                    {
                        new TextBlock { Text = "We couldn’t load this workspace", FontSize = 17, FontWeight = FontWeights.SemiBold, Foreground = System.Windows.Media.Brushes.DarkRed, Margin = new Thickness(0, 0, 0, 8) },
                        new TextBlock { Text = ex.Message, TextWrapping = TextWrapping.Wrap, Foreground = System.Windows.Media.Brushes.DimGray },
                        new TextBlock { Text = "Check your connection and account permissions, then select the workspace again to retry.", TextWrapping = TextWrapping.Wrap, Foreground = System.Windows.Media.Brushes.Gray, Margin = new Thickness(0, 10, 0, 0) }
                    }
                }
            };
            StatusText.Text = "Workspace failed to load. You can retry without restarting IWMS.";
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

        var grid = new DataGrid
        {
            AutoGenerateColumns = true,
            IsReadOnly = true,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            AlternatingRowBackground = System.Windows.Media.Brushes.WhiteSmoke,
            SelectionMode = DataGridSelectionMode.Single
        };
        grid.AutoGeneratingColumn += (_, args) =>
        {
            var name = args.PropertyName;
            if (name.Equals("id", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("version", StringComparison.OrdinalIgnoreCase))
                args.Column.Visibility = Visibility.Collapsed;
            args.Column.Header = Humanize(name);
        };
        grid.ItemsSource = table.DefaultView;
        return grid;
    }

    private static string FormatJsonValue(JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Null:
                return string.Empty;
            case JsonValueKind.String:
                var text = value.GetString() ?? string.Empty;
                if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var timestamp))
                    return timestamp.ToLocalTime().ToString("dd MMM yyyy HH:mm", CultureInfo.CurrentCulture);
                return text;
            case JsonValueKind.Number when value.TryGetDecimal(out var number):
                return number.ToString("0.####", CultureInfo.CurrentCulture);
            case JsonValueKind.True:
                return "Yes";
            case JsonValueKind.False:
                return "No";
            default:
                return value.ToString();
        }
    }



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

    private async Task RunSelectedFulfillmentActionAsync(DataGrid grid, string action, string reloadPage = "Fulfillment")
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
            else if (action == "cancel-draft")
                await _api.PostJsonAsync($"api/sales-orders/{id}/cancel", new { });
            else if (action == "cancel-order")
                await _api.PostJsonAsync($"api/fulfillment/sales-orders/{id}/cancel", new { });
            else if (action == "close-short")
                await _api.PostJsonAsync($"api/fulfillment/sales-orders/{id}/close-short", new { });
            else if (action == "dispatch")
                await _api.PostJsonAsync($"api/fulfillment/shipments/{id}/dispatch", new { });
            else if (action == "cancel-shipment")
                await _api.PostJsonAsync($"api/fulfillment/shipments/{id}/cancel", new { });
            StatusText.Text = "Action completed.";
            if (reloadPage == "Fulfillment")
                await LoadWorkspaceAsync("Fulfillment");
            else
                await OpenPageAsync(reloadPage);
        }
        catch (Exception ex) { StatusText.Text = ex.Message; }
    }

    private async Task RunPurchaseOrderActionAsync(DataGrid grid, string action)
    {
        var id = GetSelectedGuid(grid);
        if (id is null) { StatusText.Text = "Select a purchase order first."; return; }
        try
        {
            if (action == "submit")
                await _api.PostJsonAsync($"api/purchase-orders/{id}/submit", new { });
            else if (action == "approve")
                await _api.PostJsonAsync($"api/purchase-orders/{id}/approve", new { });
            else if (action == "reject" || action == "cancel")
            {
                var reason = Prompt(action == "reject" ? "Reject purchase order" : "Cancel purchase order", "Reason:");
                if (string.IsNullOrWhiteSpace(reason)) return;
                await _api.PostJsonAsync($"api/purchase-orders/{id}/{action}", new { reason });
            }
            StatusText.Text = "Purchase order updated.";
            await LoadWorkspaceAsync("Purchasing");
        }
        catch (Exception ex) { StatusText.Text = ex.Message; }
    }

    private async Task CreateUserFromDesktopAsync()
    {
        try
        {
            var fullName = Prompt("Create IWMS user", "Full name (first and last):");
            var parts = (fullName ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length < 2) { StatusText.Text = "Enter a first name and last name."; return; }
            var email = Prompt("Create IWMS user", "Email address:");
            var password = Prompt("Create IWMS user", "Temporary password (10+ characters, letters and digits):");
            var role = Prompt("Assign role", "Choose exactly one role: Administrator, Warehouse Manager, Purchaser, Warehouse Operator, Sales Officer, Viewer");
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(role))
            { StatusText.Text = "User creation cancelled."; return; }
            await _api.PostJsonAsync("api/users", new
            {
                firstName = parts[0],
                lastName = string.Join(" ", parts.Skip(1)),
                email,
                password,
                roles = new[] { role }
            });
            await LoadWorkspaceAsync("Users");
            StatusText.Text = "User created. Share the temporary password securely.";
        }
        catch (Exception ex) { StatusText.Text = ex.Message; }
    }

    private async Task ResetSelectedUserPasswordAsync(DataGrid grid)
    {
        var id = GetSelectedGuid(grid);
        if (id is null) { StatusText.Text = "Select a user first."; return; }
        var password = Prompt("Reset user password", "New password (10+ characters, letters and digits):");
        if (string.IsNullOrWhiteSpace(password)) return;
        try
        {
            await _api.PutJsonAsync($"api/users/{id}/password", new { newPassword = password });
            StatusText.Text = "Password reset. Share the new password securely.";
        }
        catch (Exception ex) { StatusText.Text = ex.Message; }
    }

    private async Task ChangeSelectedUserRoleAsync(DataGrid grid)
    {
        var id = GetSelectedGuid(grid);
        if (id is null) { StatusText.Text = "Select a user first."; return; }
        var role = Prompt("Change user role", "Choose exactly one role: Administrator, Warehouse Manager, Purchaser, Warehouse Operator, Sales Officer, Viewer");
        if (string.IsNullOrWhiteSpace(role)) return;
        try
        {
            await _api.PutJsonAsync($"api/users/{id}/roles", new { roles = new[] { role } });
            await LoadWorkspaceAsync("Users");
            StatusText.Text = "User role updated.";
        }
        catch (Exception ex) { StatusText.Text = ex.Message; }
    }

    private async Task ToggleSelectedUserStatusAsync(DataGrid grid)
    {
        if (grid.SelectedItem is not DataRowView row || !row.Row.Table.Columns.Contains("id") ||
            !row.Row.Table.Columns.Contains("isActive"))
        { StatusText.Text = "Select a user row with an account status."; return; }
        if (!Guid.TryParse(Convert.ToString(row["id"], CultureInfo.InvariantCulture), out var id))
        { StatusText.Text = "The selected user ID is invalid."; return; }
        var isActive = bool.TryParse(Convert.ToString(row["isActive"], CultureInfo.InvariantCulture), out var active) && active;
        var confirm = MessageBox.Show(
            isActive ? "Deactivate this account? The user will no longer be able to sign in." : "Activate this account?",
            "Update user access", MessageBoxButton.YesNo, isActive ? MessageBoxImage.Warning : MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;
        try
        {
            await _api.PutJsonAsync($"api/users/{id}/status", new { isActive = !isActive });
            await LoadWorkspaceAsync("Users");
            StatusText.Text = isActive ? "User deactivated." : "User activated.";
        }
        catch (Exception ex) { StatusText.Text = ex.Message; }
    }

    private async Task CreateStockTransferFromDesktopAsync()
    {
        try
        {
            if (!Guid.TryParse(Prompt("New transfer", "Source warehouse ID (GUID):"), out var sourceWarehouseId) ||
                !Guid.TryParse(Prompt("New transfer", "Destination warehouse ID (GUID):"), out var destinationWarehouseId))
            { StatusText.Text = "Transfer creation cancelled or a warehouse ID was invalid."; return; }
            if (sourceWarehouseId == destinationWarehouseId)
            { StatusText.Text = "Source and destination warehouses must be different."; return; }

            var reason = Prompt("New transfer", "Reason for transfer:") ?? string.Empty;
            var lines = new List<object>();
            var lineNumber = 1;
            while (true)
            {
                if (!Guid.TryParse(Prompt("Transfer line", $"Line {lineNumber} — Product ID (GUID):"), out var productId) ||
                    !Guid.TryParse(Prompt("Transfer line", $"Line {lineNumber} — Source storage location ID (GUID):"), out var fromLocationId) ||
                    !Guid.TryParse(Prompt("Transfer line", $"Line {lineNumber} — Destination storage location ID (GUID):"), out var toLocationId))
                { StatusText.Text = $"Transfer cancelled: line {lineNumber} has an invalid ID."; return; }
                if (!decimal.TryParse(Prompt("Transfer line", $"Line {lineNumber} — Quantity:"), NumberStyles.Number, CultureInfo.InvariantCulture, out var quantity) || quantity <= 0)
                { StatusText.Text = "Each transfer line needs a positive quantity (use a dot for decimals)."; return; }
                if (fromLocationId == toLocationId)
                { StatusText.Text = "A transfer line must use different source and destination locations."; return; }

                lines.Add(new { productId, fromStorageLocationId = fromLocationId, toStorageLocationId = toLocationId, quantity });
                if (lines.Count >= 50) break;
                var addAnother = Prompt("Transfer lines", $"Added line {lineNumber}. Add another line? Enter yes or no:");
                if (!string.Equals(addAnother, "yes", StringComparison.OrdinalIgnoreCase)) break;
                lineNumber++;
            }

            await _api.PostJsonAsync("api/stock-transfers", new
            {
                sourceWarehouseId, destinationWarehouseId, reason, lines
            });
            await LoadWorkspaceAsync("Stock transfers");
            StatusText.Text = $"Transfer request submitted with {lines.Count} line(s) for approval.";
        }
        catch (Exception ex) { StatusText.Text = $"Transfer could not be created: {ex.Message}"; }
    }

    private async Task RecordSelectedStockCountAsync(DataGrid grid)
    {
        var id = GetSelectedGuid(grid);
        if (id is null) { StatusText.Text = "Select a stock count first."; return; }
        try
        {
            var detail = await _api.GetJsonAsync($"api/stock-counts/{id}");
            if (!detail.TryGetProperty("lines", out var lines) || lines.ValueKind != JsonValueKind.Array)
                throw new InvalidOperationException("The stock count response did not contain lines.");
            var payload = new List<object>();
            foreach (var line in lines.EnumerateArray())
            {
                var lineId = line.GetProperty("id").GetGuid();
                var systemQuantity = line.GetProperty("systemQuantity").GetDecimal();
                var countedText = Prompt("Physical stock count", $"{line.GetProperty("sku").GetString()} at {line.GetProperty("location").GetString()}. System quantity: {systemQuantity}. Enter counted quantity:");
                if (!decimal.TryParse(countedText, NumberStyles.Number, CultureInfo.InvariantCulture, out var counted) || counted < 0)
                    throw new InvalidOperationException("Count cancelled or invalid. Enter non-negative quantities using invariant decimal format.");
                var varianceReason = string.Empty;
                if (counted != systemQuantity)
                    varianceReason = Prompt("Count variance", "Explain the difference:") ?? string.Empty;
                payload.Add(new { lineId, countedQuantity = counted, varianceReason });
            }
            await _api.PutJsonAsync($"api/stock-counts/{id}/lines", new { lines = payload });
            await LoadWorkspaceAsync("Stock counts");
            StatusText.Text = "Physical counts recorded.";
        }
        catch (Exception ex) { StatusText.Text = ex.Message; }
    }

    private async Task PostSelectedStockCountAsync(DataGrid grid)
    {
        var id = GetSelectedGuid(grid);
        if (id is null) { StatusText.Text = "Select a stock count first."; return; }
        var confirm = MessageBox.Show("Post this completed count and apply its inventory variances?", "Post stock count", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;
        try
        {
            await _api.PostJsonAsync($"api/stock-counts/{id}/post", new { });
            await LoadWorkspaceAsync("Stock counts");
            StatusText.Text = "Stock count adjustments posted.";
        }
        catch (Exception ex) { StatusText.Text = ex.Message; }
    }

    private async Task CreateReturnFromDesktopAsync(bool customerReturn)
    {
        try
        {
            var title = customerReturn ? "Customer return" : "Supplier return";
            if (!Guid.TryParse(Prompt(title, customerReturn ? "Sales order ID (GUID):" : "Goods receipt ID (GUID):"), out var sourceId))
            { StatusText.Text = "Return creation cancelled or the source document ID was invalid."; return; }

            var reason = Prompt(title, "Reason for return:") ?? string.Empty;
            var notes = Prompt(title, "Document notes (optional):") ?? string.Empty;
            var lines = new List<object>();
            var lineNumber = 1;
            while (true)
            {
                if (!Guid.TryParse(Prompt("Return line", $"Line {lineNumber} — Product ID (GUID):"), out var productId))
                { StatusText.Text = $"Return cancelled: line {lineNumber} has an invalid product ID."; return; }
                if (!decimal.TryParse(Prompt("Return line", $"Line {lineNumber} — Quantity:"), NumberStyles.Number, CultureInfo.InvariantCulture, out var quantity) || quantity <= 0)
                { StatusText.Text = "Each return line needs a positive quantity (use a dot for decimals)."; return; }
                var condition = Prompt("Return line", $"Line {lineNumber} — Product condition:") ?? string.Empty;
                var disposition = customerReturn ? 1 : 4;
                if (customerReturn)
                {
                    var choice = Prompt("Return disposition", "1 = Restock, 2 = Quarantine, 3 = Scrap:");
                    if (!int.TryParse(choice, out disposition) || disposition is < 1 or > 3)
                    { StatusText.Text = "Choose disposition 1, 2 or 3."; return; }
                }

                Guid? storageLocationId = null;
                if (!customerReturn || disposition == 1)
                {
                    if (!Guid.TryParse(Prompt("Return line", $"Line {lineNumber} — Storage location ID (GUID):"), out var locationId))
                    { StatusText.Text = "A valid storage location ID is required for this return disposition."; return; }
                    storageLocationId = locationId;
                }

                lines.Add(new { productId, quantity, reason, condition, disposition, storageLocationId });
                if (lines.Count >= 50) break;
                var addAnother = Prompt("Return lines", $"Added line {lineNumber}. Add another line? Enter yes or no:");
                if (!string.Equals(addAnother, "yes", StringComparison.OrdinalIgnoreCase)) break;
                lineNumber++;
            }

            Guid? salesOrderId = customerReturn ? sourceId : null;
            Guid? goodsReceiptId = customerReturn ? null : sourceId;
            await _api.PostJsonAsync("api/returns", new
            {
                type = customerReturn ? 1 : 2, salesOrderId, goodsReceiptId, notes,
                lines
            });
            await LoadWorkspaceAsync("Returns");
            StatusText.Text = $"{title} recorded with {lines.Count} line(s).";
        }
        catch (Exception ex) { StatusText.Text = $"Return could not be recorded: {ex.Message}"; }
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
                var receiptLines = new List<object>();
                foreach (var line in lines.EnumerateArray())
                {
                    var lineId = line.GetProperty("id").GetGuid();
                    var dispatched = line.GetProperty("quantityDispatched").GetDecimal();
                    var receivedText = Prompt("Receive transfer", $"{line.GetProperty("sku").GetString()} dispatched quantity: {dispatched}. Enter quantity received:");
                    if (!decimal.TryParse(receivedText, NumberStyles.Number, CultureInfo.InvariantCulture, out var received) ||
                        received < 0 || received > dispatched)
                        throw new InvalidOperationException("Receipt cancelled or invalid. Enter a quantity between zero and dispatched quantity.");
                    var discrepancyNote = received == dispatched
                        ? null
                        : Prompt("Receipt discrepancy", "Explain the missing or damaged quantity:");
                    if (received != dispatched && string.IsNullOrWhiteSpace(discrepancyNote))
                        throw new InvalidOperationException("A discrepancy note is required when received quantity differs from dispatched quantity.");
                    receiptLines.Add(new { lineId, quantityReceived = received, discrepancyNote });
                }
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

    private async Task ShowReportsAsync()
    {
        var root = new StackPanel();
        var toolbar = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
        foreach (var days in new[] { 14, 30, 90 })
        {
            var period = ActionButton($"{days} days", primary: days == _reportDays);
            var selectedDays = days;
            period.Click += async (_, _) =>
            {
                _reportDays = selectedDays;
                await ShowReportsAsync();
            };
            toolbar.Children.Add(period);
        }
        var csv = ActionButton("Download inventory CSV");
        csv.Click += async (_, _) => await DownloadInventoryCsvAsync();
        var pdf = ActionButton("Download inventory PDF");
        pdf.Click += async (_, _) => await DownloadInventoryPdfAsync();
        toolbar.Children.Add(csv);
        toolbar.Children.Add(pdf);
        root.Children.Add(toolbar);
        root.Children.Add(await BuildTrendCardAsync(_reportDays, $"Inventory movement · {_reportDays} days", "Daily receipts and issues. Use the exports for a full stock snapshot."));
        await AddReportSectionAsync(root, "Stock value by warehouse", "api/reports/stock-value");
        await AddReportSectionAsync(root, $"Dead stock · no movement in {_reportDays} days", $"api/reports/dead-stock?days={_reportDays}");
        await AddReportSectionAsync(root, "ABC classification", "api/reports/abc");
        await AddReportSectionAsync(root, "Supplier fill rate", "api/reports/supplier-performance");
        PageContent.Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = root };
        StatusText.Text = $"Reports updated · {_reportDays}-day movement chart.";
    }

    private async Task<Border> BuildTrendCardAsync(int days, string title, string subtitle)
    {
        var body = new StackPanel();
        body.Children.Add(new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeights.Bold, Foreground = Brush("#142B45") });
        body.Children.Add(new TextBlock { Text = subtitle, FontSize = 12, Foreground = Brush("#64748B"), Margin = new Thickness(0, 4, 0, 10), TextWrapping = TextWrapping.Wrap });
        var legend = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        legend.Children.Add(LegendSwatch("Receipts", "#0F766E"));
        legend.Children.Add(LegendSwatch("Issues", "#C2410C"));
        body.Children.Add(legend);
        try
        {
            var points = await LoadTrendAsync(days);
            var chart = new MovementTrendChart
            {
                Points = points,
                Height = 280,
                MinWidth = Math.Max(680, points.Count * 26)
            };
            body.Children.Add(new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Height = 290,
                Content = chart
            });
        }
        catch (Exception ex)
        {
            body.Children.Add(new TextBlock { Text = "The movement chart could not be loaded. " + ex.Message, Foreground = Brush("#B42318"), TextWrapping = TextWrapping.Wrap });
        }

        return new Border
        {
            Background = Brushes.White,
            BorderBrush = Brush("#E2E8F0"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(16),
            Margin = new Thickness(0, 0, 0, 16),
            Child = body
        };
    }

    private async Task<IReadOnlyList<MovementTrendPoint>> LoadTrendAsync(int days)
    {
        var json = await _api.GetJsonAsync($"api/reports/inventory-trend?days={days}");
        var points = new List<MovementTrendPoint>();
        if (json.ValueKind != JsonValueKind.Array)
            return points;
        foreach (var item in json.EnumerateArray())
        {
            points.Add(new MovementTrendPoint(
                item.GetProperty("dateUtc").GetDateTime(),
                item.GetProperty("receipts").GetDecimal(),
                item.GetProperty("issues").GetDecimal(),
                item.GetProperty("netMovement").GetDecimal()));
        }
        return points;
    }

    private void HighlightNav(string page)
    {
        foreach (var button in new[]
        {
            DashboardNavButton, AlertsNavButton, ReportsNavButton, CustomersNavButton, SalesOrdersNavButton, SuppliersNavButton,
            PurchasingNavButton, InventoryNavButton, WarehousesNavButton, FulfillmentNavButton, TransfersNavButton, CountsNavButton,
            ReturnsNavButton, ReceiptsNavButton, MovementsNavButton, AuditNavButton, UsersNavButton
        })
        {
            var active = button.Tag is string tag && tag == page;
            button.Background = active ? Brush("#1B4E68") : Brushes.Transparent;
            button.Foreground = active ? Brushes.White : Brush("#D7E6EF");
        }
    }

    private static Visibility AnyVisible(params Button[] buttons) =>
        buttons.Any(button => button.Visibility == Visibility.Visible) ? Visibility.Visible : Visibility.Collapsed;

    private Button ActionButton(string label, bool primary = false)
    {
        var button = new Button
        {
            Content = label,
            Margin = new Thickness(0, 0, 8, 8),
            Padding = new Thickness(12, 8, 12, 8)
        };
        if (primary && TryFindResource("PrimaryButton") is Style style)
            button.Style = style;
        return button;
    }

    private static UIElement LegendSwatch(string label, string color)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 16, 0) };
        row.Children.Add(new Border { Width = 12, Height = 12, Background = Brush(color), CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 1, 6, 0) });
        row.Children.Add(new TextBlock { Text = label, FontSize = 12, Foreground = Brush("#334155"), VerticalAlignment = VerticalAlignment.Center });
        return row;
    }

    private static SolidColorBrush Brush(string hex) =>
        (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;

    private static string Humanize(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return name;
        var chars = new List<char> { char.ToUpperInvariant(name[0]) };
        for (var i = 1; i < name.Length; i++)
        {
            if (char.IsUpper(name[i]) && !char.IsUpper(name[i - 1]))
                chars.Add(' ');
            chars.Add(name[i]);
        }
        return new string(chars.ToArray()).Replace("Utc", "UTC", StringComparison.Ordinal);
    }

    private void AddSearch(StackPanel toolbar, string page)
    {
        var box = new TextBox { Width = 200, Text = _listSearch, Margin = new Thickness(0, 0, 8, 8), ToolTip = "Search this workspace" };
        var search = ActionButton("Search");
        search.Click += async (_, _) =>
        {
            _listSearch = box.Text.Trim();
            await LoadWorkspaceAsync(page);
        };
        var clear = ActionButton("Clear");
        clear.Click += async (_, _) =>
        {
            _listSearch = "";
            await LoadWorkspaceAsync(page);
        };
        toolbar.Children.Add(box);
        toolbar.Children.Add(search);
        toolbar.Children.Add(clear);
    }

    private string WithSearch(string page, string endpoint)
    {
        if (string.IsNullOrWhiteSpace(_listSearch) || string.IsNullOrEmpty(endpoint))
            return endpoint;
        if (page is not ("Customers" or "Sales orders" or "Inventory" or "Purchasing" or "Suppliers"))
            return endpoint;
        return endpoint + (endpoint.Contains('?') ? "&" : "?") + "search=" + Uri.EscapeDataString(_listSearch.Trim());
    }

    private void ApplyClientFilter(DataGrid grid)
    {
        if (string.IsNullOrWhiteSpace(_listSearch) || grid.ItemsSource is not DataView view)
            return;
        var term = _listSearch.Trim().Replace("'", "''");
        var columns = view.Table.Columns.Cast<DataColumn>()
            .Where(column => !column.ColumnName.Equals("id", StringComparison.OrdinalIgnoreCase) &&
                             !column.ColumnName.Equals("version", StringComparison.OrdinalIgnoreCase))
            .Select(column => $"Convert([{column.ColumnName}], 'System.String') LIKE '%{term}%'");
        var filter = string.Join(" OR ", columns);
        if (!string.IsNullOrEmpty(filter))
            view.RowFilter = filter;
    }

    private async Task<UIElement> BuildAlertsPanelAsync()
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = "Notifications", FontSize = 16, FontWeight = FontWeights.Bold, Foreground = Brush("#142B45"), Margin = new Thickness(0, 8, 0, 8) });
        try
        {
            var alerts = await _api.GetJsonAsync("api/reports/alerts");
            if (alerts.ValueKind != JsonValueKind.Array || alerts.GetArrayLength() == 0)
                panel.Children.Add(new TextBlock { Text = "No low-stock, approval or overdue alerts.", Foreground = Brush("#64748B") });
            else
            {
                foreach (var alert in alerts.EnumerateArray().Take(8))
                    panel.Children.Add(AlertButton(alert));
            }
        }
        catch (Exception ex)
        {
            panel.Children.Add(new TextBlock { Text = "Alerts could not be loaded. " + ex.Message, Foreground = Brush("#B42318"), TextWrapping = TextWrapping.Wrap });
        }
        return panel;
    }

    private Button AlertButton(JsonElement alert)
    {
        var workspace = alert.GetProperty("workspace").GetString() ?? "Dashboard";
        var button = new Button
        {
            Content = $"{alert.GetProperty("title").GetString()} — {alert.GetProperty("detail").GetString()}",
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 6),
            Padding = new Thickness(10, 8, 10, 8)
        };
        button.Click += async (_, _) => await OpenPageAsync(workspace);
        return button;
    }

    private async Task ShowAlertsAsync()
    {
        var root = new StackPanel();
        try
        {
            var alerts = await _api.GetJsonAsync("api/reports/alerts");
            if (alerts.ValueKind != JsonValueKind.Array || alerts.GetArrayLength() == 0)
                root.Children.Add(new TextBlock { Text = "Nothing needs attention.", FontSize = 16 });
            else
                foreach (var alert in alerts.EnumerateArray())
                    root.Children.Add(AlertButton(alert));
            StatusText.Text = "Alerts refreshed.";
        }
        catch (Exception ex)
        {
            root.Children.Add(new TextBlock { Text = ex.Message, TextWrapping = TextWrapping.Wrap });
        }
        PageContent.Content = new ScrollViewer { Content = root };
    }

    private Task ShowWarehousesAsync()
    {
        var window = new WarehouseSetupWindow(_api) { Owner = this };
        window.ShowDialog();
        PageContent.Content = new TextBlock { Text = "Warehouse changes are saved immediately. Open this page again to add another warehouse or bin.", TextWrapping = TextWrapping.Wrap, FontSize = 15 };
        StatusText.Text = "Warehouse setup closed.";
        return Task.CompletedTask;
    }

    private async Task AddReportSectionAsync(StackPanel root, string title, string path)
    {
        root.Children.Add(new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 8), Foreground = Brush("#142B45") });
        try
        {
            var json = await _api.GetJsonAsync(path);
            root.Children.Add(CreateDataGrid(json));
        }
        catch (Exception ex)
        {
            root.Children.Add(new TextBlock { Text = ex.Message, Foreground = Brush("#B42318"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
        }
    }

    private async Task DownloadSelectedPdfAsync(DataGrid grid, string pathTemplate, string fileName)
    {
        var id = GetSelectedGuid(grid);
        if (id is null) { StatusText.Text = "Select a document first."; return; }
        try
        {
            var bytes = await _api.GetBytesAsync(pathTemplate.Replace("{id}", id.Value.ToString()));
            var dialog = new Microsoft.Win32.SaveFileDialog { FileName = fileName, Filter = "PDF files (*.pdf)|*.pdf" };
            if (dialog.ShowDialog(this) == true)
            {
                await System.IO.File.WriteAllBytesAsync(dialog.FileName, bytes);
                StatusText.Text = "PDF saved.";
            }
        }
        catch (Exception ex) { StatusText.Text = ex.Message; }
    }

    private sealed record CountWarehouseChoice(string Id, string Label);
    }
