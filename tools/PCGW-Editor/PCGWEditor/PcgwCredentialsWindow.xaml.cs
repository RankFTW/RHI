using System.Windows;

namespace PCGWEditor;

public partial class PcgwCredentialsWindow : Window
{
    public string Username => UsernameBox.Text.Trim();
    public string Password => PasswordBox.Password;

    public PcgwCredentialsWindow(string currentUsername, string currentPassword)
    {
        InitializeComponent();
        UsernameBox.Text     = currentUsername;
        PasswordBox.Password = currentPassword;
        UsernameBox.Focus();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password))
        {
            MessageBox.Show("Both username and password are required.", "Validation",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
}
