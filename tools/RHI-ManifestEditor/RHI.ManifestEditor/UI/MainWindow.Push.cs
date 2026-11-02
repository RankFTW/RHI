using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RHI.ManifestEditor.Services;

namespace RHI.ManifestEditor.UI;

/// <summary>Push to GitHub: commit manifest.json (and an optional patch note) and push to main.</summary>
public sealed partial class MainWindow
{
    private bool _pushing;

    private async Task PushAsync()
    {
        if (_doc == null || _dialogOpen || _pushing) return;
        Flush();
        _pushing = true;
        try
        {
            // ── Where and what ─────────────────────────────────────────────────
            var top = await Git.Run(Path.GetDirectoryName(_path)!, "rev-parse", "--show-toplevel");
            if (!top.Ok)
            {
                await ShowMessageAsync("Can't push", $"{_path} isn't inside a git checkout.\n\n{top.All}");
                return;
            }
            var repo = Path.GetFullPath(top.Out.Trim());
            var rel = Path.GetRelativePath(repo, _path).Replace('\\', '/');
            var branch = (await Git.Run(repo, "rev-parse", "--abbrev-ref", "HEAD")).Out.Trim();
            if (branch != "main")
            {
                await ShowMessageAsync("Can't push", $"The checkout is on branch \"{branch}\". Switch to main first: the app reads the manifest from main.");
                return;
            }
            var head = await Git.Run(repo, "show", $"HEAD:{rel}");
            var headText = head.Ok ? head.Out : "";
            var diff = LineDiff.Compute(headText, _doc.Text);
            if (diff.Count == 0)
            {
                await ShowMessageAsync("Nothing to push", "manifest.json is the same as the last commit.");
                return;
            }

            var notesPath = Path.Combine(repo, PatchNotes.RelPath.Replace('/', '\\'));
            string? heading = File.Exists(notesPath) ? PatchNotes.TopHeading(File.ReadAllText(notesPath)) : null;

            // ── Dialog ─────────────────────────────────────────────────────────
            int added = diff.Count(l => l.Kind == DiffKind.Added), removed = diff.Count(l => l.Kind == DiffKind.Removed);
            var message = new TextBox { Text = "Manifest: ", PlaceholderText = "Manifest: what changed", HorizontalAlignment = HorizontalAlignment.Stretch };
            message.Loaded += (_, _) => { message.Focus(FocusState.Programmatic); message.SelectionStart = message.Text.Length; };
            var note = new TextBox { PlaceholderText = "e.g. Fixed the Unreal Engine version not showing for ...", HorizontalAlignment = HorizontalAlignment.Stretch };
            var error = Ui.T("", 13, fg: Ui.Critical);
            error.Visibility = Visibility.Collapsed;

            var panel = Ui.V(10,
                Ui.T($"Changes since the last commit (+{added} / -{removed} lines):", 13, fg: Ui.Secondary),
                DiffView(diff, "", 300),
                Ui.T("Commit message", 14, bold: true),
                message);
            if (heading != null)
                panel.Children.Add(Ui.V(4,
                    Ui.T("Patch note (optional)", 14, bold: true),
                    note,
                    Ui.Help($"Added under \"### Manifest Updates\" in \"{heading}\" of RHI_PatchNotes.md and pushed with the manifest. " +
                            "Leave it empty for housekeeping (re-ordering, typos). If that version has already been released, add a new version heading to the patch notes first.")));
            panel.Children.Add(Ui.T("Pushing to main makes this live for every RHI user. Only manifest.json (and the patch note line) is committed; your other uncommitted work isn't touched.", 13, fg: Ui.Caution));
            panel.Children.Add(error);

            var d = MakeDialog("Push manifest.json to GitHub", new ScrollViewer { Content = panel, MaxHeight = 680 });
            d.PrimaryButtonText = _doc.IsDirty ? "Save, commit and push" : "Commit and push";
            d.CloseButtonText = "Cancel";
            d.DefaultButton = ContentDialogButton.None;
            d.PrimaryButtonClick += (_, args) =>
            {
                var m = message.Text.Trim();
                if (m.Length == 0 || m.TrimEnd(':').Equals("Manifest", StringComparison.OrdinalIgnoreCase))
                {
                    error.Text = "Write a commit message saying what changed.";
                    error.Visibility = Visibility.Visible;
                    args.Cancel = true;
                }
            };
            if (await ShowDialogAsync(d) != ContentDialogResult.Primary) return;
            var commitMessage = message.Text.Trim();
            var noteText = note.Text.Trim();

            // ── Save and check ─────────────────────────────────────────────────
            if (_doc.IsDirty && !await SaveAsync()) return;
            RunChecks();
            var errors = _issues.Where(i => i.Severity == Severity.Error).ToList();
            if (errors.Count > 0)
            {
                await ShowMessageAsync("Not pushed", "The manifest has problems that would break it for users:\n\n" +
                    string.Join("\n\n", errors.Select(e => "- " + e.Message)));
                return;
            }
            var script = Path.Combine(repo, "tools", "check-manifest.py");
            if (File.Exists(script))
            {
                SetStatus("Running check-manifest.py...");
                var py = await Git.RunTool(repo, "python", script);
                if (py.Code == -2) py = await Git.RunTool(repo, "py", script);
                if (py.Code == -2) SetStatus("Python wasn't found, so check-manifest.py was skipped (the editor's own checks passed).");
                else if (!py.Ok)
                {
                    await ShowMessageAsync("Not pushed", "tools/check-manifest.py found problems:\n\n" + py.All);
                    return;
                }
            }

            // ── Commit only these files ────────────────────────────────────────
            SetStatus("Committing...");
            var commit = await CommitFilesAsync(repo, rel, commitMessage, heading != null && noteText.Length > 0 ? noteText : null, notesPath);
            if (commit.Error != null)
            {
                await ShowMessageAsync("Not pushed", commit.Error);
                return;
            }

            // ── Push ───────────────────────────────────────────────────────────
            SetStatus("Pushing...");
            var push = await Git.Run(repo, "push", "origin", "main");
            if (!push.Ok)
            {
                // Usually GitHub has newer commits: rebase onto them and try again
                SetStatus("GitHub has newer commits. Pulling them first...");
                var pull = await Git.Run(repo, "pull", "--rebase", "--autostash", "origin", "main");
                if (!pull.Ok)
                {
                    await Git.Run(repo, "rebase", "--abort");
                    await ShowMessageAsync("Committed but not pushed",
                        $"The commit ({commit.Sha}) was made locally, but pulling GitHub's newer commits failed, so it wasn't pushed. " +
                        "Pull and push from git yourself.\n\n" + push.All + "\n\n" + pull.All);
                    ReloadIfChangedOnDisk();
                    return;
                }
                push = await Git.Run(repo, "push", "origin", "main");
                ReloadIfChangedOnDisk();
                // The rebase gave the commit a new ID
                var rebased = (await Git.Run(repo, "rev-parse", "--short=7", "HEAD")).Out.Trim();
                if (rebased.Length > 0) commit = (rebased, null);
                if (!push.Ok)
                {
                    await ShowMessageAsync("Committed but not pushed",
                        $"The commit ({commit.Sha}) was made locally but the push failed. Push from git yourself.\n\n{push.All}");
                    return;
                }
            }
            SetStatus($"Pushed {commit.Sha}: {commitMessage}. Users get it the next time RHI fetches the manifest.");
            await ShowMessageAsync("Pushed", $"{commitMessage}\n\nCommit {commit.Sha} is on main. RHI picks it up the next time it fetches the manifest (at launch and every 4 hours).");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            App.Log($"[MainWindow.PushAsync] {ex}");
            await ShowMessageAsync("Push failed", ex.Message);
        }
        finally
        {
            _pushing = false;
        }
    }

    /// <summary>
    /// Commits manifest.json as saved (plus a patch note line added to the committed RHI_PatchNotes.md)
    /// on top of HEAD without touching the index or any other file: builds the tree in a temporary index.
    /// The patch note line is also added to the working copy, so local edits there stay uncommitted.
    /// </summary>
    private static async Task<(string? Sha, string? Error)> CommitFilesAsync(string repo, string rel, string message, string? note, string notesPath)
    {
        var oldHead = (await Git.Run(repo, "rev-parse", "HEAD")).Out.Trim();
        var tempIndex = Path.Combine(Path.GetTempPath(), $"rhi-manifest-editor-{Guid.NewGuid():N}.index");
        var env = new Dictionary<string, string> { ["GIT_INDEX_FILE"] = tempIndex };
        string? tempNotes = null;
        try
        {
            async Task<string> Step(GitResult r, string what)
            {
                if (!r.Ok) throw new GitStepException($"{what} failed:\n\n{r.All}");
                return r.Out.Trim();
            }

            await Step(await Git.Run(repo, env, "read-tree", "HEAD"), "Reading the last commit");
            var manifestBlob = await Step(await Git.Run(repo, "hash-object", "-w", "--path", rel, Path.Combine(repo, rel)), "Storing manifest.json");
            await Step(await Git.Run(repo, env, "update-index", "--add", "--cacheinfo", $"100644,{manifestBlob},{rel}"), "Staging manifest.json");

            string? workingNotes = null;
            if (note != null)
            {
                var headNotes = await Git.Run(repo, "show", $"HEAD:{PatchNotes.RelPath}");
                if (!headNotes.Ok) throw new GitStepException($"Reading RHI_PatchNotes.md from the last commit failed:\n\n{headNotes.All}");
                tempNotes = Path.Combine(Path.GetTempPath(), $"rhi-manifest-editor-{Guid.NewGuid():N}.md");
                File.WriteAllText(tempNotes, PatchNotes.AddManifestUpdate(headNotes.Out, note), new UTF8Encoding(false));
                var notesBlob = await Step(await Git.Run(repo, "hash-object", "-w", "--path", PatchNotes.RelPath, tempNotes), "Storing RHI_PatchNotes.md");
                await Step(await Git.Run(repo, env, "update-index", "--add", "--cacheinfo", $"100644,{notesBlob},{PatchNotes.RelPath}"), "Staging RHI_PatchNotes.md");
                workingNotes = PatchNotes.AddManifestUpdate(File.ReadAllText(notesPath), note);
            }

            var tree = await Step(await Git.Run(repo, env, "write-tree"), "Building the commit");
            var sha = await Step(await Git.Run(repo, "commit-tree", tree, "-p", oldHead, "-m", message), "Creating the commit");
            await Step(await Git.Run(repo, "update-ref", "refs/heads/main", sha, oldHead), "Moving main to the new commit");

            // Working copy and real index follow the new commit for these files only
            if (workingNotes != null)
            {
                var bytes = File.ReadAllBytes(notesPath);
                bool bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
                File.WriteAllText(notesPath, workingNotes, new UTF8Encoding(bom));
            }
            var paths = note != null ? new[] { rel, PatchNotes.RelPath } : new[] { rel };
            await Git.Run(repo, new[] { "reset", "-q", "--" }.Concat(paths).ToArray());
            App.Log($"[MainWindow.CommitFilesAsync] {sha} {message}");
            return (sha[..Math.Min(7, sha.Length)], null);
        }
        catch (GitStepException ex)
        {
            return (null, ex.Message);
        }
        finally
        {
            try
            {
                if (File.Exists(tempIndex)) File.Delete(tempIndex);
                if (tempNotes != null && File.Exists(tempNotes)) File.Delete(tempNotes);
            }
            catch (IOException ex) { App.Log($"[MainWindow.CommitFilesAsync] temp cleanup: {ex.Message}"); }
        }
    }

    private sealed class GitStepException(string message) : Exception(message);

    /// <summary>After a pull, reload the file if git changed it and there are no edits.</summary>
    private void ReloadIfChangedOnDisk()
    {
        if (_doc == null || _doc.IsDirty) return;
        try
        {
            var disk = File.ReadAllText(_path);
            if (disk != _doc.SavedText) _doc.Reset(disk);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Json.JsonParseException)
        {
            SetStatus($"Couldn't reload the file: {ex.Message}");
        }
    }
}
