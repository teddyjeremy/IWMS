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
        LoginApiUrlBox.Text = Environment.GetEnvironmentVariable("IWMS_API_BASE_URL") ?? _api.BaseUrl;
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
            _api.SetBaseUrl(LoginApiUrlBox.Text.Trim());
            await _api.SignInAsync(LoginEmailBox.Text.Trim(), LoginPasswordBox.Password);
            LoginPasswordBox.Clear();
            ConnectionLabel.Text = $"Signed in as {_api.CurrentUserName}";
            ApplyRoleBasedWorkspace();
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
        UsersNavButton.Visibility = Visibility.Collapsed;
        SetNavigationVisibility(false, DashboardNavButton, CustomersNavButton, SalesOrdersNavButton,
            FulfillmentNavButton, TransfersNavButton, CountsNavButton, ReturnsNavButton,
            InventoryNavButton, PurchasingNavButton, ReportsNavButton);
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
            "Users" => "Manage user accounts, roles and access",
            _ => "Inventory position and warehouse activity"
        };

        if (page == "Dashboard")
        {
            await LoadDashboardAsync();
            return;
        }

        await LoadWorkspaceAsync(page);
    }


    private void ApplyRoleBasedWorkspace()
    {
        var admin = _api.IsInRole("Administrator");
        var manager = _api.IsInRole("Warehouse Manager");
        var purchaser = _api.IsInRole("Purchaser");
        var operatorRole = _api.IsInRole("Warehouse Operator");
        var sales = _api.IsInRole("Sales Officer");
        var viewer = _api.IsInRole("Viewer") || _api.IsInRole("Auditor");

        DashboardNavButton.Visibility = Visibility.Visible;
        CustomersNavButton.Visibility = admin || sales ? Visibility.Visible : Visibility.Collapsed;
        SalesOrdersNavButton.Visibility = admin || sales || manager ? Visibility.Visible : Visibility.Collapsed;
        FulfillmentNavButton.Visibility = admin || manager || operatorRole || sales ? Visibility.Visible : Visibility.Collapsed;
        TransfersNavButton.Visibility = admin || manager || operatorRole ? Visibility.Visible : Visibility.Collapsed;
        CountsNavButton.Visibility = admin || manager || operatorRole ? Visibility.Visible : Visibility.Collapsed;
        ReturnsNavButton.Visibility = admin || manager || operatorRole ? Visibility.Visible : Visibility.Collapsed;
        InventoryNavButton.Visibility = Visibility.Visible;
        PurchasingNavButton.Visibility = admin || manager || purchaser ? Visibility.Visible : Visibility.Collapsed;
        ReportsNavButton.Visibility = admin || manager || purchaser || viewer ? Visibility.Visible : Visibility.Collapsed;
        UsersNavButton.Visibility = admin ? Visibility.Visible : Visibility.Collapsed;

        RoleLabel.Text = admin ? "Administrator workspace"
            : manager ? "Warehouse manager workspace"
            : purchaser ? "Purchasing workspace"
            : operatorRole ? "Warehouse operations workspace"
            : sales ? "Sales workspace"
            : viewer ? "Read-only / audit workspace"
            : "Standard workspace";
    }

    private static void SetNavigationVisibility(bool visible, params Button[] buttons)
    {
        foreach (var button in buttons)
            button.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
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
            var isAdmin = _api.IsInRole("Administrator");
            var isManager = _api.IsInRole("Warehouse Manager");
            var isPurchaser = _api.IsInRole("Purchaser");
            var isOperator = _api.IsInRole("Warehouse Operator");
            var isSales = _api.IsInRole("Sales Officer");
            var isAuditor = _api.IsInRole("Viewer") || _api.IsInRole("Auditor");
            var metrics = new List<(string Label, string Value)>();

            if (isAdmin || isManager || isPurchaser || isAuditor)
                metrics.Add(("Stock value", data.StockValue.ToString("N2", CultureInfo.InvariantCulture)));
            if (isAdmin || isManager || isPurchaser || isOperator || isAuditor)
                metrics.Add(("Low-stock products", data.LowStockProducts.ToString("N0")));
            if (isAdmin || isManager || isPurchaser)
                metrics.Add(("Open purchase orders", data.OpenPurchaseOrders.ToString("N0")));
            if (isAdmin || isManager || isPurchaser)
                metrics.Add(("Pending approvals", data.PendingApprovals.ToString("N0")));
            if (isAdmin || isManager || isOperator || isSales || isAuditor)
                metrics.Add(("Open sales orders", data.OpenSalesOrders.ToString("N0")));
            if (isAdmin || isManager || isOperator || isSales || isAuditor)
                metrics.Add(("Units reserved", data.UnitsReserved.ToString("N2", CultureInfo.InvariantCulture)));

            if (metrics.Count == 0)
            {
                metrics.Add(("Low-stock products", data.LowStockProducts.ToString("N0")));
                metrics.Add(("Open sales orders", data.OpenSalesOrders.ToString("N0")));
            }

            var panel = new UniformGrid
            {
                Columns = 3,
                Rows = (int)Math.Ceiling(metrics.Count / 3d),
                Margin = new Thickness(0, 4, 0, 0)
            };
            foreach (var metric in metrics)
                AddMetric(panel, metric.Label, metric.Value);

            var dashboard = new StackPanel();
            dashboard.Children.Add(new TextBlock
            {
                Text = $"Welcome, {_api.CurrentUserName}",
                FontSize = 20,
                FontWeight = FontWeights.SemiBold,
                Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#152433")!,
                Margin = new Thickness(0, 0, 0, 16)
            });
            dashboard.Children.Add(panel);
            PageContent.Content = dashboard;
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

        if (page == "Users" && !_api.IsInRole("Administrator"))
        {
            StatusText.Text = "Administrator access is required to manage users.";
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
            "Users" => "api/users",
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
            if (page is "Customers" or "Sales orders" or "Fulfillment" or "Stock transfers" or "Stock counts" or "Returns" or "Users")
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
                    createTransferButton.Click += async (_, _) => await CreateStockTransferFromDesktopAsync();
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
                    var recordCountButton = new Button { Content = "Record physical counts" };
                    recordCountButton.Click += async (_, _) => await RecordSelectedStockCountAsync(dataGrid);
                    var postCountButton = new Button { Content = "Post selected count" };
                    postCountButton.Click += async (_, _) => await PostSelectedStockCountAsync(dataGrid);
                    toolbar.Children.Add(recordCountButton);
                    toolbar.Children.Add(postCountButton);
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
                else if (page == "Returns")
                {
                    var customerReturnButton = new Button { Content = "New customer return" };
                    customerReturnButton.Click += async (_, _) => await CreateReturnFromDesktopAsync(customerReturn: true);
                    var supplierReturnButton = new Button { Content = "New supplier return" };
                    supplierReturnButton.Click += async (_, _) => await CreateReturnFromDesktopAsync(customerReturn: false);
                    toolbar.Children.Add(customerReturnButton);
                    toolbar.Children.Add(supplierReturnButton);
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
                !Guid.TryParse(Prompt("New transfer", "Destination warehouse ID (GUID):"), out var destinationWarehouseId) ||
                !Guid.TryParse(Prompt("New transfer", "Product ID (GUID):"), out var productId) ||
                !Guid.TryParse(Prompt("New transfer", "Source storage location ID (GUID):"), out var fromLocationId) ||
                !Guid.TryParse(Prompt("New transfer", "Destination storage location ID (GUID):"), out var toLocationId))
            { StatusText.Text = "Transfer creation cancelled or an ID was invalid."; return; }
            if (!decimal.TryParse(Prompt("New transfer", "Quantity to transfer:"), NumberStyles.Number, CultureInfo.InvariantCulture, out var quantity) || quantity <= 0)
            { StatusText.Text = "Enter a positive quantity using invariant decimal format."; return; }
            var reason = Prompt("New transfer", "Reason for transfer:");
            await _api.PostJsonAsync("api/stock-transfers", new
            {
                sourceWarehouseId, destinationWarehouseId, reason,
                lines = new[] { new { productId, fromStorageLocationId = fromLocationId, toStorageLocationId = toLocationId, quantity } }
            });
            await LoadWorkspaceAsync("Stock transfers");
            StatusText.Text = "Transfer request submitted for approval.";
        }
        catch (Exception ex) { StatusText.Text = ex.Message; }
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
            if (!Guid.TryParse(Prompt(customerReturn ? "Customer return" : "Supplier return", customerReturn ? "Sales order ID (GUID):" : "Goods receipt ID (GUID):"), out var sourceId) ||
                !Guid.TryParse(Prompt("Return", "Product ID (GUID):"), out var productId))
            { StatusText.Text = "Return creation cancelled or an ID was invalid."; return; }
            if (!decimal.TryParse(Prompt("Return", "Quantity:"), NumberStyles.Number, CultureInfo.InvariantCulture, out var quantity) || quantity <= 0)
            { StatusText.Text = "Enter a positive quantity using invariant decimal format."; return; }
            var reason = Prompt("Return", "Reason for return:") ?? string.Empty;
            var condition = Prompt("Return", "Product condition:") ?? string.Empty;
            var disposition = customerReturn ? 1 : 4;
            if (customerReturn)
            {
                var choice = Prompt("Customer return disposition", "1 = Restock, 2 = Quarantine, 3 = Scrap:");
                if (!int.TryParse(choice, out disposition) || disposition is < 1 or > 3)
                { StatusText.Text = "Choose disposition 1, 2 or 3."; return; }
            }
            Guid? storageLocationId = null;
            if (!customerReturn || disposition == 1)
            {
                if (!Guid.TryParse(Prompt("Return", "Storage location ID (GUID):"), out var locationId))
                { StatusText.Text = "A valid storage location ID is required for this disposition."; return; }
                storageLocationId = locationId;
            }
            Guid? salesOrderId = customerReturn ? sourceId : null;
            Guid? goodsReceiptId = customerReturn ? null : sourceId;
            await _api.PostJsonAsync("api/returns", new
            {
                type = customerReturn ? 1 : 2, salesOrderId, goodsReceiptId, notes = string.Empty,
                lines = new[] { new { productId, quantity, reason, condition, disposition, storageLocationId } }
            });
            await LoadWorkspaceAsync("Returns");
            StatusText.Text = "Return recorded.";
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
