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
    private readonly TrackBar _cooldown = new() { Minimum = 30, Maximum = 1500, Value = 300, Width = 130, TickFrequency = 300 };
    private readonly TrackBar _support = new() { Minimum = 2, Maximum = 20, Value = 4, Width = 90, TickFrequency = 2 };
    private readonly TrackBar _glance = new() { Minimum = 10, Maximum = 40, Value = 25, Width = 110, TickFrequency = 5 };   // GlanceTrigger = value/10 (low = glances more)
    private readonly TrackBar _skin = new() { Minimum = 0, Maximum = 100, Value = 25, Width = 110, TickFrequency = 25 };     // SkinSuppress = value/100 (live-tuned default)
    private readonly Label _knobs = new() { AutoSize = true, ForeColor = Color.Gainsboro };
    private readonly Label _counters = new() { AutoSize = true, ForeColor = Color.DarkSeaGreen };
    private readonly Button _pause = new() { Text = "Pause", Width = 70 };
    private readonly System.Windows.Forms.Timer _redraw = new() { Interval = 66 };

    private MindEngine? _engine;
    private Thread? _thread;
    private Percept _latest;
    private volatile bool _hasFrame;
    private volatile bool _fetching;
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
        foreach (var b in new[] { watch, liveBtn, fetch, stop, _pause })   // white text on a dark button face
        {
            b.ForeColor = Color.White;
            b.BackColor = Color.FromArgb(60, 64, 72);
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderColor = Color.FromArgb(96, 100, 108);
        }
        watch.Click += (_, _) =>
        {
            var src = _source.Text.Trim();
            if (src.StartsWith("http", StringComparison.OrdinalIgnoreCase)) FetchAndWatch(src);   // a URL → fetch first
            else Start(() => _engine!.RunWorld(Path.GetFullPath(src)));
        };
        liveBtn.Click += (_, _) => Start(() => _engine!.RunLive());
        fetch.Click += (_, _) => FetchAndWatch(_source.Text.Trim());
        stop.Click += (_, _) => StopEngine();
        _pause.Click += (_, _) => { if (_engine is { } e) { e.Paused = !e.Paused; _pause.Text = e.Paused ? "Resume" : "Pause"; } };
        _cooldown.Scroll += (_, _) => ApplyKnobs();
        _support.Scroll += (_, _) => ApplyKnobs();
        _glance.Scroll += (_, _) => ApplyKnobs();
        _skin.Scroll += (_, _) => ApplyKnobs();

        foreach (Control c in new Control[] {
            L("source:"), _source, watch, liveBtn, fetch, stop, _pause,
            L(" speak-gap:"), _cooldown, L("support:"), _support,
            L("glance-thresh:"), _glance, L("ignore-skin:"), _skin, _knobs, _counters })
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
        if (_engine is { } e)
        {
            e.SpeakCooldownTicks = _cooldown.Value; e.RecallSupport = _support.Value;
            e.GlanceTrigger = _glance.Value / 10f; e.SkinSuppress = _skin.Value / 100f;
        }
        _knobs.Text = $"  gap {_cooldown.Value} · support {_support.Value} · glance {_glance.Value / 10f:F1} · skin {_skin.Value}%";
    }

    // Must be called on the UI thread — it reads the knob controls before handing off to the engine thread.
    private void Start(Action driver)
    {
        StopEngine();
        _transcript.Clear();
        var stateDir = MindPaths.State;   // repo-root/mind-state — findable, and shared with the console mind
        var cooldown = _cooldown.Value;   // read UI controls HERE, on the UI thread
        var support = _support.Value;
        var glance = _glance.Value / 10f;
        var skin = _skin.Value / 100f;
        _thread = new Thread(() =>
        {
            _engine = new MindEngine(stateDir) { SpeakCooldownTicks = cooldown, RecallSupport = support, GlanceTrigger = glance, SkinSuppress = skin };
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

    // Complete videos in the download folder (exclude yt-dlp's ".fNNN.mp4" merge fragments).
    private static string[] CompleteVideos(string folder) => Directory.Exists(folder)
        ? Directory.GetFiles(folder, "*.mp4").Where(f => !Path.GetFileNameWithoutExtension(f).Contains(".f")).ToArray()
        : [];

    // Fetch a URL/playlist with the yt-dlp wrapper; start watching the FIRST video as soon as it lands
    // and keep downloading the rest in the background (the mind re-scans the folder each pass).
    private void FetchAndWatch(string url)
    {
        if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        { Log("put a video/playlist URL (http…) in the source box first, then Fetch."); return; }
        if (_fetching) { Log("already fetching — let it finish, or hit Stop first."); return; }

        var script = new[]
        {
            Path.GetFullPath(Path.Combine("tools", "fetch-playlist.ps1")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "tools", "fetch-playlist.ps1")),
        }.FirstOrDefault(File.Exists);
        if (script is null) { Log("can't find tools/fetch-playlist.ps1 — run the tuner from the repo root."); return; }

        StopEngine();
        _transcript.Clear();
        _fetching = true;
        var outFolder = MindPaths.Downloads;
        var started = false;
        new Thread(() =>
        {
            Log($"fetching {url}");
            Log($"  → {outFolder} (a long playlist indexes fully before the first file appears)...");
            try
            {
                var psi = new ProcessStartInfo("powershell",
                    $"-NoProfile -ExecutionPolicy Bypass -Command \"& '{script}' '{url}' -Out '{outFolder}' *>&1\"")
                { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
                using var p = Process.Start(psi)!;
                // Skip yt-dlp's per-frame progress spam; keep the meaningful lines.
                p.OutputDataReceived += (_, e) => { if (e.Data is { Length: > 0 } d && !(d.Contains("% of") && !d.Contains("100%"))) Log("  " + d.Trim()); };
                p.ErrorDataReceived += (_, e) => { if (e.Data is { Length: > 0 } d) Log("  ! " + d); };
                p.BeginOutputReadLine(); p.BeginErrorReadLine();

                while (!p.HasExited)
                {
                    if (!started && CompleteVideos(outFolder).Length > 0)
                    {
                        started = true;
                        Log("first video ready — watching now while the rest download.");
                        if (!IsDisposed) BeginInvoke(() => Start(() => _engine!.RunWorld(outFolder)));
                    }
                    Thread.Sleep(1000);
                }
                Log($"fetch finished (exit {p.ExitCode}).");
            }
            catch (Exception ex) { Log($"fetch failed to start: {ex.Message}"); _fetching = false; return; }
            _fetching = false;

            var got = CompleteVideos(outFolder).Length;
            if (got == 0) { Log("no videos downloaded — check the URL and the messages above."); return; }
            Log($"got {got} video(s).");
            if (!started && !IsDisposed) BeginInvoke(() => Start(() => _engine!.RunWorld(outFolder)));
        }) { IsBackground = true }.Start();
    }

    private void Log(string line)
    {
        if (IsDisposed) return;
        BeginInvoke(() =>
        {
            _transcript.AppendText(line + Environment.NewLine);
            if (_transcript.Lines.Length > 700) _transcript.Lines = _transcript.Lines[^400..];   // trim rarely, not every line
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
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        g.DrawImage(frame, 0, 0, w, h);
        var sx = w / (float)p.Width; var sy = h / (float)p.Height;
        using var pen = new Pen(Color.Lime, 2);
        g.DrawRectangle(pen, p.BoxX * sx, p.BoxY * sy, p.BoxW * sx, p.BoxH * sy);
        g.DrawString($"attending → object #{p.ObjectUnit}", new Font("Consolas", 10), Brushes.Lime, 6, 6);
        return canvas;
    }
}
