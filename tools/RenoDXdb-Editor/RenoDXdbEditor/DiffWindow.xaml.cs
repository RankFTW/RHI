using System.Windows;
using System.Windows.Media;

namespace RenoDXdbEditor;

public partial class DiffWindow : Window
{
    public bool UserChoseOverwrite { get; private set; }

    public DiffWindow(string fileName, string? oldText, string newText)
    {
        InitializeComponent();
        FileNameLabel.Text = $"File: {fileName}";
        BuildDiff(oldText ?? "", newText);
    }

    private void BuildDiff(string oldText, string newText)
    {
        var lines = DbSyncService.ComputeDiff(oldText, newText);

        int added   = lines.Count(l => l.Kind == DbSyncService.DiffKind.Added);
        int removed = lines.Count(l => l.Kind == DbSyncService.DiffKind.Removed);
        SummaryLabel.Text = $"+{added} added  −{removed} removed  (unchanged lines hidden in colour)";

        var items = lines.Select(l => new DiffLineVm(l)).ToList();
        DiffList.ItemsSource = items;
    }

    private void Overwrite_Click(object sender, RoutedEventArgs e)
    {
        UserChoseOverwrite = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        UserChoseOverwrite = false;
        Close();
    }
}

/// <summary>View-model for a single diff line, providing display properties for binding.</summary>
internal class DiffLineVm
{
    private static readonly SolidColorBrush SameFg    = new(Color.FromRgb(0xBB, 0xBB, 0xCC));
    private static readonly SolidColorBrush AddedFg   = new(Color.FromRgb(0x88, 0xEE, 0x88));
    private static readonly SolidColorBrush RemovedFg = new(Color.FromRgb(0xEE, 0x88, 0x88));
    private static readonly SolidColorBrush SameBg    = new(Color.FromRgb(0x0F, 0x0F, 0x1E));
    private static readonly SolidColorBrush AddedBg   = new(Color.FromRgb(0x1A, 0x33, 0x1A));
    private static readonly SolidColorBrush RemovedBg = new(Color.FromRgb(0x33, 0x1A, 0x1A));
    private static readonly SolidColorBrush AddedGutter   = new(Color.FromRgb(0x55, 0xCC, 0x77));
    private static readonly SolidColorBrush RemovedGutter = new(Color.FromRgb(0xEE, 0x55, 0x55));
    private static readonly SolidColorBrush SameGutter    = new(Color.FromRgb(0x55, 0x55, 0x77));

    public string DisplayText  { get; }
    public Brush  FgColor      { get; }
    public Brush  BgColor      { get; }
    public string Gutter       { get; }
    public Brush  GutterColor  { get; }

    public DiffLineVm(DbSyncService.DiffLine line)
    {
        DisplayText = line.Text;
        switch (line.Kind)
        {
            case DbSyncService.DiffKind.Added:
                FgColor     = AddedFg;
                BgColor     = AddedBg;
                Gutter      = "+";
                GutterColor = AddedGutter;
                break;
            case DbSyncService.DiffKind.Removed:
                FgColor     = RemovedFg;
                BgColor     = RemovedBg;
                Gutter      = "−";
                GutterColor = RemovedGutter;
                break;
            default:
                FgColor     = SameFg;
                BgColor     = SameBg;
                Gutter      = " ";
                GutterColor = SameGutter;
                break;
        }
    }
}
