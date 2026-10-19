using System.Windows;
using IWMS.Desktop.Services;

namespace IWMS.Desktop;

public partial class SignInWindow : Window
{
    private readonly ApiClient _api;

    public SignInWindow(ApiClient api)
    {
        InitializeComponent();
        _api = api;
        ApiUrlBox.Text = api.BaseUrl;
    }

    private async void SignIn_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = string.Empty;
        try
        {
            _api.SetBaseUrl(ApiUrlBox.Text.Trim());
            await _api.SignInAsync(EmailBox.Text.Trim(), PasswordBox.Password);
            DialogResult = true;
        }
        catch (Exception ex)
        {
            ErrorText.Text = ex.Message;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}