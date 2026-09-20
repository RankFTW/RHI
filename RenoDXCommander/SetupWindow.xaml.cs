// SetupWindow.xaml.cs — First-launch setup window.
// Shown before MainWindow on first launch to ask the user how they want ReShade managed.
// UI is built imperatively (same pattern as DetailPanelBuilder) — no separate XAML template.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using RenoDXCommander.Services;
using WinRT.Interop;

namespace RenoDXCommander;

/// <summary>
/// First-launch setup window. Asks the user whether RHI should manage ReShade
/// automatically or leave it alone. Invokes <see cref="OnComplete"/> with the choice
/// and then closes itself.
/// </summary>
public sealed partial class SetupWindow : Window
{
    /// <summary>
    /// Called after the user clicks either button.
    /// <c>true</c> = "Manage ReShade for me", <c>false</c> = "I'll manage it myself".
    /// </summary>
    public Action<bool>? OnComplete { get; set; }

    public SetupWindow()
    {
        InitializeComponent();

        Title = "Установка RHI";

        // Size and position — scale by display DPI so window is correct at any Windows scaling setting
        var hwndForDpi = WindowNative.GetWindowHandle(this);
        uint dpi = NativeInterop.GetDpiForWindow(hwndForDpi);
        double dpiScale = dpi / 96.0;
        int logicalW = 600, logicalH = 620;
        AppWindow.Resize(new Windows.Graphics.SizeInt32(
            (int)(logicalW * dpiScale),
            (int)(logicalH * dpiScale)));
        CenterOnPrimaryDisplay();

        // Dark title bar — same as MainWindow
        var hwnd = WindowNative.GetWindowHandle(this);
        NativeInterop.EnableDarkTitleBar(hwnd);

        if (AppWindow.TitleBar is { } titleBar)
        {
            var res = Application.Current.Resources;
            titleBar.BackgroundColor               = (Windows.UI.Color)res["TitleBarBackground"];
            titleBar.ForegroundColor               = (Windows.UI.Color)res["TitleBarForeground"];
            titleBar.InactiveBackgroundColor       = (Windows.UI.Color)res["TitleBarInactiveBackground"];
            titleBar.InactiveForegroundColor       = (Windows.UI.Color)res["TitleBarInactiveForeground"];
            titleBar.ButtonBackgroundColor         = (Windows.UI.Color)res["TitleBarButtonBackground"];
            titleBar.ButtonForegroundColor         = (Windows.UI.Color)res["TitleBarButtonForeground"];
            titleBar.ButtonHoverBackgroundColor    = (Windows.UI.Color)res["TitleBarButtonHoverBackground"];
            titleBar.ButtonHoverForegroundColor    = (Windows.UI.Color)res["TitleBarButtonHoverForeground"];
            titleBar.ButtonPressedBackgroundColor  = (Windows.UI.Color)res["TitleBarButtonPressedBackground"];
            titleBar.ButtonPressedForegroundColor  = (Windows.UI.Color)res["TitleBarButtonPressedForeground"];
            titleBar.ButtonInactiveBackgroundColor = (Windows.UI.Color)res["TitleBarButtonInactiveBackground"];
            titleBar.ButtonInactiveForegroundColor = (Windows.UI.Color)res["TitleBarButtonInactiveForeground"];
        }

        // Prevent resizing
        AppWindow.SetPresenter(Microsoft.UI.Windowing.AppWindowPresenterKind.Overlapped);
        if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter overlapped)
        {
            overlapped.IsResizable  = false;
            overlapped.IsMaximizable = false;
        }

        BuildContent();
    }

    private void CenterOnPrimaryDisplay()
    {
        try
        {
            var displayArea = Microsoft.UI.Windowing.DisplayArea.Primary;
            var workArea = displayArea.WorkArea;
            var winSize = AppWindow.Size;
            int x = workArea.X + (workArea.Width  - winSize.Width)  / 2;
            int y = workArea.Y + (workArea.Height - winSize.Height) / 2;
            AppWindow.Move(new Windows.Graphics.PointInt32(x, y));
        }
        catch { /* non-critical */ }
    }

    private void BuildContent()
    {
        var root = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 16,
            Padding = new Thickness(36, 32, 36, 32),
        };

        // ── Title ──
        root.Children.Add(new TextBlock
        {
            Text = "Добро пожаловать в RHI",
            FontSize = 22,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush),
        });

        // ── Description ──
        const string description =
            "RHI умеет автоматически управлять ReShade во всех ваших играх. Это включает:\n\n" +
            "  - Устанавливает и обновляет ReShade для каждой игры в один клик\n" +
            "  - Подбирает правильное имя DLL для каждой игры в зависимости от её требований\n" +
            "  - Управляет наборами шейдеров глобально — установите один раз, они развернутся во все игры автоматически\n" +
            "  - Создаёт резервные копии шейдеров в папках игр перед тем, как взять их под контроль\n" +
            "  - Синхронизирует ReShade при установке RenoDX, OptiScaler и других компонентов\n\n" +
            "Если у вас уже своя настройка ReShade — особые наборы шейдеров, вручную подобранные конфиги, " +
            "или любую удобную версию — выберите «Настрою сам», и RHI вообще не будет трогать ReShade.";

        root.Children.Add(new TextBlock
        {
            Text = description,
            FontSize = 13,
            Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 20,
        });

        // ── Spacer ──
        root.Children.Add(new Border { Height = 4 });

        // ── Separator ──
        root.Children.Add(UIFactory.MakeSeparator());

        // ── Buttons ──
        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 10,
            Margin = new Thickness(0, 4, 0, 0),
        };

        // "Manage ReShade for me" — accent blue style
        var manageBtn = new Button
        {
            Content = "Пусть ReShade управляет RHI",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            FontSize = 14,
            Padding = new Thickness(12, 10, 12, 10),
            Background = UIFactory.Brush(ResourceKeys.AccentBlueBgBrush),
            Foreground = UIFactory.Brush(ResourceKeys.AccentBlueBrush),
            BorderBrush = UIFactory.Brush(ResourceKeys.AccentBlueBorderBrush),
            CornerRadius = new CornerRadius(6),
        };
        VisualStateManager.GoToState(manageBtn, "Normal", false);

        var selfBtn = new Button
        {
            Content = "Настрою сам",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            FontSize = 14,
            Padding = new Thickness(12, 10, 12, 10),
            Background = UIFactory.Brush(ResourceKeys.SurfaceRaisedBrush),
            Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
            BorderBrush = UIFactory.Brush(ResourceKeys.BorderDefaultBrush),
            CornerRadius = new CornerRadius(6),
        };
        VisualStateManager.GoToState(selfBtn, "Normal", false);

        manageBtn.Click += (_, _) => Complete(true);
        selfBtn.Click   += (_, _) => Complete(false);

        buttonPanel.Children.Add(manageBtn);
        buttonPanel.Children.Add(selfBtn);
        root.Children.Add(buttonPanel);

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = root,
        };

        RootContent.Content = scroll;
    }

    private void Complete(bool manageReShade)
    {
        // Defer off the button's Click handler stack to avoid an access violation
        // when the callback closes this window while still inside event processing.
        DispatcherQueue.TryEnqueue(() =>
        {
            try { OnComplete?.Invoke(manageReShade); }
            catch (Exception ex) { CrashReporter.Log($"[SetupWindow.Complete] OnComplete threw — {ex.Message}"); }
        });
    }
}
