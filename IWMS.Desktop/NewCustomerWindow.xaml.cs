using System.Windows;
using IWMS.Desktop.Services;

namespace IWMS.Desktop;

public partial class NewCustomerWindow : Window
{
    private readonly ApiClient _api;
    private readonly Guid? _customerId;

    public NewCustomerWindow(ApiClient api, Guid? customerId = null)
    {
        InitializeComponent();
        _api = api;
        _customerId = customerId;
        if (_customerId.HasValue)
        {
            Title = "Edit customer";
            HeaderText.Text = "Edit customer";
            SaveButton.Content = "Save changes";
            Loaded += async (_, _) => await LoadCustomerAsync(_customerId.Value);
        }
    }

    private async Task LoadCustomerAsync(Guid customerId)
    {
        try
        {
            var customer = await _api.GetAsync<System.Text.Json.JsonElement>($"api/customers/{customerId}");
            NameBox.Text = customer.GetProperty("name").GetString() ?? "";
            EmailBox.Text = customer.GetProperty("email").GetString() ?? "";
            PhoneBox.Text = customer.GetProperty("phone").GetString() ?? "";
            AddressBox.Text = customer.GetProperty("address").GetString() ?? "";
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
        if (string.IsNullOrWhiteSpace(NameBox.Text) || string.IsNullOrWhiteSpace(EmailBox.Text))
        {
            ErrorText.Text = "Customer name and email are required.";
            return;
        }

        try
        {
            var payload = new
            {
                name = NameBox.Text.Trim(),
                email = EmailBox.Text.Trim(),
                phone = PhoneBox.Text.Trim(),
                address = AddressBox.Text.Trim()
            };
            if (_customerId.HasValue)
                await _api.PutJsonAsync($"api/customers/{_customerId.Value}", payload);
            else
                await _api.PostJsonAsync("api/customers", payload);
            DialogResult = true;
        }
        catch (Exception ex)
        {
            ErrorText.Text = ex.Message;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}