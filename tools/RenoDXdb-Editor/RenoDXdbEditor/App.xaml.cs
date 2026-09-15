namespace RenoDXdbEditor;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        // Catch any unhandled exception and show it rather than silently dying
        DispatcherUnhandledException += (_, ex) =>
        {
            System.Windows.MessageBox.Show(
                $"Unhandled error:\n\n{ex.Exception.GetType().Name}: {ex.Exception.Message}\n\n{ex.Exception.StackTrace}",
                "RenoDXdb Editor — Startup Error",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
            ex.Handled = true;
        };

        base.OnStartup(e);
    }
}
