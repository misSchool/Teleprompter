// Teleprompter for Windows - a small always-on-top, notch-style teleprompter.
// Scrolls your script at a steady, adjustable speed (no voice recognition).
// Built with the C# compiler that ships with Windows (.NET Framework 4.x) - see build.bat.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace Teleprompter
{
    static class Native
    {
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] public static extern bool ReleaseCapture();
        [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] public static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint affinity);
        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        public const int WM_NCLBUTTONDOWN = 0xA1, HTCAPTION = 2, WM_NCHITTEST = 0x84, WM_HOTKEY = 0x312;
        public const uint WDA_NONE = 0, WDA_EXCLUDEFROMCAPTURE = 0x11;
        public const uint MOD_ALT = 1, MOD_CONTROL = 2, MOD_NOREPEAT = 0x4000;
    }

    class Settings
    {
        public const string DefaultScript =
            "Welcome to your Teleprompter!\n\n" +
            "This text scrolls at a steady speed so you can read it while looking at your camera.\n\n" +
            "Press SPACE (or double-click) to start and pause.\n" +
            "Use UP / DOWN arrows to change the speed.\n" +
            "Press R to restart from the top.\n" +
            "Press E to edit your script and settings.\n" +
            "Scroll the mouse wheel to move the text manually.\n" +
            "Ctrl + mouse wheel changes the font size.\n\n" +
            "Drag the window to move it. Drag its edges to resize it.\n" +
            "Right-click for more options.\n\n" +
            "Global shortcuts work even when another app is focused:\n" +
            "Ctrl+Alt+Space  play / pause\n" +
            "Ctrl+Alt+=  faster      Ctrl+Alt+-  slower\n" +
            "Ctrl+Alt+R  restart     Ctrl+Alt+H  show / hide\n\n" +
            "By default this window is hidden from screen sharing and recordings, " +
            "so your audience will not see it.\n\n" +
            "Good luck with your presentation!";

        public string Script = DefaultScript;
        public int Speed = 25;            // 1..100  (pixels per second = Speed * 2)
        public float FontSize = 22f;
        public string FontName = "Segoe UI";
        public int Opacity = 92;          // 30..100 %
        public bool CenterText = true;
        public bool HideFromCapture = true;
        public int X = int.MinValue, Y = int.MinValue, W = 0, H = 0;

        public static string Dir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Teleprompter"); }
        }

        static int I(string v, int def) { int r; return int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out r) ? r : def; }
        static float F(string v, float def) { float r; return float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out r) ? r : def; }

        public static Settings Load()
        {
            var s = new Settings();
            try
            {
                string ini = Path.Combine(Dir, "settings.ini");
                if (File.Exists(ini))
                {
                    foreach (string line in File.ReadAllLines(ini))
                    {
                        int eq = line.IndexOf('=');
                        if (eq <= 0) continue;
                        string k = line.Substring(0, eq).Trim(), v = line.Substring(eq + 1).Trim();
                        switch (k)
                        {
                            case "Speed": s.Speed = I(v, s.Speed); break;
                            case "FontSize": s.FontSize = F(v, s.FontSize); break;
                            case "FontName": if (v.Length > 0) s.FontName = v; break;
                            case "Opacity": s.Opacity = I(v, s.Opacity); break;
                            case "CenterText": s.CenterText = v == "1"; break;
                            case "HideFromCapture": s.HideFromCapture = v == "1"; break;
                            case "X": s.X = I(v, s.X); break;
                            case "Y": s.Y = I(v, s.Y); break;
                            case "W": s.W = I(v, s.W); break;
                            case "H": s.H = I(v, s.H); break;
                        }
                    }
                }
                string sp = Path.Combine(Dir, "script.txt");
                if (File.Exists(sp)) s.Script = File.ReadAllText(sp, Encoding.UTF8);
            }
            catch { }
            s.Clamp();
            return s;
        }

        public void Clamp()
        {
            Speed = Math.Max(1, Math.Min(100, Speed));
            FontSize = Math.Max(8f, Math.Min(96f, FontSize));
            Opacity = Math.Max(30, Math.Min(100, Opacity));
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                var sb = new StringBuilder();
                sb.AppendLine("Speed=" + Speed.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("FontSize=" + FontSize.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("FontName=" + FontName);
                sb.AppendLine("Opacity=" + Opacity.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("CenterText=" + (CenterText ? "1" : "0"));
                sb.AppendLine("HideFromCapture=" + (HideFromCapture ? "1" : "0"));
                sb.AppendLine("X=" + X.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("Y=" + Y.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("W=" + W.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("H=" + H.ToString(CultureInfo.InvariantCulture));
                File.WriteAllText(Path.Combine(Dir, "settings.ini"), sb.ToString(), Encoding.UTF8);
                File.WriteAllText(Path.Combine(Dir, "script.txt"), Script, Encoding.UTF8);
            }
            catch { }
        }
    }

    class PrompterForm : Form
    {
        readonly Settings s;
        readonly List<string> lines = new List<string>();
        readonly List<float> lineWidths = new List<float>();
        float lineH = 30f, contentHeight, offset;
        bool playing, hover;
        Font font, smallFont;
        readonly Timer timer = new Timer();
        readonly Stopwatch clock = Stopwatch.StartNew();
        double lastT;
        string toast; double toastUntil;
        NotifyIcon tray;
        ContextMenuStrip menu;
        ToolStripMenuItem miPlay, miHideCapture;
        readonly float dpi;
        readonly Color bg = Color.FromArgb(10, 10, 18);        // bottom of background gradient
        readonly Color bgTop = Color.FromArgb(22, 20, 38);     // top of background gradient
        readonly Color accent = Color.FromArgb(130, 200, 255);
        public static readonly Color WaveA = Color.FromArgb(0, 210, 255);    // cyan
        public static readonly Color WaveB = Color.FromArgb(130, 90, 255);   // violet
        public static readonly Color WaveC = Color.FromArgb(255, 80, 170);   // pink
        double wavePhase;
        readonly StringFormat fmt;

        const int HK_PLAY = 1, HK_FASTER = 2, HK_SLOWER = 3, HK_RESTART = 4, HK_SHOWHIDE = 5;

        int Px(float v) { return (int)Math.Round(v * dpi); }

        public PrompterForm()
        {
            s = Settings.Load();
            using (var g = CreateGraphics()) dpi = g.DpiX / 96f;

            fmt = (StringFormat)StringFormat.GenericTypographic.Clone();
            fmt.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.NoWrap;

            Text = "Teleprompter";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            ShowInTaskbar = true;
            BackColor = bg;
            KeyPreview = true;
            MinimumSize = new Size(Px(220), Px(70));
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Icon = MakeIcon();
            smallFont = new Font("Segoe UI", 9f);

            int w = s.W > 0 ? s.W : Px(640), h = s.H > 0 ? s.H : Px(170);
            Size = new Size(w, h);
            Point p = new Point(s.X, s.Y);
            bool onScreen = false;
            foreach (Screen sc in Screen.AllScreens)
                if (s.X != int.MinValue && sc.WorkingArea.IntersectsWith(new Rectangle(p, new Size(Math.Min(w, 100), Math.Min(h, 40))))) onScreen = true;
            if (onScreen) Location = p; else SnapTopCenter();

            BuildMenu();
            ApplySettings();

            timer.Interval = 15;
            timer.Tick += OnTick;
            timer.Start();
        }

        // ---------- setup ----------

        Icon MakeIcon()
        {
            var bmp = new Bitmap(32, 32);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using (var b = new SolidBrush(Color.FromArgb(20, 20, 24))) g.FillRectangle(b, 1, 4, 30, 24);
                using (var pen = new Pen(accent, 3f))
                {
                    g.DrawLine(pen, 6, 11, 26, 11);
                    g.DrawLine(pen, 6, 17, 22, 17);
                    g.DrawLine(pen, 6, 23, 18, 23);
                }
            }
            return Icon.FromHandle(bmp.GetHicon());
        }

        void BuildMenu()
        {
            menu = new ContextMenuStrip();
            miPlay = new ToolStripMenuItem("Play", null, delegate { TogglePlay(); });
            miPlay.ShortcutKeyDisplayString = "Space";
            menu.Items.Add(miPlay);
            var mi = new ToolStripMenuItem("Restart from top", null, delegate { Restart(); }); mi.ShortcutKeyDisplayString = "R"; menu.Items.Add(mi);
            mi = new ToolStripMenuItem("Faster", null, delegate { ChangeSpeed(+1); }); mi.ShortcutKeyDisplayString = "Up"; menu.Items.Add(mi);
            mi = new ToolStripMenuItem("Slower", null, delegate { ChangeSpeed(-1); }); mi.ShortcutKeyDisplayString = "Down"; menu.Items.Add(mi);
            menu.Items.Add(new ToolStripSeparator());
            mi = new ToolStripMenuItem("Edit script && settings...", null, delegate { OpenEditor(); }); mi.ShortcutKeyDisplayString = "E"; menu.Items.Add(mi);
            menu.Items.Add(new ToolStripMenuItem("Open text file...", null, delegate { OpenTextFile(); }));
            miHideCapture = new ToolStripMenuItem("Hide from screen sharing", null, delegate
            {
                s.HideFromCapture = !s.HideFromCapture; ApplySettings(); s.Save();
                ShowToast(s.HideFromCapture ? "Hidden from screen share" : "Visible in screen share");
            });
            menu.Items.Add(miHideCapture);
            menu.Items.Add(new ToolStripMenuItem("Snap to top center", null, delegate { SnapTopCenter(); }));
            mi = new ToolStripMenuItem("Show / hide window", null, delegate { ToggleVisible(); }); mi.ShortcutKeyDisplayString = "Ctrl+Alt+H"; menu.Items.Add(mi);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Exit", null, delegate { Close(); }));
            menu.Opening += delegate
            {
                miPlay.Text = playing ? "Pause" : "Play";
                miHideCapture.Checked = s.HideFromCapture;
            };
            menu.Renderer = new ToolStripProfessionalRenderer(new DarkMenuColors());
            menu.ForeColor = Color.FromArgb(232, 232, 245);
            menu.BackColor = Color.FromArgb(28, 26, 44);
            ContextMenuStrip = menu;

            tray = new NotifyIcon();
            tray.Icon = Icon;
            tray.Text = "Teleprompter";
            tray.ContextMenuStrip = menu;
            tray.Visible = true;
            tray.DoubleClick += delegate { ToggleVisible(); };
        }

        public void ApplySettings()
        {
            s.Clamp();
            if (font != null) font.Dispose();
            try { font = new Font(s.FontName, s.FontSize, FontStyle.Regular); }
            catch { font = new Font("Segoe UI", s.FontSize); }
            Opacity = s.Opacity / 100.0;
            ApplyCaptureAffinity();
            Relayout();
            Invalidate();
        }

        void ApplyCaptureAffinity()
        {
            if (!IsHandleCreated) return;
            try { Native.SetWindowDisplayAffinity(Handle, s.HideFromCapture ? Native.WDA_EXCLUDEFROMCAPTURE : Native.WDA_NONE); }
            catch { }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try { int round = 2; Native.DwmSetWindowAttribute(Handle, 33, ref round, 4); } catch { } // Windows 11 rounded corners
            ApplyCaptureAffinity();
            uint ca = Native.MOD_CONTROL | Native.MOD_ALT;
            Native.RegisterHotKey(Handle, HK_PLAY, ca | Native.MOD_NOREPEAT, (uint)Keys.Space);
            Native.RegisterHotKey(Handle, HK_FASTER, ca, (uint)Keys.Oemplus);
            Native.RegisterHotKey(Handle, HK_SLOWER, ca, (uint)Keys.OemMinus);
            Native.RegisterHotKey(Handle, HK_RESTART, ca | Native.MOD_NOREPEAT, (uint)Keys.R);
            Native.RegisterHotKey(Handle, HK_SHOWHIDE, ca | Native.MOD_NOREPEAT, (uint)Keys.H);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            for (int i = HK_PLAY; i <= HK_SHOWHIDE; i++) Native.UnregisterHotKey(Handle, i);
            if (WindowState == FormWindowState.Normal)
            {
                s.X = Left; s.Y = Top; s.W = Width; s.H = Height;
            }
            s.Save();
            if (tray != null) { tray.Visible = false; tray.Dispose(); }
            base.OnFormClosing(e);
        }

        // ---------- layout & painting ----------

        int PadX { get { return Px(22); } }
        int PadTop { get { return Px(14); } }

        void Relayout()
        {
            if (font == null) return;
            lines.Clear(); lineWidths.Clear();
            using (var g = CreateGraphics())
            {
                g.TextRenderingHint = TextRenderingHint.AntiAlias;
                float maxW = Math.Max(40, ClientSize.Width - 2 * PadX);
                string text = (s.Script ?? "").Replace("\r\n", "\n").Replace('\r', '\n');
                foreach (string para in text.Split('\n'))
                {
                    string[] words = para.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (words.Length == 0) { AddLine(g, ""); continue; }
                    string cur = "";
                    foreach (string word in words)
                    {
                        string trial = cur.Length == 0 ? word : cur + " " + word;
                        if (cur.Length > 0 && g.MeasureString(trial, font, PointF.Empty, fmt).Width > maxW)
                        {
                            AddLine(g, cur);
                            cur = word;
                        }
                        else cur = trial;
                    }
                    AddLine(g, cur);
                }
                lineH = font.GetHeight(g) * 1.2f;
            }
            contentHeight = lines.Count * lineH;
            offset = Math.Min(offset, MaxOffset);
        }

        void AddLine(Graphics g, string t)
        {
            lines.Add(t);
            lineWidths.Add(t.Length == 0 ? 0 : g.MeasureString(t, font, PointF.Empty, fmt).Width);
        }

        float MaxOffset { get { return Math.Max(0, contentHeight - lineH); } }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Relayout();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            int w = ClientSize.Width, h = ClientSize.Height;
            if (w <= 0 || h <= 0) return;
            using (var bgb = new LinearGradientBrush(new Rectangle(0, -1, w, h + 2), bgTop, bg, LinearGradientMode.Vertical))
                g.FillRectangle(bgb, 0, 0, w, h);
            int waveH = WaveHeight;
            g.TextRenderingHint = TextRenderingHint.AntiAlias;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // script lines (only the visible ones are drawn)
            int first = Math.Max(0, (int)((offset - PadTop) / lineH) - 1);
            for (int i = first; i < lines.Count; i++)
            {
                float y = PadTop + i * lineH - offset;
                if (y > h) break;
                if (y + lineH < 0 || lines[i].Length == 0) continue;
                float x = s.CenterText ? (w - lineWidths[i]) / 2f : PadX;
                g.DrawString(lines[i], font, Brushes.White, x, y, fmt);
            }

            // soft fades at the top edge and just above the waves
            int fade = Math.Min(Px(26), h / 4);
            if (fade > 2)
            {
                using (var b = new LinearGradientBrush(new Rectangle(0, -1, w, fade + 2), bgTop, Color.FromArgb(0, bgTop), LinearGradientMode.Vertical))
                    g.FillRectangle(b, 0, 0, w, fade);
                int fy = h - waveH - fade;
                using (var b = new LinearGradientBrush(new Rectangle(0, fy - 1, w, fade + 2), Color.FromArgb(0, bg), bg, LinearGradientMode.Vertical))
                    g.FillRectangle(b, 0, fy, w, fade);
                using (var b = new SolidBrush(bg)) g.FillRectangle(b, 0, h - waveH, w, waveH);
            }

            DrawWaves(g, w, h);

            // progress bar (glowing gradient line at the very bottom)
            if (MaxOffset > 0)
            {
                float pct = Math.Max(0, Math.Min(1, offset / MaxOffset));
                using (var b = new SolidBrush(Color.FromArgb(40, 255, 255, 255))) g.FillRectangle(b, 0, h - Px(3), w, Px(3));
                if (pct > 0)
                    using (var b = new LinearGradientBrush(new Rectangle(0, 0, w + 1, 1), WaveA, WaveC, LinearGradientMode.Horizontal))
                        g.FillRectangle(b, 0, h - Px(3), w * pct, Px(3));
            }

            // status chip (top-right)
            string status = (playing ? "▶  Speed " : "Paused · Speed ") + s.Speed;
            if (!playing || hover) DrawChip(g, status, w - Px(8), Px(6), true);

            // help bar when hovered
            if (hover)
            {
                string help = "Space play/pause · ↑↓ speed · R restart · E edit · right-click menu";
                SizeF sz = g.MeasureString(help, smallFont);
                if (sz.Width + Px(16) < w) DrawChip(g, help, (w + sz.Width) / 2f + Px(6), h - waveH - sz.Height - Px(6), false);
            }

            // toast (centered)
            if (toast != null && clock.Elapsed.TotalSeconds < toastUntil)
            {
                using (var f = new Font("Segoe UI Semibold", 12f))
                {
                    SizeF sz = g.MeasureString(toast, f);
                    var r = new RectangleF((w - sz.Width) / 2f - Px(12), (h - sz.Height) / 2f - Px(6), sz.Width + Px(24), sz.Height + Px(12));
                    using (var path = RoundRect(r, Px(10)))
                    using (var b = new SolidBrush(Color.FromArgb(235, 30, 28, 50)))
                    using (var pen = new Pen(Color.FromArgb(120, WaveB), Math.Max(1f, dpi)))
                    {
                        g.FillPath(b, path);
                        g.DrawPath(pen, path);
                    }
                    using (var b = new SolidBrush(accent)) g.DrawString(toast, f, b, r.X + Px(12), r.Y + Px(6));
                }
            }
        }

        int WaveHeight { get { return Math.Min(Px(26), ClientSize.Height / 4); } }

        // Three translucent, layered sine waves with a sliding cyan-violet-pink gradient.
        void DrawWaves(Graphics g, int w, int h)
        {
            int waveH = WaveHeight;
            if (waveH < 4) return;
            float t = (float)wavePhase;
            int step = Math.Max(2, Px(5));
            var blend = new ColorBlend();
            blend.Positions = new[] { 0f, 0.33f, 0.66f, 1f };

            for (int layer = 0; layer < 3; layer++)
            {
                float amp = waveH * (0.16f + layer * 0.06f);
                float wavelength = w / (1.1f + layer * 0.55f);
                float dir = layer % 2 == 0 ? 1f : -1f;
                float speed = 1.6f + layer * 0.7f;
                float yBase = h - waveH + amp + layer * waveH * 0.14f;

                var pts = new List<PointF>();
                pts.Add(new PointF(0, h));
                for (int x = 0; ; x += step)
                {
                    if (x > w) x = w;
                    double a = x / wavelength * 2 * Math.PI + t * speed * dir + layer * 1.7;
                    float y = yBase + amp * (float)Math.Sin(a) + amp * 0.35f * (float)Math.Sin(a * 2.3 + t);
                    pts.Add(new PointF(x, y));
                    if (x >= w) break;
                }
                pts.Add(new PointF(w, h));

                int alpha = 70 + layer * 55;
                using (var path = new GraphicsPath())
                using (var brush = new LinearGradientBrush(new Rectangle(0, 0, w, h), WaveA, WaveC, LinearGradientMode.Horizontal))
                {
                    path.AddPolygon(pts.ToArray());
                    blend.Colors = new[] { Color.FromArgb(alpha, WaveA), Color.FromArgb(alpha, WaveB), Color.FromArgb(alpha, WaveC), Color.FromArgb(alpha, WaveA) };
                    brush.InterpolationColors = blend;
                    brush.WrapMode = WrapMode.Tile;
                    brush.TranslateTransform((t * Px(30) * (layer + 1)) % w, 0);
                    g.FillPath(brush, path);

                    // bright crest line on the front wave
                    if (layer == 2)
                    {
                        var crest = new PointF[pts.Count - 2];
                        pts.CopyTo(1, crest, 0, crest.Length);
                        using (var pen = new Pen(Color.FromArgb(150, 255, 255, 255), Math.Max(1f, dpi)))
                            g.DrawLines(pen, crest);
                    }
                }
            }
        }

        void DrawChip(Graphics g, string text, float right, float top, bool accentText)
        {
            SizeF sz = g.MeasureString(text, smallFont);
            var r = new RectangleF(right - sz.Width - Px(12), top, sz.Width + Px(12), sz.Height + Px(4));
            using (var path = RoundRect(r, Px(7)))
            using (var b = new SolidBrush(Color.FromArgb(215, 32, 30, 52))) g.FillPath(b, path);
            using (var b = new SolidBrush(accentText ? accent : Color.FromArgb(220, 220, 220)))
                g.DrawString(text, smallFont, b, r.X + Px(6), r.Y + Px(2));
        }

        static GraphicsPath RoundRect(RectangleF r, float rad)
        {
            var p = new GraphicsPath();
            float d = rad * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        // ---------- animation ----------

        void OnTick(object sender, EventArgs e)
        {
            double t = clock.Elapsed.TotalSeconds;
            double dt = Math.Min(0.1, t - lastT);
            lastT = t;
            wavePhase += dt * (playing ? 1.0 : 0.35);   // waves flow faster while scrolling
            bool redraw = Visible && WindowState != FormWindowState.Minimized;

            if (playing)
            {
                offset += (float)(s.Speed * 2 * dt * dpi);
                if (offset >= MaxOffset)
                {
                    offset = MaxOffset;
                    playing = false;
                    ShowToast("End of script — press R to restart");
                }
                redraw = true;
            }

            bool h = Visible && Bounds.Contains(Cursor.Position);
            if (h != hover) { hover = h; redraw = true; }
            if (toast != null)
            {
                redraw = true;
                if (t >= toastUntil) toast = null;
            }
            if (redraw) Invalidate();
        }

        // ---------- actions ----------

        void TogglePlay()
        {
            if (!playing && offset >= MaxOffset && MaxOffset > 0) offset = 0; // restart if at the end
            playing = !playing;
            Invalidate();
        }

        void Restart()
        {
            offset = 0;
            ShowToast("Restarted");
        }

        void ChangeSpeed(int delta)
        {
            s.Speed = Math.Max(1, Math.Min(100, s.Speed + delta));
            ShowToast("Speed " + s.Speed);
        }

        void ShowToast(string text)
        {
            toast = text;
            toastUntil = clock.Elapsed.TotalSeconds + 1.2;
            Invalidate();
        }

        void ToggleVisible()
        {
            if (Visible) Hide();
            else { Show(); TopMost = true; Activate(); }
        }

        void SnapTopCenter()
        {
            Screen sc = Screen.FromPoint(Cursor.Position);
            Location = new Point(sc.Bounds.Left + (sc.Bounds.Width - Width) / 2, sc.Bounds.Top);
        }

        void OpenEditor()
        {
            bool wasPlaying = playing;
            playing = false;
            string oldScript = s.Script;
            using (var ed = new EditorForm(s))
            {
                if (ed.ShowDialog(this) == DialogResult.OK)
                {
                    ed.WriteTo(s);
                    if (s.Script != oldScript) offset = 0;
                    ApplySettings();
                    s.Save();
                    return;
                }
            }
            playing = wasPlaying;
        }

        void OpenTextFile()
        {
            using (var dlg = new OpenFileDialog())
            {
                dlg.Filter = "Text files (*.txt;*.md)|*.txt;*.md|All files (*.*)|*.*";
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    s.Script = File.ReadAllText(dlg.FileName);
                    offset = 0; playing = false;
                    ApplySettings(); s.Save();
                    ShowToast("Loaded " + Path.GetFileName(dlg.FileName));
                }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "Could not open file"); }
            }
        }

        // ---------- input ----------

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.Space: TogglePlay(); return true;
                case Keys.Up: case Keys.Oemplus: case Keys.Add: ChangeSpeed(+1); return true;
                case Keys.Down: case Keys.OemMinus: case Keys.Subtract: ChangeSpeed(-1); return true;
                case Keys.Shift | Keys.Up: ChangeSpeed(+5); return true;
                case Keys.Shift | Keys.Down: ChangeSpeed(-5); return true;
                case Keys.Left: case Keys.PageUp: Nudge(-3); return true;
                case Keys.Right: case Keys.PageDown: Nudge(+3); return true;
                case Keys.R: case Keys.Home: Restart(); return true;
                case Keys.E: case Keys.Enter: OpenEditor(); return true;
                case Keys.O: OpenTextFile(); return true;
                case Keys.Escape: playing = false; Invalidate(); return true;
                case Keys.Control | Keys.Oemplus: case Keys.Control | Keys.Add: ChangeFont(+2); return true;
                case Keys.Control | Keys.OemMinus: case Keys.Control | Keys.Subtract: ChangeFont(-2); return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        void Nudge(int linesDelta)
        {
            offset = Math.Max(0, Math.Min(MaxOffset, offset + linesDelta * lineH));
            Invalidate();
        }

        void ChangeFont(float delta)
        {
            s.FontSize = Math.Max(8f, Math.Min(96f, s.FontSize + delta));
            float ratio = contentHeight > 0 ? offset / contentHeight : 0;
            ApplySettings();
            offset = Math.Min(MaxOffset, ratio * contentHeight);
            ShowToast("Font " + s.FontSize.ToString("0", CultureInfo.InvariantCulture));
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            int steps = e.Delta / 120;
            if ((ModifierKeys & Keys.Control) != 0) ChangeFont(steps * 2);
            else if ((ModifierKeys & Keys.Shift) != 0) ChangeSpeed(steps);
            else
            {
                offset = Math.Max(0, Math.Min(MaxOffset, offset - e.Delta / 120f * lineH));
                Invalidate();
            }
            base.OnMouseWheel(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            if (e.Clicks >= 2) { TogglePlay(); return; }
            Native.ReleaseCapture();
            Native.SendMessage(Handle, Native.WM_NCLBUTTONDOWN, (IntPtr)Native.HTCAPTION, IntPtr.Zero);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_HOTKEY)
            {
                switch (m.WParam.ToInt32())
                {
                    case HK_PLAY: TogglePlay(); break;
                    case HK_FASTER: ChangeSpeed(+1); break;
                    case HK_SLOWER: ChangeSpeed(-1); break;
                    case HK_RESTART: Restart(); break;
                    case HK_SHOWHIDE: ToggleVisible(); break;
                }
                return;
            }
            base.WndProc(ref m);
            if (m.Msg == Native.WM_NCHITTEST && WindowState == FormWindowState.Normal)
            {
                // borderless resize from the edges
                long lp = m.LParam.ToInt64();
                var pt = PointToClient(new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF)));
                int grip = Px(7);
                bool l = pt.X < grip, r = pt.X >= ClientSize.Width - grip, t = pt.Y < grip, b = pt.Y >= ClientSize.Height - grip;
                int hit = 0;
                if (t && l) hit = 13; else if (t && r) hit = 14; else if (b && l) hit = 16; else if (b && r) hit = 17;
                else if (l) hit = 10; else if (r) hit = 11; else if (t) hit = 12; else if (b) hit = 15;
                if (hit != 0) m.Result = (IntPtr)hit;
            }
        }
    }

    class DarkMenuColors : ProfessionalColorTable
    {
        static readonly Color Bg = Color.FromArgb(28, 26, 44), Sel = Color.FromArgb(70, 60, 130), Line = Color.FromArgb(60, 56, 90);
        public override Color ToolStripDropDownBackground { get { return Bg; } }
        public override Color ImageMarginGradientBegin { get { return Bg; } }
        public override Color ImageMarginGradientMiddle { get { return Bg; } }
        public override Color ImageMarginGradientEnd { get { return Bg; } }
        public override Color MenuBorder { get { return Line; } }
        public override Color MenuItemBorder { get { return Sel; } }
        public override Color MenuItemSelected { get { return Sel; } }
        public override Color MenuItemSelectedGradientBegin { get { return Sel; } }
        public override Color MenuItemSelectedGradientEnd { get { return Sel; } }
        public override Color SeparatorDark { get { return Line; } }
        public override Color SeparatorLight { get { return Bg; } }
        public override Color CheckBackground { get { return Color.FromArgb(0, 150, 200); } }
        public override Color CheckSelectedBackground { get { return Color.FromArgb(0, 170, 220); } }
        public override Color CheckPressedBackground { get { return Color.FromArgb(0, 170, 220); } }
    }

    class EditorForm : Form
    {
        static readonly Color EdBg = Color.FromArgb(22, 20, 36), EdBox = Color.FromArgb(14, 13, 24),
            EdText = Color.FromArgb(232, 232, 245), EdMuted = Color.FromArgb(150, 148, 180), EdBtn = Color.FromArgb(48, 44, 74);

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try { int on = 1; Native.DwmSetWindowAttribute(Handle, 20, ref on, 4); } catch { } // dark title bar
        }

        static Button DarkButton(string text, bool primary)
        {
            var b = new Button();
            b.Text = text; b.AutoSize = true; b.FlatStyle = FlatStyle.Flat;
            b.Padding = new Padding(10, 3, 10, 3); b.Margin = new Padding(4, 4, 4, 0);
            b.BackColor = primary ? Color.FromArgb(110, 80, 240) : EdBtn;
            b.ForeColor = Color.White;
            b.FlatAppearance.BorderColor = primary ? Color.FromArgb(150, 120, 255) : Color.FromArgb(70, 66, 100);
            b.Cursor = Cursors.Hand;
            return b;
        }

        readonly TextBox txt;
        readonly TrackBar speed, opacity;
        readonly NumericUpDown fontSize;
        readonly CheckBox center, hide;
        readonly Label speedLbl, opacityLbl, info;
        readonly ComboBox fontName;

        public EditorForm(Settings s)
        {
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96f, 96f);
            Font = new Font("Segoe UI", 9.5f);
            Text = "Teleprompter — Script & Settings";
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(820, 620);
            MinimumSize = new Size(600, 420);
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = EdBg;
            ForeColor = EdText;

            txt = new TextBox();
            txt.BackColor = EdBox; txt.ForeColor = EdText; txt.BorderStyle = BorderStyle.None;
            txt.Multiline = true; txt.AcceptsReturn = true; txt.AcceptsTab = true;
            txt.MaxLength = 0; // no length limit (default would cap at 32,767 characters)
            txt.ScrollBars = ScrollBars.Vertical; txt.WordWrap = true;
            txt.Font = new Font("Segoe UI", 12f);
            txt.Dock = DockStyle.Fill;
            txt.Text = (s.Script ?? "").Replace("\r\n", "\n").Replace("\n", "\r\n");
            txt.TextChanged += delegate { UpdateInfo(); };

            var row1 = new FlowLayoutPanel();
            row1.Dock = DockStyle.Bottom; row1.AutoSize = true; row1.Padding = new Padding(6, 8, 6, 0);
            row1.WrapContents = true;

            speedLbl = new Label(); speedLbl.AutoSize = true; speedLbl.Margin = new Padding(3, 10, 0, 0);
            speed = new TrackBar(); speed.Minimum = 1; speed.Maximum = 100; speed.TickFrequency = 10; speed.Width = 220;
            speed.Value = s.Speed; speed.ValueChanged += delegate { UpdateInfo(); };
            row1.Controls.Add(speedLbl); row1.Controls.Add(speed);

            var fl = new Label(); fl.Text = "Font:"; fl.AutoSize = true; fl.Margin = new Padding(12, 10, 0, 0);
            fontName = new ComboBox(); fontName.DropDownStyle = ComboBoxStyle.DropDownList; fontName.Width = 150; fontName.Margin = new Padding(3, 7, 3, 0);
            fontName.FlatStyle = FlatStyle.Flat; fontName.BackColor = EdBtn; fontName.ForeColor = EdText;
            foreach (string f in new[] { "Segoe UI", "Arial", "Calibri", "Verdana", "Tahoma", "Georgia", "Consolas" }) fontName.Items.Add(f);
            if (!fontName.Items.Contains(s.FontName)) fontName.Items.Add(s.FontName);
            fontName.SelectedItem = s.FontName;
            fontSize = new NumericUpDown(); fontSize.Minimum = 8; fontSize.Maximum = 96; fontSize.Width = 60; fontSize.Margin = new Padding(3, 7, 3, 0);
            fontSize.Value = (decimal)Math.Round(s.FontSize);
            fontSize.BackColor = EdBtn; fontSize.ForeColor = EdText; fontSize.BorderStyle = BorderStyle.FixedSingle;
            speed.BackColor = EdBg;
            row1.Controls.Add(fl); row1.Controls.Add(fontName); row1.Controls.Add(fontSize);

            opacityLbl = new Label(); opacityLbl.AutoSize = true; opacityLbl.Margin = new Padding(12, 10, 0, 0);
            opacity = new TrackBar(); opacity.Minimum = 30; opacity.Maximum = 100; opacity.TickFrequency = 10; opacity.Width = 150;
            opacity.Value = s.Opacity; opacity.ValueChanged += delegate { UpdateInfo(); };
            opacity.BackColor = EdBg;
            row1.Controls.Add(opacityLbl); row1.Controls.Add(opacity);

            var row2 = new FlowLayoutPanel();
            row2.Dock = DockStyle.Bottom; row2.AutoSize = true; row2.Padding = new Padding(6, 0, 6, 8);

            center = new CheckBox(); center.Text = "Center text"; center.AutoSize = true; center.Checked = s.CenterText; center.Margin = new Padding(6, 8, 6, 0);
            hide = new CheckBox(); hide.Text = "Hide from screen sharing / recording"; hide.AutoSize = true; hide.Checked = s.HideFromCapture; hide.Margin = new Padding(6, 8, 6, 0);
            var load = DarkButton("Load .txt...", false);
            load.Click += delegate
            {
                using (var dlg = new OpenFileDialog())
                {
                    dlg.Filter = "Text files (*.txt;*.md)|*.txt;*.md|All files (*.*)|*.*";
                    if (dlg.ShowDialog(this) == DialogResult.OK)
                    {
                        try { txt.Text = File.ReadAllText(dlg.FileName).Replace("\r\n", "\n").Replace("\n", "\r\n"); }
                        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Could not open file"); }
                    }
                }
            };
            var ok = DarkButton("Save && Close", true); ok.DialogResult = DialogResult.OK;
            var cancel = DarkButton("Cancel", false); cancel.DialogResult = DialogResult.Cancel;
            CancelButton = cancel;
            info = new Label(); info.AutoSize = true; info.Margin = new Padding(12, 9, 0, 0); info.ForeColor = EdMuted;
            row2.Controls.Add(center); row2.Controls.Add(hide); row2.Controls.Add(load); row2.Controls.Add(ok); row2.Controls.Add(cancel); row2.Controls.Add(info);

            // gradient accent strip under the title bar
            var strip = new Panel(); strip.Dock = DockStyle.Top; strip.Height = 4;
            strip.Paint += delegate(object o, PaintEventArgs pe)
            {
                var r = strip.ClientRectangle; if (r.Width <= 0) return;
                using (var b = new LinearGradientBrush(r, PrompterForm.WaveA, PrompterForm.WaveC, LinearGradientMode.Horizontal))
                {
                    var cb = new ColorBlend();
                    cb.Colors = new[] { PrompterForm.WaveA, PrompterForm.WaveB, PrompterForm.WaveC };
                    cb.Positions = new[] { 0f, 0.5f, 1f };
                    b.InterpolationColors = cb;
                    pe.Graphics.FillRectangle(b, r);
                }
            };
            // padding around the text box so text doesn't touch the edges
            var txtHost = new Panel(); txtHost.Dock = DockStyle.Fill; txtHost.BackColor = EdBox; txtHost.Padding = new Padding(14, 10, 6, 10);
            txtHost.Controls.Add(txt);

            Controls.Add(txtHost);
            Controls.Add(strip);
            Controls.Add(row1);
            Controls.Add(row2);
            txtHost.BringToFront();
            UpdateInfo();
        }

        void UpdateInfo()
        {
            speedLbl.Text = "Speed: " + speed.Value;
            opacityLbl.Text = "Opacity: " + opacity.Value + "%";
            int words = txt.Text.Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
            info.Text = words + " words";
        }

        public void WriteTo(Settings s)
        {
            s.Script = txt.Text.Replace("\r\n", "\n");
            s.Speed = speed.Value;
            s.FontName = (fontName.SelectedItem as string) ?? "Segoe UI";
            s.FontSize = (float)fontSize.Value;
            s.Opacity = opacity.Value;
            s.CenterText = center.Checked;
            s.HideFromCapture = hide.Checked;
        }
    }

    static class Program
    {
        [STAThread]
        static void Main()
        {
            try { Native.SetProcessDPIAware(); } catch { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            bool created;
            using (var m = new System.Threading.Mutex(true, "TeleprompterWin_SingleInstance", out created))
            {
                if (!created)
                {
                    MessageBox.Show("Teleprompter is already running (check the system tray).", "Teleprompter");
                    return;
                }
                Application.Run(new PrompterForm());
            }
        }
    }
}
