using System.Windows;
using IWMS.Desktop.Services;

namespace IWMS.Desktop;

public partial class SupplierWindow : Window
{
    private readonly ApiClient _api;

    public SupplierWindow(ApiClient api)
    {
        InitializeComponent();
        _api = api;
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = string.Empty;
        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            ErrorText.Text = "Supplier name is required.";
            return;
        }

        try
        {
            await _api.PostJsonAsync("api/suppliers", new
            {
                name = NameBox.Text.Trim(),
                contactPerson = ContactBox.Text.Trim(),
                email = EmailBox.Text.Trim(),
                phone = PhoneBox.Text.Trim(),
                address = AddressBox.Text.Trim(),
                isActive = ActiveBox.IsChecked == true,
                version = 0
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
