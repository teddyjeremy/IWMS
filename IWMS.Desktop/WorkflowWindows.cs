using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using IWMS.Desktop.Services;
using Microsoft.Win32;

namespace IWMS.Desktop;

public sealed class WarehouseSetupWindow : Window
{
    private readonly ApiClient _api;
    private readonly ComboBox _warehouses = new() { DisplayMemberPath = "Label", Margin = new Thickness(0, 4, 0, 10) };
    private readonly TextBox _code = new();
    private readonly TextBox _name = new();
    private readonly TextBox _address = new();
    private readonly TextBox _binCode = new();
    private readonly TextBox _binDescription = new();
    private readonly TextBlock _error = new() { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel _bins = new();

    public WarehouseSetupWindow(ApiClient api)
    {
        _api = api;
        Title = "Warehouses and bins";
        Width = 640;
        Height = 720;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = Build();
        Loaded += async (_, _) => await ReloadAsync();
    }

    private UIElement Build()
    {
        var root = new StackPanel { Margin = new Thickness(20) };
        root.Children.Add(new TextBlock { Text = "Warehouse and bin setup", FontSize = 22, FontWeight = FontWeights.SemiBold });
        root.Children.Add(new TextBlock { Text = "Create a warehouse, then add aisle, rack or bin locations.", Margin = new Thickness(0, 4, 0, 12), TextWrapping = TextWrapping.Wrap });
        root.Children.Add(Label("Existing warehouse"));
        root.Children.Add(_warehouses);
        root.Children.Add(_bins);
        root.Children.Add(Label("New warehouse code"));
        root.Children.Add(_code);
        root.Children.Add(Label("Name"));
        root.Children.Add(_name);
        root.Children.Add(Label("Address"));
        root.Children.Add(_address);
        var create = new Button { Content = "Create warehouse", Margin = new Thickness(0, 8, 0, 16), HorizontalAlignment = HorizontalAlignment.Left };
        create.Click += async (_, _) => await CreateWarehouseAsync();
        root.Children.Add(create);
        root.Children.Add(Label("New bin code"));
        root.Children.Add(_binCode);
        root.Children.Add(Label("Bin description"));
        root.Children.Add(_binDescription);
        var add = new Button { Content = "Add bin to selected warehouse", Margin = new Thickness(0, 8, 0, 8), HorizontalAlignment = HorizontalAlignment.Left };
        add.Click += async (_, _) => await AddBinAsync();
        root.Children.Add(add);
        root.Children.Add(_error);
        return new ScrollViewer { Content = root };
    }

    private async Task ReloadAsync()
    {
        _warehouses.Items.Clear();
        _bins.Children.Clear();
        var json = await _api.GetJsonAsync("api/warehouses");
        foreach (var warehouse in json.EnumerateArray())
        {
            var id = warehouse.GetProperty("id").GetString()!;
            var label = $"{warehouse.GetProperty("code").GetString()} — {warehouse.GetProperty("name").GetString()}";
            _warehouses.Items.Add(new Choice(id, label, warehouse.GetRawText()));
        }
        if (_warehouses.Items.Count > 0)
        {
            _warehouses.SelectedIndex = 0;
            _warehouses.SelectionChanged += (_, _) => ShowBins();
            ShowBins();
        }
    }

    private void ShowBins()
    {
        _bins.Children.Clear();
        if (_warehouses.SelectedItem is not Choice choice) return;
        using var doc = JsonDocument.Parse(choice.Raw);
        if (!doc.RootElement.TryGetProperty("locations", out var locations)) return;
        foreach (var location in locations.EnumerateArray())
            _bins.Children.Add(new TextBlock { Text = $"{location.GetProperty("code").GetString()}  {location.GetProperty("description").GetString()}", Margin = new Thickness(0, 0, 0, 2) });
    }

    private async Task CreateWarehouseAsync()
    {
        _error.Text = "";
        try
        {
            await _api.PostJsonAsync("api/warehouses", new { code = _code.Text.Trim(), name = _name.Text.Trim(), address = _address.Text.Trim() });
            _code.Clear(); _name.Clear(); _address.Clear();
            await ReloadAsync();
        }
        catch (Exception ex) { _error.Text = ex.Message; }
    }

    private async Task AddBinAsync()
    {
        _error.Text = "";
        if (_warehouses.SelectedItem is not Choice warehouse || !Guid.TryParse(warehouse.Id, out var id))
        {
            _error.Text = "Select a warehouse first.";
            return;
        }
        try
        {
            await _api.PostJsonAsync($"api/warehouses/{id}/locations", new { code = _binCode.Text.Trim(), description = _binDescription.Text.Trim() });
            _binCode.Clear(); _binDescription.Clear();
            await ReloadAsync();
        }
        catch (Exception ex) { _error.Text = ex.Message; }
    }

    private static TextBlock Label(string text) => new() { Text = text, Margin = new Thickness(0, 8, 0, 0) };
    private sealed record Choice(string Id, string Label, string Raw);
}

public sealed class TransferRequestWindow : Window
{
    private readonly ApiClient _api;
    private readonly ComboBox _source = new() { DisplayMemberPath = "Label" };
    private readonly ComboBox _destination = new() { DisplayMemberPath = "Label" };
    private readonly ComboBox _product = new() { DisplayMemberPath = "Label" };
    private readonly ComboBox _from = new() { DisplayMemberPath = "Label" };
    private readonly ComboBox _to = new() { DisplayMemberPath = "Label" };
    private readonly TextBox _quantity = new() { Text = "1" };
    private readonly TextBox _reason = new();
    private readonly TextBlock _error = new() { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap };
    private readonly List<object> _lines = new();
    private readonly TextBlock _summary = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    private List<BinChoice> _bins = new();

    public TransferRequestWindow(ApiClient api)
    {
        _api = api;
        Title = "New stock transfer";
        Width = 640;
        Height = 680;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new StackPanel { Margin = new Thickness(20) };
        root.Children.Add(new TextBlock { Text = "Request a transfer", FontSize = 22, FontWeight = FontWeights.SemiBold });
        root.Children.Add(Field("Source warehouse", _source));
        root.Children.Add(Field("Destination warehouse", _destination));
        root.Children.Add(Field("Product", _product));
        root.Children.Add(Field("From bin", _from));
        root.Children.Add(Field("To bin", _to));
        root.Children.Add(Field("Quantity", _quantity));
        root.Children.Add(Field("Reason", _reason));
        var add = new Button { Content = "Add line", Margin = new Thickness(0, 8, 8, 0), HorizontalAlignment = HorizontalAlignment.Left };
        add.Click += (_, _) => AddLine();
        var save = new Button { Content = "Submit transfer", Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        save.Click += async (_, _) => await SubmitAsync();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(add);
        buttons.Children.Add(save);
        root.Children.Add(buttons);
        root.Children.Add(_summary);
        root.Children.Add(_error);
        Content = new ScrollViewer { Content = root };
        _source.SelectionChanged += (_, _) => FilterBins();
        _destination.SelectionChanged += (_, _) => FilterBins();
        Loaded += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        var warehouses = await _api.GetJsonAsync("api/warehouses");
        _bins = new List<BinChoice>();
        foreach (var warehouse in warehouses.EnumerateArray())
        {
            var warehouseId = warehouse.GetProperty("id").GetString()!;
            _source.Items.Add(new Choice(warehouseId, $"{warehouse.GetProperty("code").GetString()} — {warehouse.GetProperty("name").GetString()}"));
            _destination.Items.Add(new Choice(warehouseId, $"{warehouse.GetProperty("code").GetString()} — {warehouse.GetProperty("name").GetString()}"));
            if (!warehouse.TryGetProperty("locations", out var locations)) continue;
            foreach (var location in locations.EnumerateArray())
            {
                if (location.TryGetProperty("isActive", out var active) && active.ValueKind == JsonValueKind.False) continue;
                _bins.Add(new BinChoice(location.GetProperty("id").GetString()!, warehouseId, $"{warehouse.GetProperty("code").GetString()} / {location.GetProperty("code").GetString()}"));
            }
        }
        var products = await _api.GetListJsonAsync("api/products");
        var items = products.TryGetProperty("items", out var paged) ? paged : products;
        foreach (var product in items.EnumerateArray())
            _product.Items.Add(new Choice(product.GetProperty("id").GetString()!, $"{product.GetProperty("sku").GetString()} — {product.GetProperty("name").GetString()}"));
        if (_source.Items.Count > 0) _source.SelectedIndex = 0;
        if (_destination.Items.Count > 1) _destination.SelectedIndex = 1;
        else if (_destination.Items.Count > 0) _destination.SelectedIndex = 0;
        if (_product.Items.Count > 0) _product.SelectedIndex = 0;
        FilterBins();
    }

    private void FilterBins()
    {
        var sourceId = (_source.SelectedItem as Choice)?.Id;
        var destId = (_destination.SelectedItem as Choice)?.Id;
        _from.ItemsSource = _bins.Where(b => b.WarehouseId == sourceId).ToList();
        _to.ItemsSource = _bins.Where(b => b.WarehouseId == destId).ToList();
        if (_from.Items.Count > 0) _from.SelectedIndex = 0;
        if (_to.Items.Count > 0) _to.SelectedIndex = 0;
    }

    private void AddLine()
    {
        _error.Text = "";
        if (_product.SelectedItem is not Choice product || _from.SelectedItem is not BinChoice from || _to.SelectedItem is not BinChoice to)
        {
            _error.Text = "Choose a product and both bins.";
            return;
        }
        if (!decimal.TryParse(_quantity.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out var quantity) || quantity <= 0)
        {
            _error.Text = "Quantity must be greater than zero.";
            return;
        }
        if (from.Id == to.Id)
        {
            _error.Text = "Source and destination bins must be different.";
            return;
        }
        _lines.Add(new { productId = Guid.Parse(product.Id), fromStorageLocationId = Guid.Parse(from.Id), toStorageLocationId = Guid.Parse(to.Id), quantity });
        _summary.Text = $"{_lines.Count} line(s) ready. Last: {product.Label} × {quantity}.";
    }

    private async Task SubmitAsync()
    {
        _error.Text = "";
        if (_lines.Count == 0) AddLine();
        if (_lines.Count == 0) return;
        if (_source.SelectedItem is not Choice source || _destination.SelectedItem is not Choice destination)
        {
            _error.Text = "Choose both warehouses.";
            return;
        }
        if (source.Id == destination.Id)
        {
            _error.Text = "Source and destination warehouses must be different.";
            return;
        }
        try
        {
            await _api.PostJsonAsync("api/stock-transfers", new
            {
                sourceWarehouseId = Guid.Parse(source.Id),
                destinationWarehouseId = Guid.Parse(destination.Id),
                reason = _reason.Text.Trim(),
                lines = _lines
            });
            DialogResult = true;
        }
        catch (Exception ex) { _error.Text = ex.Message; }
    }

    private static StackPanel Field(string label, Control input)
    {
        input.Margin = new Thickness(0, 4, 0, 8);
        return new StackPanel { Children = { new TextBlock { Text = label }, input } };
    }

    private sealed record Choice(string Id, string Label);
    private sealed record BinChoice(string Id, string WarehouseId, string Label);
}

public sealed class ReturnRequestWindow : Window
{
    private readonly ApiClient _api;
    private readonly bool _customer;
    private readonly ComboBox _source = new() { DisplayMemberPath = "Label" };
    private readonly ComboBox _product = new() { DisplayMemberPath = "Label" };
    private readonly ComboBox _location = new() { DisplayMemberPath = "Label" };
    private readonly ComboBox _disposition = new();
    private readonly TextBox _quantity = new() { Text = "1" };
    private readonly TextBox _condition = new();
    private readonly TextBox _reason = new();
    private readonly TextBox _notes = new();
    private readonly TextBox _lot = new();
    private readonly TextBox _serials = new();
    private readonly TextBlock _error = new() { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap };
    private readonly List<object> _lines = new();

    public ReturnRequestWindow(ApiClient api, bool customer)
    {
        _api = api;
        _customer = customer;
        Title = customer ? "Customer return" : "Supplier return";
        Width = 640;
        Height = 740;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        if (customer)
        {
            _disposition.Items.Add(new DispositionChoice(1, "Restock"));
            _disposition.Items.Add(new DispositionChoice(2, "Quarantine"));
            _disposition.Items.Add(new DispositionChoice(3, "Scrap"));
            _disposition.SelectedIndex = 0;
        }
        var root = new StackPanel { Margin = new Thickness(20) };
        root.Children.Add(new TextBlock { Text = Title, FontSize = 22, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
        root.Children.Add(Field(customer ? "Sales order" : "Goods receipt", _source));
        root.Children.Add(Field("Product", _product));
        root.Children.Add(Field("Quantity", _quantity));
        if (customer) root.Children.Add(Field("Disposition", _disposition));
        root.Children.Add(Field("Storage location", _location));
        root.Children.Add(Field("Condition", _condition));
        root.Children.Add(Field("Reason", _reason));
        root.Children.Add(Field("Lot / batch (if tracked)", _lot));
        root.Children.Add(Field("Serials, comma separated", _serials));
        root.Children.Add(Field("Notes", _notes));
        var add = new Button { Content = "Add line and save when finished", Margin = new Thickness(0, 4, 0, 8), HorizontalAlignment = HorizontalAlignment.Left };
        add.Click += (_, _) => AddLine();
        var save = new Button { Content = "Record return", HorizontalAlignment = HorizontalAlignment.Left };
        save.Click += async (_, _) => await SubmitAsync();
        root.Children.Add(add);
        root.Children.Add(save);
        root.Children.Add(_error);
        Content = new ScrollViewer { Content = root };
        Loaded += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        var documents = await _api.GetListJsonAsync(_customer ? "api/sales-orders" : "api/receipts");
        var items = documents.TryGetProperty("items", out var paged) ? paged : documents;
        foreach (var item in items.EnumerateArray())
        {
            var number = item.TryGetProperty("orderNumber", out var order) ? order.GetString() : item.GetProperty("receiptNumber").GetString();
            _source.Items.Add(new Choice(item.GetProperty("id").GetString()!, number ?? item.GetProperty("id").GetString()!));
        }
        if (_source.Items.Count > 0) _source.SelectedIndex = 0;
        var products = await _api.GetListJsonAsync("api/products");
        var productItems = products.TryGetProperty("items", out var productPage) ? productPage : products;
        foreach (var product in productItems.EnumerateArray())
            _product.Items.Add(new Choice(product.GetProperty("id").GetString()!, $"{product.GetProperty("sku").GetString()} — {product.GetProperty("name").GetString()}"));
        if (_product.Items.Count > 0) _product.SelectedIndex = 0;
        var warehouses = await _api.GetJsonAsync("api/warehouses");
        foreach (var warehouse in warehouses.EnumerateArray())
        {
            if (!warehouse.TryGetProperty("locations", out var locations)) continue;
            foreach (var location in locations.EnumerateArray())
                _location.Items.Add(new Choice(location.GetProperty("id").GetString()!, $"{warehouse.GetProperty("code").GetString()} / {location.GetProperty("code").GetString()}"));
        }
        if (_location.Items.Count > 0) _location.SelectedIndex = 0;
    }

    private void AddLine()
    {
        _error.Text = "";
        if (_product.SelectedItem is not Choice product || !decimal.TryParse(_quantity.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out var quantity) || quantity <= 0)
        {
            _error.Text = "Choose a product and a positive quantity.";
            return;
        }
        var disposition = _customer ? ((DispositionChoice)_disposition.SelectedItem).Value : 4;
        Guid? locationId = _location.SelectedItem is Choice location ? Guid.Parse(location.Id) : null;
        if ((!_customer || disposition == 1) && locationId is null)
        {
            _error.Text = "Choose a storage location.";
            return;
        }
        _lines.Add(new
        {
            productId = Guid.Parse(product.Id),
            quantity,
            reason = _reason.Text.Trim(),
            condition = _condition.Text.Trim(),
            disposition,
            storageLocationId = locationId,
            lotNumber = string.IsNullOrWhiteSpace(_lot.Text) ? null : _lot.Text.Trim(),
            serials = string.IsNullOrWhiteSpace(_serials.Text) ? null : _serials.Text.Trim()
        });
        _error.Text = $"{_lines.Count} line(s) added.";
    }

    private async Task SubmitAsync()
    {
        if (_lines.Count == 0) AddLine();
        if (_lines.Count == 0 || _source.SelectedItem is not Choice source) return;
        try
        {
            await _api.PostJsonAsync("api/returns", new
            {
                type = _customer ? 1 : 2,
                salesOrderId = _customer ? Guid.Parse(source.Id) : (Guid?)null,
                goodsReceiptId = _customer ? (Guid?)null : Guid.Parse(source.Id),
                notes = _notes.Text.Trim(),
                lines = _lines
            });
            DialogResult = true;
        }
        catch (Exception ex) { _error.Text = ex.Message; }
    }

    private static StackPanel Field(string label, Control input)
    {
        input.Margin = new Thickness(0, 4, 0, 8);
        return new StackPanel { Children = { new TextBlock { Text = label }, input } };
    }

    private sealed record Choice(string Id, string Label);
    private sealed record DispositionChoice(int Value, string Label)
    {
        public override string ToString() => Label;
    }
}

public sealed class BlindCountWindow : Window
{
    private readonly ApiClient _api;
    private readonly Guid _countId;
    private readonly StackPanel _lines = new();
    private readonly TextBlock _error = new() { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap };
    private readonly List<CountEditor> _editors = new();
    private bool _review;

    public BlindCountWindow(ApiClient api, Guid countId)
    {
        _api = api;
        _countId = countId;
        Title = "Blind stock count";
        Width = 760;
        Height = 680;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel { Margin = new Thickness(16) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        var save = new Button { Content = "Save counts", Margin = new Thickness(0, 0, 8, 0) };
        save.Click += async (_, _) => await SaveAsync(post: false);
        var post = new Button { Content = "Save reasons and post" };
        post.Click += async (_, _) => await SaveAsync(post: true);
        buttons.Children.Add(save);
        buttons.Children.Add(post);
        DockPanel.SetDock(buttons, Dock.Bottom);
        var header = new TextBlock { Text = "Enter the physical quantity. The book quantity stays hidden until variance review.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(_error, Dock.Top);
        root.Children.Add(buttons);
        root.Children.Add(header);
        root.Children.Add(_error);
        root.Children.Add(new ScrollViewer { Content = _lines });
        Content = root;
        Loaded += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        var detail = await _api.GetJsonAsync($"api/stock-counts/{_countId}");
        var status = detail.GetProperty("status").GetString();
        _review = !string.Equals(status, "Counting", StringComparison.OrdinalIgnoreCase);
        Title = _review ? "Variance review" : "Blind stock count";
        foreach (var line in detail.GetProperty("lines").EnumerateArray())
        {
            var counted = new TextBox { Width = 90, Text = line.TryGetProperty("countedQuantity", out var existing) && existing.ValueKind == JsonValueKind.Number ? existing.GetDecimal().ToString(CultureInfo.InvariantCulture) : "" };
            var reason = new TextBox { Width = 220 };
            if (line.TryGetProperty("varianceReason", out var varianceReason) && varianceReason.ValueKind == JsonValueKind.String)
                reason.Text = varianceReason.GetString();
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            row.Children.Add(new TextBlock { Text = $"{line.GetProperty("sku").GetString()}  {line.GetProperty("location").GetString()}", Width = 240, VerticalAlignment = VerticalAlignment.Center });
            if (_review && line.TryGetProperty("systemQuantity", out var system) && system.ValueKind == JsonValueKind.Number)
                row.Children.Add(new TextBlock { Text = $"Book {system.GetDecimal()}", Width = 90, VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(counted);
            if (_review) row.Children.Add(reason);
            _lines.Children.Add(row);
            _editors.Add(new CountEditor(line.GetProperty("id").GetGuid(), counted, reason));
        }
    }

    private async Task SaveAsync(bool post)
    {
        _error.Text = "";
        var payload = new List<object>();
        foreach (var editor in _editors)
        {
            if (!decimal.TryParse(editor.Counted.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out var counted) || counted < 0)
            {
                _error.Text = "Every line needs a counted quantity of zero or more.";
                return;
            }
            payload.Add(new { lineId = editor.LineId, countedQuantity = counted, varianceReason = editor.Reason.Text.Trim() });
        }
        try
        {
            await _api.PutJsonAsync($"api/stock-counts/{_countId}/lines", new { lines = payload });
            if (post)
                await _api.PostJsonAsync($"api/stock-counts/{_countId}/post", new { });
            DialogResult = true;
        }
        catch (Exception ex) { _error.Text = ex.Message; }
    }

    private sealed record CountEditor(Guid LineId, TextBox Counted, TextBox Reason);
}

public sealed class CsvImportWindow : Window
{
    private readonly ApiClient _api;
    private readonly TextBox _products = new() { AcceptsReturn = true, Height = 140, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly TextBox _stock = new() { AcceptsReturn = true, Height = 140, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly TextBlock _error = new() { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap };

    public CsvImportWindow(ApiClient api)
    {
        _api = api;
        Title = "Import catalogue and opening stock";
        Width = 760;
        Height = 680;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new StackPanel { Margin = new Thickness(16) };
        root.Children.Add(new TextBlock { Text = "Products: Sku,Name,Category,BaseUnit,CostPrice,ReorderLevel,Barcode,Description,TrackLots,TrackSerials", TextWrapping = TextWrapping.Wrap });
        root.Children.Add(_products);
        var loadProducts = new Button { Content = "Load products CSV", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 12) };
        loadProducts.Click += (_, _) => LoadFile(_products);
        root.Children.Add(loadProducts);
        root.Children.Add(new TextBlock { Text = "Opening stock: Sku,WarehouseCode,LocationCode,Quantity,LotNumber,ExpiryUtc,Serials", TextWrapping = TextWrapping.Wrap });
        root.Children.Add(_stock);
        var loadStock = new Button { Content = "Load opening-stock CSV", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 12) };
        loadStock.Click += (_, _) => LoadFile(_stock);
        root.Children.Add(loadStock);
        var import = new Button { Content = "Import", HorizontalAlignment = HorizontalAlignment.Left };
        import.Click += async (_, _) => await ImportAsync();
        root.Children.Add(import);
        root.Children.Add(_error);
        Content = new ScrollViewer { Content = root };
    }

    private static void LoadFile(TextBox target)
    {
        var dialog = new OpenFileDialog { Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*" };
        if (dialog.ShowDialog() == true)
            target.Text = System.IO.File.ReadAllText(dialog.FileName);
    }

    private async Task ImportAsync()
    {
        _error.Text = "";
        try
        {
            var messages = new List<string>();
            if (!string.IsNullOrWhiteSpace(_products.Text))
                messages.Add(await _api.PostForTextAsync("api/imports/products", new { csv = _products.Text }));
            if (!string.IsNullOrWhiteSpace(_stock.Text))
                messages.Add(await _api.PostForTextAsync("api/imports/opening-stock", new { csv = _stock.Text, batchId = Guid.NewGuid() }));
            if (messages.Count == 0)
            {
                _error.Text = "Paste or load at least one CSV.";
                return;
            }
            _error.Text = string.Join("\n", messages);
            DialogResult = true;
        }
        catch (Exception ex) { _error.Text = ex.Message; }
    }
}
