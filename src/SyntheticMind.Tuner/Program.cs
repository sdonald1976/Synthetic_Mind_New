using System.Diagnostics;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using SyntheticMind.Runtime;

// A dev tuning window over the same MindEngine: watch what it sees (frame + attention box), read what
// it hears/binds/says (transcript), and turn the live knobs while it runs — the instrument for the
// "sharpness" work. Not pretty, useful. Source can be a folder, the live room, or a YouTube URL/playlist
// (fetched with tools/fetch-playlist.ps1, then watched).
//   dotnet run --project src/SyntheticMind.Tuner

static class Program
{
    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new Tuner());
    }
}

sealed class Tuner : Form
{
    private readonly PictureBox _view = new() { Dock = DockStyle.Fill, BackColor = Color.Black, SizeMode = PictureBoxSizeMode.Normal };
    private readonly TextBox _transcript = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BackColor = Color.FromArgb(20, 22, 26), ForeColor = Color.Gainsboro, Font = new Font("Consolas", 9), BorderStyle = BorderStyle.None };
    private readonly TextBox _source = new() { Width = 420, Text = "temp" };
    private readonly TrackBar _cooldown = new() { Minimum = 30, Maximum = 1500, Value = 300, Width = 160, TickFrequency = 300 };
    private readonly TrackBar _support = new() { Minimum = 2, Maximum = 20, Value = 4, Width = 120, TickFrequency = 2 };
    private readonly Label _knobs = new() { AutoSize = true, ForeColor = Color.Gainsboro };
    private readonly Label _counters = new() { AutoSize = true, ForeColor = Color.DarkSeaGreen };
    private readonly Button _pause = new() { Text = "Pause", Width = 70 };
    private readonly System.Windows.Forms.Timer _redraw = new() { Interval = 66 };

    private MindEngine? _engine;
    private Thread? _thread;
    private Percept _latest;
    private volatile bool _hasFrame;
    private readonly object _gate = new();

    public Tuner()
    {
        Text = "SyntheticMind — tuner";
        Size = new Size(1040, 720);
        BackColor = Color.FromArgb(28, 30, 34);

        var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 560, BackColor = Color.Black };
        split.Panel1.Controls.Add(_view);
        split.Panel2.Controls.Add(_transcript);

        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 96, Padding = new Padding(8), BackColor = Color.FromArgb(36, 38, 44), WrapContents = true };
        var watch = new Button { Text = "Watch folder", Width = 100 };
        var liveBtn = new Button { Text = "Live (cam+mic)", Width = 110 };
        var fetch = new Button { Text = "Fetch URL & watch", Width = 130 };
        var stop = new Button { Text = "Stop", Width = 60 };
        watch.Click += (_, _) => Start(() => _engine!.RunWorld(Path.GetFullPath(_source.Text.Trim())));
        liveBtn.Click += (_, _) => Start(() => _engine!.RunLive());
        fetch.Click += (_, _) => FetchAndWatch(_source.Text.Trim());
        stop.Click += (_, _) => StopEngine();
        _pause.Click += (_, _) => { if (_engine is { } e) { e.Paused = !e.Paused; _pause.Text = e.Paused ? "Resume" : "Pause"; } };
        _cooldown.Scroll += (_, _) => ApplyKnobs();
        _support.Scroll += (_, _) => ApplyKnobs();

        foreach (Control c in new Control[] {
            L("source:"), _source, watch, liveBtn, fetch, stop, _pause,
            L("   speak-gap:"), _cooldown, L("recall-support:"), _support, _knobs, _counters })
            bar.Controls.Add(c);

        Controls.Add(split);
        Controls.Add(bar);
        _redraw.Tick += (_, _) => Redraw();
        _redraw.Start();
        ApplyKnobs();
        FormClosing += (_, _) => StopEngine();
    }

    private static Label L(string t) => new() { Text = t, AutoSize = true, ForeColor = Color.Gray, Margin = new Padding(6, 12, 2, 0) };

    private void ApplyKnobs()
    {
        if (_engine is { } e) { e.SpeakCooldownTicks = _cooldown.Value; e.RecallSupport = _support.Value; }
        _knobs.Text = $"  gap {_cooldown.Value} · support {_support.Value}";
    }

    // Must be called on the UI thread — it reads the knob controls before handing off to the engine thread.
    private void Start(Action driver)
    {
        StopEngine();
        _transcript.Clear();
        var stateDir = Path.GetFullPath("mind-state");
        var cooldown = _cooldown.Value;   // read UI controls HERE, on the UI thread
        var support = _support.Value;
        _thread = new Thread(() =>
        {
            _engine = new MindEngine(stateDir) { SpeakCooldownTicks = cooldown, RecallSupport = support };
            _engine.Log += Log;
            _engine.Perceived += p => { lock (_gate) { _latest = p; _hasFrame = true; } };
            driver();
        }) { IsBackground = true };
        _thread.Start();
    }

    private void StopEngine()
    {
        _engine?.Stop();
        if (_thread is { } t && t.IsAlive && !t.Join(3000)) { /* let it wind down */ }
        _engine = null; _thread = null; _hasFrame = false;
    }

    // Fetch a URL/playlist with the yt-dlp wrapper, streaming progress to the transcript, then watch it.
    private void FetchAndWatch(string url)
    {
        if (string.IsNullOrWhiteSpace(url) || url == "temp") { Log("put a video/playlist URL in the source box first."); return; }
        StopEngine();
        _transcript.Clear();
        var outFolder = Path.GetFullPath("youtube-tuner");
        new Thread(() =>
        {
            Log($"fetching {url} → {outFolder} (yt-dlp)...");
            try
            {
                var script = Path.GetFullPath("tools/fetch-playlist.ps1");
                var psi = new ProcessStartInfo("powershell", $"-ExecutionPolicy Bypass -File \"{script}\" \"{url}\" -Out \"{outFolder}\"")
                { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
                using var p = Process.Start(psi)!;
                p.OutputDataReceived += (_, e) => { if (e.Data is { Length: > 0 }) Log(e.Data); };
                p.ErrorDataReceived += (_, e) => { if (e.Data is { Length: > 0 }) Log(e.Data); };
                p.BeginOutputReadLine(); p.BeginErrorReadLine();
                p.WaitForExit();
            }
            catch (Exception ex) { Log($"fetch failed: {ex.Message}"); return; }
            Log("fetched. now watching.");
            if (!IsDisposed) BeginInvoke(() => Start(() => _engine!.RunWorld(outFolder)));   // Start must run on the UI thread
        }) { IsBackground = true }.Start();
    }

    private void Log(string line)
    {
        if (IsDisposed) return;
        BeginInvoke(() =>
        {
            _transcript.AppendText(line + Environment.NewLine);
            if (_transcript.Lines.Length > 500) _transcript.Text = string.Join(Environment.NewLine, _transcript.Lines[^400..]);
            _transcript.SelectionStart = _transcript.TextLength; _transcript.ScrollToCaret();
        });
    }

    private void Redraw()
    {
        Percept p; bool has;
        lock (_gate) { p = _latest; has = _hasFrame; }
        if (has) { var old = _view.Image; _view.Image = Render(p, _view.ClientSize); old?.Dispose(); }
        if (_engine is { } e) _counters.Text = $"  {e.WordCount} words · {e.ObjectCount} objects · {e.Bindings} bindings · tick {e.Ticks}";
    }

    // Frame (BGR) scaled up, with the attention window drawn as a box.
    private static Bitmap Render(Percept p, Size target)
    {
        using var frame = new Bitmap(p.Width, p.Height, PixelFormat.Format24bppRgb);
        var data = frame.LockBits(new Rectangle(0, 0, p.Width, p.Height), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
        for (var y = 0; y < p.Height; y++) Marshal.Copy(p.Bgr, y * p.Width * 3, data.Scan0 + y * data.Stride, p.Width * 3);
        frame.UnlockBits(data);

        var w = Math.Max(1, target.Width); var h = Math.Max(1, target.Height);
        var canvas = new Bitmap(w, h);
        using var g = Graphics.FromImage(canvas);
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
        g.DrawImage(frame, 0, 0, w, h);
        var sx = w / (float)p.Width; var sy = h / (float)p.Height;
        using var pen = new Pen(Color.Lime, 2);
        g.DrawRectangle(pen, p.BoxX * sx, p.BoxY * sy, p.BoxW * sx, p.BoxH * sy);
        g.DrawString($"attending → object #{p.ObjectUnit}", new Font("Consolas", 10), Brushes.Lime, 6, 6);
        return canvas;
    }
}
