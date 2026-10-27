using System.Windows;
using System.Windows.Media;

namespace RenoDXdbEditor;

public partial class DiffWindow : Window
{
    public enum DiffMode { ReviewRemote, PreviewPush }

    public bool UserChoseOverwrite { get; private set; }

    public DiffWindow(string fileName, string? oldText, string newText,
                      DiffMode mode = DiffMode.ReviewRemote)
    {
        InitializeComponent();
        FileNameLabel.Text = $"File: {fileName}";
        BuildDiff(oldText ?? "", newText);

        if (mode == DiffMode.PreviewPush)
        {
            Title = "Preview Push";
            HeaderLabel.Text = "Review your changes before pushing to GitHub.";
            HeaderLabel.Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0xCC, 0x77));
            OverwriteBtn.Content = "↑ Push";
            // Flip legend: local is "before", pushed is "after"
            AddedLegend.Text    = "Added (new)";
            RemovedLegend.Text  = "Removed";
        }
    }

    private void BuildDiff(string oldText, string newText)
    {
        const int Context = 3; // lines of context around each changed block

        var lines = DbSyncService.ComputeDiff(oldText, newText);

        int added   = lines.Count(l => l.Kind == DbSyncService.DiffKind.Added);
        int removed = lines.Count(l => l.Kind == DbSyncService.DiffKind.Removed);
        SummaryLabel.Text = $"+{added} added  −{removed} removed";

        // Build a set of line indices that should be visible (changed lines ± context)
        var visible = new HashSet<int>();
        for (int i = 0; i < lines.Count; i++)
        {
            if (lines[i].Kind != DbSyncService.DiffKind.Same)
            {
                for (int c = Math.Max(0, i - Context); c <= Math.Min(lines.Count - 1, i + Context); c++)
                    visible.Add(c);
            }
        }

        // If nothing changed at all, show a brief message
        if (visible.Count == 0)
        {
            DiffList.ItemsSource = new[] { DiffLineVm.MakeSeparator("No changes") };
            return;
        }

        var items = new List<DiffLineVm>();
        int skipped = 0;
        for (int i = 0; i < lines.Count; i++)
        {
            if (!visible.Contains(i))
            {
                skipped++;
            }
            else
            {
                if (skipped > 0)
                {
                    items.Add(DiffLineVm.MakeSeparator($"  ···  {skipped} unchanged line{(skipped == 1 ? "" : "s")}  ···"));
                    skipped = 0;
                }
                items.Add(new DiffLineVm(lines[i]));
            }
        }
        if (skipped > 0)
            items.Add(DiffLineVm.MakeSeparator($"  ···  {skipped} unchanged line{(skipped == 1 ? "" : "s")}  ···"));

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
    private static readonly SolidColorBrush SameFg       = new(Color.FromRgb(0xBB, 0xBB, 0xCC));
    private static readonly SolidColorBrush AddedFg      = new(Color.FromRgb(0x88, 0xEE, 0x88));
    private static readonly SolidColorBrush RemovedFg    = new(Color.FromRgb(0xEE, 0x88, 0x88));
    private static readonly SolidColorBrush SeparatorFg  = new(Color.FromRgb(0x66, 0x66, 0x88));
    private static readonly SolidColorBrush SameBg       = new(Color.FromRgb(0x0F, 0x0F, 0x1E));
    private static readonly SolidColorBrush AddedBg      = new(Color.FromRgb(0x1A, 0x33, 0x1A));
    private static readonly SolidColorBrush RemovedBg    = new(Color.FromRgb(0x33, 0x1A, 0x1A));
    private static readonly SolidColorBrush SeparatorBg  = new(Color.FromRgb(0x14, 0x14, 0x28));
    private static readonly SolidColorBrush AddedGutter   = new(Color.FromRgb(0x55, 0xCC, 0x77));
    private static readonly SolidColorBrush RemovedGutter = new(Color.FromRgb(0xEE, 0x55, 0x55));
    private static readonly SolidColorBrush SameGutter    = new(Color.FromRgb(0x55, 0x55, 0x77));

    public string DisplayText  { get; }
    public Brush  FgColor      { get; }
    public Brush  BgColor      { get; }
    public string Gutter       { get; }
    public Brush  GutterColor  { get; }
    public bool   IsSeparator  { get; }

    public DiffLineVm(DbSyncService.DiffLine line)
    {
        DisplayText = line.Text;
        IsSeparator = false;
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

    private DiffLineVm(string text)
    {
        DisplayText = text;
        IsSeparator = true;
        FgColor     = SeparatorFg;
        BgColor     = SeparatorBg;
        Gutter      = "…";
        GutterColor = SeparatorFg;
    }

    public static DiffLineVm MakeSeparator(string text) => new(text);
}
