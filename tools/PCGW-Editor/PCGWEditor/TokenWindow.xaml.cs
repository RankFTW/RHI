using System.Windows;

namespace PCGWEditor;

public partial class TokenWindow : Window
{
    public string Token => TokenBox.Text.Trim();

    public TokenWindow(string currentToken)
    {
        InitializeComponent();
        TokenBox.Text = currentToken;
        TokenBox.Focus();
        TokenBox.SelectAll();
    }

    private void Save_Click(object sender, RoutedEventArgs e)   { DialogResult = true;  Close(); }
    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
}
