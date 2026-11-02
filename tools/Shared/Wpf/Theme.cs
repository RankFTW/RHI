using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace RhiTools.Theme;

/// <summary>
/// Shared Fluent-style dark theme for the WPF tools (PCGW Editor, RenoDXdb Editor).
/// Colours match the WinUI manifest editor. FluentDark.xaml holds the styles; this file
/// holds the bits XAML can't do: dark title bar, the Windows accent colour and a few
/// attached properties (button icons, text box placeholders).
/// </summary>
public static class Ui
{
    // ── Status colours for code-behind (same values as the XAML brushes) ─────────────
    public static readonly SolidColorBrush Success   = Frozen(0x6C, 0xCB, 0x5F);
    public static readonly SolidColorBrush Caution   = Frozen(0xFC, 0xE1, 0x00);
    public static readonly SolidColorBrush Critical  = Frozen(0xFF, 0x99, 0xA4);
    public static readonly SolidColorBrush Secondary = Frozen(0xC5, 0xC5, 0xC5);

    public const string SuccessHex  = "#6CCB5F";
    public const string CautionHex  = "#FCE100";
    public const string CriticalHex = "#FF99A4";

    private static SolidColorBrush Frozen(byte r, byte g, byte b)
    {
        var br = new SolidColorBrush(Color.FromRgb(r, g, b));
        br.Freeze();
        return br;
    }

    /// <summary>Call from App.OnStartup: swaps the default blue for the user's Windows accent colour.</summary>
    public static void ApplyAccent(Application app)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent");
            if (key?.GetValue("AccentPalette") is not byte[] p || p.Length < 32) return;
            // Palette: Light3, Light2, Light1, Base, Dark1, Dark2, Dark3 (RGBA each).
            // WinUI dark theme uses Light2 for accent fills and Light3 for accent text.
            Color At(int i) => Color.FromRgb(p[i * 4], p[i * 4 + 1], p[i * 4 + 2]);
            var light3 = At(0);
            var light2 = At(1);
            app.Resources["AccentFill"]        = Solid(light2);
            app.Resources["AccentFillHover"]   = Solid(Blend(light2, Color.FromRgb(0x20, 0x20, 0x20), 0.9));
            app.Resources["AccentFillPressed"] = Solid(Blend(light2, Color.FromRgb(0x20, 0x20, 0x20), 0.8));
            app.Resources["AccentText"]        = Solid(light3);
            app.Resources["AccentSelection"]   = Solid(Color.FromArgb(0x66, light2.R, light2.G, light2.B));
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or System.IO.IOException)
        {
            // Keep the default accent colour
        }
    }

    private static SolidColorBrush Solid(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

    private static Color Blend(Color fg, Color bg, double alpha) => Color.FromRgb(
        (byte)(fg.R * alpha + bg.R * (1 - alpha)),
        (byte)(fg.G * alpha + bg.G * (1 - alpha)),
        (byte)(fg.B * alpha + bg.B * (1 - alpha)));

    // ── Dark title bar (set via the ThemedWindow style) ────────────────────────────

    public static readonly DependencyProperty DarkTitleBarProperty = DependencyProperty.RegisterAttached(
        "DarkTitleBar", typeof(bool), typeof(Ui), new PropertyMetadata(false, OnDarkTitleBarChanged));

    public static bool GetDarkTitleBar(DependencyObject o) => (bool)o.GetValue(DarkTitleBarProperty);
    public static void SetDarkTitleBar(DependencyObject o, bool v) => o.SetValue(DarkTitleBarProperty, v);

    private static void OnDarkTitleBarChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Window w || e.NewValue is not true) return;
        if (new WindowInteropHelper(w).Handle != IntPtr.Zero) ApplyDarkTitleBar(w);
        else w.SourceInitialized += (_, _) => ApplyDarkTitleBar(w);
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    private static void ApplyDarkTitleBar(Window w)
    {
        var hwnd = new WindowInteropHelper(w).Handle;
        if (hwnd == IntPtr.Zero) return;
        int on = 1;
        DwmSetWindowAttribute(hwnd, 20, ref on, sizeof(int));        // DWMWA_USE_IMMERSIVE_DARK_MODE
        int caption = 0x00202020;                                     // COLORREF (BGR): matches WindowBg
        DwmSetWindowAttribute(hwnd, 35, ref caption, sizeof(int));   // DWMWA_CAPTION_COLOR (Win11; ignored on Win10)
    }

    // ── Icon: Segoe Fluent Icons glyph shown before a button's text ──────────────────

    public static readonly DependencyProperty IconProperty = DependencyProperty.RegisterAttached(
        "Icon", typeof(string), typeof(Ui), new FrameworkPropertyMetadata(null));

    public static string? GetIcon(DependencyObject o) => (string?)o.GetValue(IconProperty);
    public static void SetIcon(DependencyObject o, string? v) => o.SetValue(IconProperty, v);

    // ── Placeholder: hint text shown in an empty TextBox ────────────────────────────

    public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.RegisterAttached(
        "Placeholder", typeof(string), typeof(Ui), new FrameworkPropertyMetadata(null));

    public static string? GetPlaceholder(DependencyObject o) => (string?)o.GetValue(PlaceholderProperty);
    public static void SetPlaceholder(DependencyObject o, string? v) => o.SetValue(PlaceholderProperty, v);
}
