using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using RHI.Linux.Core;

namespace RHI.Linux;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            if (args.Length > 0) return CommandLine.Run(args).GetAwaiter().GetResult();
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            RenoDXCommander.Services.CrashReporter.Log(ex.ToString());
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }
    // A settings utility does not need GPU rendering. Software avoids black windows
    // on NVIDIA + XWayland/HDR desktops and leaves the GPU available for the game.
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect()
        .With(new X11PlatformOptions { RenderingMode = [X11RenderingMode.Software] })
        .WithInterFont().LogToTrace();
}

public sealed class App : Application
{
    public override void Initialize()
    {
        RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
        Styles.Add(new FluentTheme());
        Styles.Add(new Avalonia.Markup.Xaml.Styling.StyleInclude(new Uri("avares://RHI.Linux/")) { Source = new Uri("avares://RHI.Linux/Theme.axaml") });
    }
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) desktop.MainWindow = new MainWindow();
        base.OnFrameworkInitializationCompleted();
    }
}
