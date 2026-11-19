using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using DriftWatch.Core;

namespace DriftWatch.UI
{
    /// <summary>What pages can ask the main window to do.</summary>
    public interface IShell
    {
        Workspace Workspace { get; }
        AppSettings Settings { get; }
        Form Window { get; }
        void Navigate(string page, object arg);
        void Toast(string text, BannerKind kind);
        void OpenFolderDialog();
        void OpenFolder(string folder);
        void AddReportsDialog();
        void Compare(Snapshot older, Snapshot newer, Change focus);
        void ShowDevice(DeviceHistory d, int tab);
        /// <summary>Saves the policy and re-scores everything.</summary>
        void PolicyChanged(bool save);
        void ShowPalette();
        string SaveFileDialog(string title, string fileName, string filter);
        void Rescan();
        void SettingsChanged();
    }

    /// <summary>A screen in the main window.</summary>
    public abstract class Page : Surface
    {
        protected Page(IShell shell)
        {
            Shell = shell;
        }

        protected IShell Shell { get; private set; }
        protected Workspace Ws { get { return Shell.Workspace; } }
        public abstract string Key { get; }
        public abstract string PageTitle { get; }
        public virtual string PageSubtitle { get { return ""; } }
        public event EventHandler TitleChanged;

        protected void RaiseTitleChanged()
        {
            var h = TitleChanged;
            if (h != null) h(this, EventArgs.Empty);
        }

        /// <summary>Called when the loaded reports or the policy change.</summary>
        public virtual void RefreshData() { }
        /// <summary>Called each time the page is shown, with an optional argument such as a device.</summary>
        public virtual void Activate(object arg) { }
        public virtual void FocusSearch() { }

        protected int Pad { get { return Dpi.S(28); } }
        protected int Gap { get { return Dpi.S(16); } }
    }

    public sealed class NavItem
    {
        public NavItem(string key, string text, string icon, string group) { Key = key; Text = text; Icon = icon; Group = group; }
        public string Key, Text, Icon, Group;
        public int Badge;
        public Color? BadgeColor;
        public Rectangle Bounds;
    }

    /// <summary>The left navigation rail: logo, pages, workspace status and quick settings.</summary>
    public sealed class NavRail : Surface
    {
        private readonly List<NavItem> items = new List<NavItem>();
        private string selected = "dashboard";
        private string hover;
        private Rectangle workspaceRect, themeRect, keysRect, aboutRect;
        private readonly Timer pulse = new Timer { Interval = 50 };
        private double phase;

        public NavRail()
        {
            Width = Dpi.S(236);
            items.Add(new NavItem("dashboard", "Overview", "dashboard", "MONITOR"));
            items.Add(new NavItem("devices", "Devices", "devices", "MONITOR"));
            items.Add(new NavItem("compare", "Compare", "compare", "MONITOR"));
            items.Add(new NavItem("posture", "Posture", "shield-check", "ANALYSE"));
            items.Add(new NavItem("software", "Software", "package", "ANALYSE"));
            items.Add(new NavItem("policy", "Policy", "policy", "CONFIGURE"));
            items.Add(new NavItem("settings", "Settings", "settings", "CONFIGURE"));
            pulse.Tick += delegate { phase += 0.12; Invalidate(new Rectangle(workspaceRect.X, workspaceRect.Bottom - Dpi.S(30), Dpi.S(40), Dpi.S(30))); };
            if (Environment.GetEnvironmentVariable("DRIFTWATCH_NO_ANIMATION") != "1") pulse.Start();
        }

        public event EventHandler<string> Navigate;
        public event EventHandler WorkspaceClicked, ThemeClicked, ShortcutsClicked, AboutClicked;

        public string FolderName = "";
        public string FolderStats = "No reports loaded";
        public bool Watching;
        public bool Busy;

        public override Color InnerColor { get { return T.Rail; } }

        public string Selected
        {
            get { return selected; }
            set { selected = value; Invalidate(); }
        }

        public void SetBadge(string key, int count, Color? color)
        {
            var it = items.FirstOrDefault(i => i.Key == key);
            if (it != null) { it.Badge = count; it.BadgeColor = color; Invalidate(); }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) pulse.Dispose();
            base.Dispose(disposing);
        }

        private string HitTest(Point p)
        {
            foreach (var it in items) if (it.Bounds.Contains(p)) return it.Key;
            if (workspaceRect.Contains(p)) return "#workspace";
            if (themeRect.Contains(p)) return "#theme";
            if (keysRect.Contains(p)) return "#keys";
            if (aboutRect.Contains(p)) return "#about";
            return null;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            string h = HitTest(e.Location);
            if (h != hover) { hover = h; Cursor = h != null ? Cursors.Hand : Cursors.Default; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e) { hover = null; Invalidate(); }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            string h = HitTest(e.Location);
            if (h == null) return;
            EventHandler ev = null;
            switch (h)
            {
                case "#workspace": ev = WorkspaceClicked; break;
                case "#theme": ev = ThemeClicked; break;
                case "#keys": ev = ShortcutsClicked; break;
                case "#about": ev = AboutClicked; break;
                default:
                    var nav = Navigate;
                    if (nav != null) nav(this, h);
                    return;
            }
            if (ev != null) ev(this, EventArgs.Empty);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(T.Rail);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            using (var pen = new Pen(T.Line, 1f)) g.DrawLine(pen, Width - 1, 0, Width - 1, Height);
            int pad = Dpi.S(16);

            var logo = new RectangleF(pad + Dpi.S(4), Dpi.S(20), Dpi.S(36), Dpi.S(36));
            Draw.Logo(g, logo, T);
            int tx = (int)logo.Right + Dpi.S(12);
            Draw.Text(g, "DriftWatch", Fonts.H3, T.Ink, new Rectangle(tx, Dpi.S(19), Dpi.S(120), Dpi.S(22)));
            int bw = Draw.Measure("DriftWatch", Fonts.H3).Width;
            var beta = new Rectangle(tx + bw + Dpi.S(8), Dpi.S(22), Draw.Measure("BETA", Fonts.Tiny).Width + Dpi.S(10), Dpi.S(17));
            if (beta.Right < Width - pad)
            {
                Draw.Smooth(g);
                Draw.Fill(g, beta, Dpi.S(5), T.Tint(T.Accent2, T.Dark ? 0.2 : 0.12));
                Draw.Text(g, "BETA", Fonts.Tiny, T.Dark ? Theme.Mix(T.Accent2, Color.White, 0.35) : T.Accent2, beta, Draw.Center);
            }
            Draw.Text(g, "Endpoint drift monitor", Fonts.Small, T.Muted, new Rectangle(tx, Dpi.S(41), Width - tx - pad, Dpi.S(16)));

            int y = Dpi.S(84);
            string group = null;
            foreach (var it in items)
            {
                if (it.Group != group)
                {
                    group = it.Group;
                    if (y > Dpi.S(84)) y += Dpi.S(10);
                    Draw.Text(g, group, Fonts.Tiny, T.Faint, new Rectangle(pad + Dpi.S(12), y, Width, Dpi.S(20)));
                    y += Dpi.S(24);
                }
                it.Bounds = new Rectangle(pad - Dpi.S(4), y, Width - pad * 2 + Dpi.S(8), Dpi.S(40));
                bool sel = it.Key == selected, hov = it.Key == hover;
                Draw.Smooth(g);
                if (sel)
                {
                    Draw.Gradient(g, it.Bounds, Dpi.S(10), T.Tint(T.Accent, T.Dark ? 0.16 : 0.1), T.Tint(T.Accent2, T.Dark ? 0.08 : 0.05), 0f);
                    Draw.Fill(g, new RectangleF(it.Bounds.X, it.Bounds.Y + Dpi.S(10), Dpi.S(3), it.Bounds.Height - Dpi.S(20)), Dpi.S(1.5f), T.Accent);
                }
                else if (hov) Draw.Fill(g, it.Bounds, Dpi.S(10), T.CardHover);
                int s = Dpi.S(18);
                Icons.Draw(g, it.Icon, new RectangleF(it.Bounds.X + Dpi.S(14), it.Bounds.Y + (it.Bounds.Height - s) / 2f, s, s), sel ? T.Accent : hov ? T.Soft : T.Muted, 1.9f);
                Draw.Text(g, it.Text, sel ? Fonts.Semi : Fonts.Body, sel ? T.Ink : hov ? T.Ink : T.Soft, new Rectangle(it.Bounds.X + Dpi.S(44), it.Bounds.Y, it.Bounds.Width - Dpi.S(90), it.Bounds.Height));
                if (it.Badge > 0)
                {
                    string n = it.Badge.ToString(CultureInfo.InvariantCulture);
                    var c = it.BadgeColor ?? T.Muted;
                    int w = Math.Max(Dpi.S(22), Draw.Measure(n, Fonts.Tiny).Width + Dpi.S(12));
                    var br = new Rectangle(it.Bounds.Right - w - Dpi.S(10), it.Bounds.Y + (it.Bounds.Height - Dpi.S(20)) / 2, w, Dpi.S(20));
                    Draw.Fill(g, br, Dpi.S(10), T.Tint(c, T.Dark ? 0.24 : 0.14));
                    Draw.Text(g, n, Fonts.Tiny, T.Dark ? Theme.Mix(c, Color.White, 0.3) : Theme.Mix(c, Color.Black, 0.15), br, Draw.Center);
                }
                y += Dpi.S(42);
            }

            int by = Height - Dpi.S(52);
            int bs = Dpi.S(34);
            themeRect = new Rectangle(pad, by, bs, bs);
            keysRect = new Rectangle(pad + bs + Dpi.S(6), by, bs, bs);
            aboutRect = new Rectangle(pad + (bs + Dpi.S(6)) * 2, by, bs, bs);
            QuickButton(g, themeRect, T.Dark ? "sun" : "moon", "#theme");
            QuickButton(g, keysRect, "keyboard", "#keys");
            QuickButton(g, aboutRect, "info", "#about");
            string ver = "v" + AppInfo.Version;
            int vw = Width - aboutRect.Right - Dpi.S(8) - pad;
            if (Draw.Measure(ver, Fonts.MonoSmall).Width > vw) ver = "v" + AppInfo.Version.Split('-')[0];
            if (Draw.Measure(ver, Fonts.MonoSmall).Width <= vw) Draw.Text(g, ver, Fonts.MonoSmall, T.Faint, new Rectangle(aboutRect.Right + Dpi.S(8), by, vw, bs), Draw.Right);

            int wh = Dpi.S(92);
            workspaceRect = new Rectangle(pad - Dpi.S(4), by - wh - Dpi.S(12), Width - pad * 2 + Dpi.S(8), wh);
            if (workspaceRect.Y > y + Dpi.S(8))
            {
                bool hov = hover == "#workspace";
                Draw.Smooth(g);
                Draw.Fill(g, workspaceRect, Dpi.S(12), hov ? T.CardHover : T.Card);
                Draw.Stroke(g, workspaceRect, Dpi.S(12), hov ? Theme.Mix(T.Line, T.Accent, 0.4) : T.Line, 1f);
                int ix = workspaceRect.X + Dpi.S(14), iy = workspaceRect.Y + Dpi.S(14);
                Icons.Draw(g, "folder", new RectangleF(ix, iy, Dpi.S(16), Dpi.S(16)), T.Accent, 2f);
                Draw.Text(g, string.IsNullOrEmpty(FolderName) ? "No folder open" : FolderName, Fonts.SmallSemi, T.Ink, new Rectangle(ix + Dpi.S(24), iy - Dpi.S(1), workspaceRect.Width - Dpi.S(52), Dpi.S(18)));
                Draw.Text(g, FolderStats, Fonts.Small, T.Muted, new Rectangle(ix, iy + Dpi.S(24), workspaceRect.Width - Dpi.S(28), Dpi.S(16)));
                int dy = workspaceRect.Bottom - Dpi.S(22);
                Color dot = Busy ? T.Accent : Watching ? T.Ok : T.Faint;
                if (Watching || Busy)
                {
                    double a = (Math.Sin(phase) + 1) / 2;
                    float halo = Dpi.S(6f) + (float)(a * Dpi.S(5f));
                    using (var b = new SolidBrush(Theme.Alpha(dot, (int)(70 * (1 - a)))))
                        g.FillEllipse(b, ix + Dpi.S(4) - halo / 2, dy + Dpi.S(1) - halo / 2 + Dpi.S(4), halo, halo);
                }
                using (var b = new SolidBrush(dot)) g.FillEllipse(b, ix + Dpi.S(1), dy + Dpi.S(2), Dpi.S(7), Dpi.S(7));
                string status = Busy ? "Scanning…" : Watching ? "Watching for new audits" : string.IsNullOrEmpty(FolderName) ? "Click to open a folder" : "Not watching";
                Draw.Text(g, status, Fonts.Small, Busy || Watching ? T.Soft : T.Muted, new Rectangle(ix + Dpi.S(16), dy - Dpi.S(3), workspaceRect.Width - Dpi.S(40), Dpi.S(18)));
            }
        }

        private void QuickButton(Graphics g, Rectangle r, string icon, string key)
        {
            bool hov = hover == key;
            Draw.Smooth(g);
            Draw.Fill(g, r, Dpi.S(9), hov ? T.CardHover : T.Rail);
            Draw.Stroke(g, r, Dpi.S(9), T.Line, 1f);
            int s = Dpi.S(16);
            Icons.Draw(g, icon, new RectangleF(r.X + (r.Width - s) / 2f, r.Y + (r.Height - s) / 2f, s, s), hov ? T.Ink : T.Muted, 1.9f);
        }
    }

    /// <summary>The page title area with the global search and the main actions.</summary>
    public sealed class TopBar : Surface
    {
        public readonly SearchBox Search;
        public readonly Btn AddButton, OpenButton;

        public TopBar()
        {
            Search = new SearchBox("Search devices, software, pages…") { Width = Dpi.S(300), Hint = "Ctrl+K", Launcher = true };
            AddButton = new Btn("Add reports", "file-plus", ButtonStyle.Secondary);
            OpenButton = new Btn("Open folder", "folder", ButtonStyle.Primary);
            Controls.Add(Search);
            Controls.Add(AddButton);
            Controls.Add(OpenButton);
            Height = Dpi.S(100);
        }

        public string Title = "";
        public string Subtitle = "";

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (OpenButton == null || AddButton == null || Search == null) return;
            int pad = Dpi.S(28), y = Dpi.S(28);
            int x = Width - pad - OpenButton.Width;
            OpenButton.Location = new Point(x, y);
            x -= Dpi.S(10) + AddButton.Width;
            AddButton.Location = new Point(x, y);
            int sw = Math.Min(Dpi.S(320), Math.Max(Dpi.S(180), x - Dpi.S(380)));
            Search.Width = sw;
            x -= Dpi.S(14) + sw;
            Search.Location = new Point(x, y);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            int pad = Dpi.S(28);
            int right = Search.Left - Dpi.S(20);
            int block = Fonts.H1.Height + Dpi.S(4) + Fonts.Body.Height;
            int top = Dpi.S(28) + OpenButton.Height / 2 - block / 2;
            Draw.Text(g, Title, Fonts.H1, T.Ink, new Rectangle(pad, top, right - pad, Fonts.H1.Height + Dpi.S(2)));
            Draw.Text(g, Subtitle, Fonts.Body, T.Muted, new Rectangle(pad, top + Fonts.H1.Height + Dpi.S(4), right - pad, Fonts.Body.Height + Dpi.S(2)));
        }
    }

    /// <summary>A short message that slides in at the bottom right and fades away.</summary>
    public sealed class ToastView : Surface
    {
        private readonly Timer timer = new Timer();
        public BannerKind Kind = BannerKind.Info;

        public ToastView()
        {
            Visible = false;
            Size = Dpi.S(380, 56);
            timer.Tick += delegate { timer.Stop(); Visible = false; };
            Cursor = Cursors.Hand;
        }

        public void Show(string text, BannerKind kind)
        {
            Text = text;
            Kind = kind;
            int h = Math.Max(Dpi.S(56), Draw.MeasureHeight(text, Fonts.Body, Width - Dpi.S(70)) + Dpi.S(30));
            Height = h;
            if (Parent != null)
                Location = new Point(Parent.ClientSize.Width - Width - Dpi.S(24), Parent.ClientSize.Height - Height - Dpi.S(24));
            Visible = true;
            BringToFront();
            Invalidate();
            timer.Stop();
            timer.Interval = Math.Max(3500, text.Length * 60);
            timer.Start();
        }

        protected override void OnClick(EventArgs e) { Visible = false; base.OnClick(e); }

        protected override void Dispose(bool disposing)
        {
            if (disposing) timer.Dispose();
            base.Dispose(disposing);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(T.Bg);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Draw.Smooth(g);
            Color c = Kind == BannerKind.Ok ? T.Ok : Kind == BannerKind.Warn ? T.Warn : Kind == BannerKind.Bad ? T.Bad : T.Accent;
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Draw.Fill(g, r, Dpi.S(12f), T.Dark ? T.CardHover : T.Card);
            Draw.Stroke(g, r, Dpi.S(12f), Theme.Mix(T.Line, c, 0.5), 1f);
            Draw.Fill(g, new RectangleF(Dpi.S(1), Dpi.S(12), Dpi.S(3), Height - Dpi.S(24)), Dpi.S(1.5f), c);
            int s = Dpi.S(18);
            string icon = Kind == BannerKind.Ok ? "check-circle" : Kind == BannerKind.Info ? "info" : "alert";
            Icons.Draw(g, icon, new RectangleF(Dpi.S(18), Dpi.S(18), s, s), c, 2f);
            Draw.Text(g, Text, Fonts.Body, T.Ink, new Rectangle(Dpi.S(48), Dpi.S(17), Width - Dpi.S(64), Height - Dpi.S(24)), Draw.Wrap);
        }
    }

    /// <summary>Covers the window while files are dragged over it, or while a folder is being scanned.</summary>
    public sealed class Overlay : Surface
    {
        private readonly Timer spin = new Timer { Interval = 30 };
        private float angle;

        public Overlay()
        {
            Visible = false;
            spin.Tick += delegate { angle = (angle + 9) % 360; Invalidate(); };
        }

        public bool Busy;
        public string Title = "";
        public string Detail = "";

        public void ShowDrop()
        {
            Busy = false;
            Title = "Drop to add reports";
            Detail = "DeviceDossier XML reports, or a folder of them";
            spin.Stop();
            Visible = true;
            BringToFront();
            Invalidate();
        }

        public void ShowBusy(string title, string detail)
        {
            Busy = true;
            Title = title;
            Detail = detail;
            Visible = true;
            BringToFront();
            if (Environment.GetEnvironmentVariable("DRIFTWATCH_NO_ANIMATION") != "1") spin.Start();
            Invalidate();
        }

        public void HideOverlay()
        {
            spin.Stop();
            Visible = false;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) spin.Dispose();
            base.Dispose(disposing);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(Theme.Mix(T.Bg, T.Card, 0.3));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Draw.Smooth(g);
            var r = new RectangleF(Dpi.S(24), Dpi.S(24), Width - Dpi.S(48), Height - Dpi.S(48));
            if (!Busy)
            {
                Draw.Fill(g, r, Dpi.S(18), T.Tint(T.Accent, T.Dark ? 0.05 : 0.04));
                using (var p = Draw.Round(r, Dpi.S(18)))
                using (var pen = new Pen(T.Accent, Dpi.S(2f)) { DashStyle = DashStyle.Dash }) g.DrawPath(pen, p);
            }
            int s = Dpi.S(56);
            int cy = Height / 2 - Dpi.S(40);
            var ir = new RectangleF((Width - s) / 2f, cy - s / 2f, s, s);
            if (Busy)
            {
                using (var pen = new Pen(T.LineSoft, Dpi.S(5f))) g.DrawEllipse(pen, ir);
                using (var br = new LinearGradientBrush(ir, T.Accent, T.Accent2, 45f))
                using (var pen = new Pen(br, Dpi.S(5f)) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawArc(pen, ir, angle, 100);
            }
            else Icons.Draw(g, "upload", ir, T.Accent, 1.6f);
            Draw.Text(g, Title, Fonts.H2, T.Ink, new Rectangle(0, cy + s / 2 + Dpi.S(20), Width, Fonts.H2.Height), Draw.Center);
            Draw.Text(g, Detail, Fonts.Body, T.Muted, new Rectangle(0, cy + s / 2 + Dpi.S(24) + Fonts.H2.Height, Width, Dpi.S(22)), Draw.Center);
        }
    }

    /// <summary>A scrolling container for long forms (Policy and Settings).</summary>
    public sealed class ScrollHost : Panel
    {
        public ScrollHost()
        {
            AutoScroll = true;
            DoubleBuffered = true;
            BackColor = Theme.Current.Bg;
            Theme.Changed += OnTheme;
            HandleCreated += delegate { Native.DarkScrollbars(this, Theme.Current); };
        }

        private void OnTheme(object s, EventArgs e)
        {
            BackColor = Theme.Current.Bg;
            Native.DarkScrollbars(this, Theme.Current);
            Invalidate(true);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) Theme.Changed -= OnTheme;
            base.Dispose(disposing);
        }
    }

    /// <summary>Shared formatting.</summary>
    public static class Fmt
    {
        public static string Ago(DateTime when)
        {
            if (when == DateTime.MinValue) return "unknown";
            var span = DateTime.Now - when;
            if (span.TotalMinutes < 1) return "just now";
            if (span.TotalHours < 1) return (int)span.TotalMinutes + " min ago";
            if (span.TotalDays < 1) return (int)span.TotalHours + " h ago";
            int d = (int)span.TotalDays;
            if (d == 1) return "yesterday";
            if (d < 60) return d + " days ago";
            return (d / 30) + " months ago";
        }

        public static string Date(DateTime d)
        {
            return d == DateTime.MinValue ? "–" : d.ToString("dd MMM yyyy", CultureInfo.InvariantCulture);
        }

        public static string When(DateTime d)
        {
            return d == DateTime.MinValue ? "–" : d.ToString("dd MMM yyyy, HH:mm", CultureInfo.InvariantCulture);
        }

        public static string Plural(int n, string word)
        {
            return n.ToString(CultureInfo.InvariantCulture) + " " + word + (n == 1 ? "" : "s");
        }

        public static string SeverityCounts(Comparison c)
        {
            if (c == null) return "One audit only";
            var parts = SeveritySummary(c);
            return parts.Length == 0 ? (c.Changes.Count == 0 ? "No changes" : "All acknowledged") : parts;
        }

        public static string SeveritySummary(Comparison c)
        {
            var list = new List<string>();
            foreach (var s in SeverityText.All)
            {
                int n = c.Count(s);
                if (n > 0 && s <= Severity.Medium) list.Add(n + " " + s.ToString().ToLowerInvariant());
            }
            if (list.Count == 0)
            {
                int other = c.ActiveCount;
                return other == 0 ? "" : Plural(other, "routine change");
            }
            return string.Join(", ", list);
        }

        public static int[] Counts(Comparison c)
        {
            var a = new int[5];
            if (c == null) return a;
            foreach (var ch in c.Active) a[(int)ch.Severity]++;
            return a;
        }

        public static string Field(Snapshot s, string section, string field)
        {
            var sec = s == null ? null : s.Section(section);
            if (sec == null) return "";
            foreach (var b in sec.Blocks)
            {
                if (b.Kind != BlockKind.Fields) continue;
                var v = b.Field(field);
                if (v != null && !TextUtil.IsBlank(v)) return v;
            }
            return "";
        }

        public static string Model(Snapshot s)
        {
            string m = Field(s, "device", "Model Name");
            if (m.Length == 0) m = Field(s, "device", "Model");
            string make = Field(s, "device", "Manufacturer");
            if (make.Length > 0 && !m.StartsWith(make, StringComparison.OrdinalIgnoreCase) && make.Length < 24) m = TextUtil.TitleCase(make) + " " + m;
            return m.Trim();
        }

        public static string Os(Snapshot s)
        {
            string name = Field(s, "os", "OS Name").Replace("Microsoft ", "");
            string ver = Field(s, "os", "Display Version");
            return (name + (ver.Length > 0 ? " " + ver : "")).Trim();
        }
    }
}
