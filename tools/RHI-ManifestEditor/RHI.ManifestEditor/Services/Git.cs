using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace RHI.ManifestEditor.Services;

public sealed record GitResult(int Code, string Out, string Err)
{
    public bool Ok => Code == 0;
    public string All => (Out + "\n" + Err).Trim();
}

/// <summary>Runs git.exe (from PATH) with the user's own git config and credentials.</summary>
public static class Git
{
    public static Task<GitResult> Run(string repo, params string[] args) => Run(repo, null, args);

    public static Task<GitResult> Run(string repo, IDictionary<string, string>? env, params string[] args) => Task.Run(() =>
    {
        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = repo,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";  // fail instead of waiting for a password prompt nobody can see
        if (env != null) foreach (var (k, v) in env) psi.Environment[k] = v;
        try
        {
            using var p = Process.Start(psi)!;
            var stdout = p.StandardOutput.ReadToEndAsync();
            var stderr = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(180_000))
            {
                try { p.Kill(true); } catch (InvalidOperationException) { /* already exited */ }
                return new GitResult(-1, "", $"git {string.Join(' ', args)} timed out.");
            }
            return new GitResult(p.ExitCode, stdout.Result, stderr.Result);
        }
        catch (Win32Exception ex)
        {
            return new GitResult(-1, "", $"Couldn't run git: {ex.Message}. Is Git installed and on PATH?");
        }
    });

    /// <summary>Runs a command that isn't git (e.g. python) the same way.</summary>
    public static Task<GitResult> RunTool(string dir, string exe, params string[] args) => Task.Run(() =>
    {
        var psi = new ProcessStartInfo(exe)
        {
            WorkingDirectory = dir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        try
        {
            using var p = Process.Start(psi)!;
            var stdout = p.StandardOutput.ReadToEndAsync();
            var stderr = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(120_000))
            {
                try { p.Kill(true); } catch (InvalidOperationException) { /* already exited */ }
                return new GitResult(-1, "", $"{exe} timed out.");
            }
            return new GitResult(p.ExitCode, stdout.Result, stderr.Result);
        }
        catch (Win32Exception)
        {
            return new GitResult(-2, "", $"{exe} wasn't found.");  // -2 = not installed
        }
    });
}

/// <summary>Adds a line under "### Manifest Updates" in the newest version of RHI_PatchNotes.md.</summary>
public static class PatchNotes
{
    public static string RelPath => "RenoDXCommander/RHI_PatchNotes.md";

    /// <summary>The newest version heading (e.g. "v2.8.9 Beta 3"), or null.</summary>
    public static string? TopHeading(string text)
    {
        foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
            if (line.StartsWith("## ", StringComparison.Ordinal)) return line[3..].Trim();
        return null;
    }

    public static string AddManifestUpdate(string text, string note)
    {
        var nl = text.Contains("\r\n") ? "\r\n" : "\n";
        var lines = text.Split(nl).ToList();
        int top = lines.FindIndex(l => l.StartsWith("## ", StringComparison.Ordinal));
        if (top < 0) throw new InvalidOperationException("RHI_PatchNotes.md has no version heading (## ...).");
        int next = lines.FindIndex(top + 1, l => l.StartsWith("## ", StringComparison.Ordinal));
        if (next < 0) next = lines.Count;

        var bullet = "- " + note.Trim().TrimStart('-', ' ');
        int section = -1;
        for (int i = top + 1; i < next; i++)
            if (lines[i].Trim() == "### Manifest Updates") { section = i; break; }

        if (section >= 0)
        {
            // After the section's last non-empty line
            int insert = section + 1;
            for (int i = section + 1; i < next; i++)
            {
                if (lines[i].StartsWith("#", StringComparison.Ordinal)) break;
                if (lines[i].Trim().Length > 0) insert = i + 1;
            }
            lines.Insert(insert, bullet);
        }
        else
        {
            // New section at the end of the version, before its trailing blank lines
            int insert = next;
            while (insert - 1 > top && lines[insert - 1].Trim().Length == 0) insert--;
            lines.InsertRange(insert, new[] { "", "### Manifest Updates", bullet });
        }
        return string.Join(nl, lines);
    }
}
