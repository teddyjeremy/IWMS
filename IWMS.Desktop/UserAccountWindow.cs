using System.Windows;
using System.Windows.Controls;

namespace IWMS.Desktop;

public sealed class UserAccountWindow : Window
{
    private readonly TextBox _first = Field();
    private readonly TextBox _last = Field();
    private readonly TextBox _email = Field();
    private readonly PasswordBox _password = new() { Margin = new Thickness(0, 4, 0, 12), Padding = new Thickness(8) };
    private readonly PasswordBox _confirm = new() { Margin = new Thickness(0, 4, 0, 12), Padding = new Thickness(8) };
    private readonly ComboBox _role = new() { Margin = new Thickness(0, 4, 0, 8) };
    private readonly TextBlock _error = new() { Foreground = System.Windows.Media.Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };

    public string FirstNameText => _first.Text.Trim();
    public string LastNameText => _last.Text.Trim();
    public string EmailText => _email.Text.Trim();
    public string PasswordText => _password.Password;
    public string RoleText => (_role.SelectedItem as string) ?? string.Empty;

    public UserAccountWindow()
    {
        Title = "Create user";
        Width = 480;
        Height = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        Background = System.Windows.Media.Brushes.White;

        foreach (var role in new[]
        {
            "Administrator", "Warehouse Manager", "Purchaser", "Warehouse Operator", "Sales Officer", "Viewer"
        })
            _role.Items.Add(role);
        _role.SelectedIndex = 4;

        var form = new StackPanel { Margin = new Thickness(22) };
        form.Children.Add(Heading("New user"));
        form.Children.Add(Hint("The person can sign in with this email and the temporary password."));
        form.Children.Add(Label("First name"));
        form.Children.Add(_first);
        form.Children.Add(Label("Last name"));
        form.Children.Add(_last);
        form.Children.Add(Label("Work email"));
        form.Children.Add(_email);
        form.Children.Add(Label("Temporary password"));
        form.Children.Add(_password);
        form.Children.Add(Label("Confirm password"));
        form.Children.Add(_confirm);
        form.Children.Add(Label("Role"));
        form.Children.Add(_role);
        form.Children.Add(_error);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "Cancel", IsCancel = true, Margin = new Thickness(0, 0, 8, 0) };
        var save = new Button { Content = "Create user", IsDefault = true };
        if (TryFindResource("PrimaryButton") is Style style)
            save.Style = style;
        save.Click += (_, _) =>
        {
            _error.Text = Validate();
            if (string.IsNullOrEmpty(_error.Text))
                DialogResult = true;
        };
        actions.Children.Add(cancel);
        actions.Children.Add(save);
        form.Children.Add(actions);
        Content = new ScrollViewer { Content = form };
    }

    public static string? PromptRole(Window owner, string? current)
    {
        var dialog = new Window
        {
            Title = "Change role",
            Owner = owner,
            Width = 420,
            Height = 220,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Background = System.Windows.Media.Brushes.White
        };
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock { Text = "Choose one role", FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 10) });
        var combo = new ComboBox();
        foreach (var role in new[] { "Administrator", "Warehouse Manager", "Purchaser", "Warehouse Operator", "Sales Officer", "Viewer" })
            combo.Items.Add(role);
        combo.SelectedItem = combo.Items.Cast<object>().FirstOrDefault(item => string.Equals(item as string, current, StringComparison.OrdinalIgnoreCase)) ?? combo.Items[0];
        panel.Children.Add(combo);
        var save = new Button { Content = "Save role", HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0), IsDefault = true };
        save.Click += (_, _) => dialog.DialogResult = true;
        panel.Children.Add(save);
        dialog.Content = panel;
        return dialog.ShowDialog() == true ? combo.SelectedItem as string : null;
    }

    private string Validate()
    {
        if (FirstNameText.Length == 0 || LastNameText.Length == 0)
            return "Enter a first name and a last name.";
        if (!EmailText.Contains('@') || EmailText.Contains(' '))
            return "Enter a valid work email.";
        if (PasswordText.Length < 10 || !PasswordText.Any(char.IsLetter) || !PasswordText.Any(char.IsDigit))
            return "Password must be at least 10 characters and contain letters and digits.";
        if (PasswordText != _confirm.Password)
            return "The passwords do not match.";
        if (string.IsNullOrWhiteSpace(RoleText))
            return "Choose a role.";
        return string.Empty;
    }

    private static TextBox Field() => new() { Margin = new Thickness(0, 4, 0, 12) };
    private static TextBlock Label(string text) => new() { Text = text, FontWeight = FontWeights.SemiBold };
    private static TextBlock Heading(string text) => new() { Text = text, FontSize = 20, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 4) };
    private static TextBlock Hint(string text) => new() { Text = text, Foreground = System.Windows.Media.Brushes.Gray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14) };
}
