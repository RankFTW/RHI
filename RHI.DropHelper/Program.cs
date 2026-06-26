using System;
using System.Drawing;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace RHI.DropHelper;

static class Program
{
    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        IntPtr parentHwnd = IntPtr.Zero;
        string? watchFolder = null;
        var hwndFile = Path.Combine(Path.GetTempPath(), "rhi_drop_hwnd.txt");
        if (File.Exists(hwndFile))
        {
            try
            {
                var lines = File.ReadAllLines(hwndFile);
                if (lines.Length > 0 && long.TryParse(lines[0].Trim(), out var v))
                    parentHwnd = new IntPtr(v);
                if (lines.Length > 1 && !string.IsNullOrWhiteSpace(lines[1]))
                    watchFolder = lines[1].Trim();
            }
            catch { }
        }

        Application.Run(new DropOverlayForm(parentHwnd, watchFolder));
    }
}

class DropOverlayForm : Form
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? cls, string title);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT r);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    private IntPtr _parentHwnd;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly HttpClient _http = new();
    private readonly string _downloadDir;

    public DropOverlayForm(IntPtr parentHwnd, string? watchFolder)
    {
        _parentHwnd = parentHwnd;
        _downloadDir = watchFolder
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        Directory.CreateDirectory(_downloadDir);

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = false;
        BackColor = Color.Black;
        Opacity = 0.01;
        StartPosition = FormStartPosition.Manual;
        Size = new Size(200, 50);
        Text = "RHI.DropHelper";
        AllowDrop = true;

        DragEnter += OnDragEnter;
        DragDrop += OnDragDrop;

        _timer = new System.Windows.Forms.Timer { Interval = 500 };
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (_parentHwnd == IntPtr.Zero) _parentHwnd = FindWindow(null, "RHI");
        SyncPosition();
    }

    private void Tick()
    {
        if (_parentHwnd == IntPtr.Zero) { _parentHwnd = FindWindow(null, "RHI"); return; }
        if (!IsWindow(_parentHwnd)) { Application.Exit(); return; }
        SyncPosition();
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;
    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private static readonly IntPtr HWND_NOTOPMOST = new(-2);

    private void SyncPosition()
    {
        if (_parentHwnd == IntPtr.Zero) return;
        if (IsIconic(_parentHwnd)) { if (Visible) Hide(); return; }

        if (!Visible) Show();
        if (!GetWindowRect(_parentHwnd, out var r)) return;
        int x = r.Left + 12, y = r.Top + 38;
        if (Left != x || Top != y) Location = new Point(x, y);

        // Only bring to front when RHI is the foreground window
        var fg = GetForegroundWindow();
        if (fg == _parentHwnd || fg == this.Handle)
        {
            SetWindowPos(this.Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            SetWindowPos(this.Handle, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }
    }

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        if (e.Data == null) { e.Effect = DragDropEffects.None; return; }
        if (e.Data.GetDataPresent(DataFormats.FileDrop) ||
            e.Data.GetDataPresent(DataFormats.UnicodeText) ||
            e.Data.GetDataPresent(DataFormats.Text))
            e.Effect = DragDropEffects.Copy;
        else
            e.Effect = DragDropEffects.None;
    }

    private async void OnDragDrop(object? sender, DragEventArgs e)
    {
        if (e.Data == null) return;

        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files != null)
                foreach (var f in files)
                {
                    try
                    {
                        var dest = Path.Combine(_downloadDir, Path.GetFileName(f));
                        if (File.Exists(dest)) File.Delete(dest);
                        File.Copy(f, dest);
                    }
                    catch { }
                }
            return;
        }

        var text = e.Data.GetData(DataFormats.UnicodeText) as string
                ?? e.Data.GetData(DataFormats.Text) as string;
        if (string.IsNullOrWhiteSpace(text)) return;

        foreach (var line in text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var url = line.Trim();
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) continue;
            if (uri.Scheme != "https" && uri.Scheme != "http") continue;
            try
            {
                var fileName = Path.GetFileName(uri.AbsolutePath);
                if (string.IsNullOrEmpty(fileName)) fileName = "download";
                foreach (var c in Path.GetInvalidFileNameChars()) fileName = fileName.Replace(c, '_');
                var dest = Path.Combine(_downloadDir, fileName);
                if (File.Exists(dest)) File.Delete(dest);
                using var resp = await _http.GetAsync(uri);
                resp.EnsureSuccessStatusCode();
                await File.WriteAllBytesAsync(dest, await resp.Content.ReadAsByteArrayAsync());
            }
            catch { }
        }
    }
}
