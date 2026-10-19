using System.Windows;
using IWMS.Desktop.Services;

namespace IWMS.Desktop;

public partial class NewCustomerWindow : Window
{
    private readonly ApiClient _api;

    public NewCustomerWindow(ApiClient api)
    {
        InitializeComponent();
        _api = api;
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
            await _api.PostJsonAsync("api/customers", new
            {
                name = NameBox.Text.Trim(),
                email = EmailBox.Text.Trim(),
                phone = PhoneBox.Text.Trim(),
                address = AddressBox.Text.Trim()
            });
            DialogResult = true;
        }
        catch (Exception ex)
        {
            ErrorText.Text = ex.Message;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}