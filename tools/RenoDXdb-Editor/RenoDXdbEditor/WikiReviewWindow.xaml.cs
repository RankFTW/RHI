using System.Windows;

namespace RenoDXdbEditor;

/// <summary>
/// One-by-one review dialog for new mods found on the wiki that are not yet in the DB.
/// 
/// Usage:
///   var win = new WikiReviewWindow(newMods) { Owner = this };
///   win.ShowDialog();
///   var accepted = win.AcceptedMods;  // list of GameMod ready to insert
/// </summary>
public partial class WikiReviewWindow : Window
{
    // ── Input ─────────────────────────────────────────────────────────────────

    private readonly List<WikiMod> _pending;

    // ── Output ────────────────────────────────────────────────────────────────

    /// <summary>Mods the user accepted (possibly with name edits). Ready to insert into _allMods.</summary>
    public List<GameMod> AcceptedMods { get; } = new();

    // ── State ─────────────────────────────────────────────────────────────────

    private int  _index       = 0;   // which mod we're currently showing
    private int  _acceptedCount = 0;

    // ── Init ──────────────────────────────────────────────────────────────────

    public WikiReviewWindow(List<WikiMod> newMods)
    {
        InitializeComponent();
        _pending = newMods;
        TotalCountLabel.Text = newMods.Count.ToString();
        ShowCurrent();
    }

    // ── Navigation ────────────────────────────────────────────────────────────

    private void ShowCurrent()
    {
        if (_index >= _pending.Count)
        {
            // All done — show summary and close
            FinishReview();
            return;
        }

        var mod = _pending[_index];

        ProgressLabel.Text = $"Reviewing mod {_index + 1} of {_pending.Count}";
        PositionLabel.Text = $"Mod {_index + 1} / {_pending.Count}";

        // Left panel — read-only wiki view
        WikiName.Text          = mod.Name;
        WikiAuthor.Text        = mod.Author;
        WikiStatus.Text        = mod.Status;
        WikiSnapshotUrl.Text   = NoneIfEmpty(mod.SnapshotUrl);
        WikiSnapshotUrl32.Text = NoneIfEmpty(mod.SnapshotUrl32);
        WikiNexusUrl.Text      = NoneIfEmpty(mod.NexusUrl);
        WikiDiscordUrl.Text    = NoneIfEmpty(mod.DiscordUrl);
        WikiDiscussionUrl.Text = NoneIfEmpty(mod.DiscussionUrl);
        WikiNotes.Text         = NoneIfEmpty(mod.Notes);

        // Right panel — pre-populated, editable before accepting
        DbNameBox.Text          = mod.Name;
        DbAuthorBox.Text        = mod.Author;
        DbSnapshotUrlBox.Text   = mod.SnapshotUrl   ?? "";
        DbSnapshotUrl32Box.Text = mod.SnapshotUrl32 ?? "";
        DbNexusUrlBox.Text      = mod.NexusUrl      ?? "";
        DbDiscordUrlBox.Text    = mod.DiscordUrl     ?? "";
        DbDiscussionUrlBox.Text = mod.DiscussionUrl  ?? "";
        DbNotesBox.Text         = mod.Notes          ?? "";

        // Focus the name box so it's easy to tweak
        DbNameBox.Focus();
        DbNameBox.SelectAll();

        AcceptedCountLabel.Text = _acceptedCount.ToString();
    }

    // ── Button handlers ───────────────────────────────────────────────────────

    private void Accept_Click(object sender, RoutedEventArgs e)
    {
        var name = DbNameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show("Name cannot be empty.", "Validation",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        AcceptedMods.Add(new GameMod
        {
            Name          = name,
            Status        = "Done",
            Author        = DbAuthorBox.Text.Trim(),
            SnapshotUrl   = NullIfEmpty(DbSnapshotUrlBox.Text),
            SnapshotUrl32 = NullIfEmpty(DbSnapshotUrl32Box.Text),
            NexusUrl      = NullIfEmpty(DbNexusUrlBox.Text),
            DiscordUrl    = NullIfEmpty(DbDiscordUrlBox.Text),
            DiscussionUrl = NullIfEmpty(DbDiscussionUrlBox.Text),
            Notes         = NullIfEmpty(DbNotesBox.Text),
        });

        _acceptedCount++;
        _index++;
        ShowCurrent();
    }

    private void Skip_Click(object sender, RoutedEventArgs e)
    {
        _index++;
        ShowCurrent();
    }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        FinishReview();
    }

    // ── Completion ────────────────────────────────────────────────────────────

    private void FinishReview()
    {
        // Update the summary line then close so the caller can inspect AcceptedMods
        DialogResult = AcceptedMods.Count > 0;
        Close();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string NoneIfEmpty(string? s)
        => string.IsNullOrWhiteSpace(s) ? "(none)" : s;

    private static string? NullIfEmpty(string s)
        => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
