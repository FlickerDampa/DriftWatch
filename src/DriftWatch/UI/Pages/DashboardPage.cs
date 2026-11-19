using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using DriftWatch.Core;

namespace DriftWatch.UI.Pages
{
    /// <summary>Fleet overview: headline numbers, score trend, open changes and what needs attention.</summary>
    public sealed class DashboardPage : Page
    {
        private readonly WelcomeView welcome;
        private readonly StatTile devicesTile = new StatTile("devices", "Devices monitored");
        private readonly StatTile scoreTile = new StatTile("shield-check", "Fleet security score");
        private readonly StatTile attentionTile = new StatTile("alert", "Need attention");
        private readonly StatTile staleTile = new StatTile("clock", "Overdue audits");
        private readonly Card trendCard = new Card { Title = "Security score over time", Subtitle = "Fleet average, with each PC behind it", Icon = "activity" };
        private readonly LineChart trend = new LineChart();
        private readonly Card donutCard = new Card { Title = "Open changes", Subtitle = "Latest audit of each PC against the one before", Icon = "compare" };
        private readonly Donut donut = new Donut();
        private readonly Card attentionCard = new Card { Title = "Devices by risk", Subtitle = "Riskiest first · double-click to open", Icon = "devices" };
        private readonly GridView attention = new GridView();
        private readonly Card controlsCard = new Card { Title = "Weakest controls", Subtitle = "Share of PCs passing each check", Icon = "shield" };
        private readonly BarList controls = new BarList();

        public DashboardPage(IShell shell) : base(shell)
        {
            welcome = new WelcomeView(shell);
            Controls.Add(welcome);
            foreach (var t in new[] { devicesTile, scoreTile, attentionTile, staleTile }) Controls.Add(t);
            trend.Dock = DockStyle.Fill;
            trendCard.Controls.Add(trend);
            donut.Dock = DockStyle.Fill;
            donutCard.Controls.Add(donut);
            attention.Dock = DockStyle.Fill;
            attentionCard.Controls.Add(attention);
            controls.Dock = DockStyle.Fill;
            controlsCard.Controls.Add(controls);
            Controls.Add(trendCard);
            Controls.Add(donutCard);
            Controls.Add(attentionCard);
            Controls.Add(controlsCard);

            var all = new Btn("All devices", "chevron-right", ButtonStyle.Ghost) { Compact = true, Height = Dpi.S(30) };
            all.FitWidth();
            all.Click += delegate { Shell.Navigate("devices", null); };
            attentionCard.AddHeaderControl(all);
            var posture = new Btn("Posture", "chevron-right", ButtonStyle.Ghost) { Compact = true, Height = Dpi.S(30) };
            posture.FitWidth();
            posture.Click += delegate { Shell.Navigate("posture", null); };
            controlsCard.AddHeaderControl(posture);

            attentionTile.Clickable = true;
            attentionTile.Click += delegate { Shell.Navigate("devices", "attention"); };
            staleTile.Clickable = true;
            staleTile.Click += delegate { Shell.Navigate("devices", "stale"); };
            scoreTile.Clickable = true;
            scoreTile.Click += delegate { Shell.Navigate("posture", null); };
            devicesTile.Clickable = true;
            devicesTile.Click += delegate { Shell.Navigate("devices", null); };

            trend.ValueSuffix = " / 100";
            trend.PointClicked += (s, e) =>
            {
                var snap = e.Point.Tag as Snapshot;
                if (snap != null) Shell.ShowDevice(Ws.DeviceOf(snap), 0);
            };

            attention.RowHeight = Dpi.S(54);
            attention.Columns.Add(new GridColumn("Device", 0) { Fill = 1.1f, MinWidth = 150, Text = o => ((DeviceHistory)o).Device, Paint = a => { var d = (DeviceHistory)a.Item; Cells.TwoLine(a, d.Device, Fmt.Model(d.Latest), Cells.DeviceIcon(d.Latest), null); } });
            attention.Columns.Add(new GridColumn("Score", 96) { SortKey = o => Ws.Posture(((DeviceHistory)o).Latest).Score, Paint = a => Cells.Score(a, Ws.Posture(((DeviceHistory)a.Item).Latest).Score) });
            attention.Columns.Add(new GridColumn("Latest changes", 0) { Fill = 1.6f, MinWidth = 160, SortKey = o => Risk((DeviceHistory)o), Paint = a => Cells.Changes(a, Ws.Latest((DeviceHistory)a.Item)) });
            attention.Columns.Add(new GridColumn("Audited", 96) { Align = Draw.Right, SortKey = o => ((DeviceHistory)o).Latest.Generated, Paint = a => { var d = (DeviceHistory)a.Item; Draw.Text(a.G, Fmt.Ago(d.Latest.Generated), Fonts.Small, Ws.IsStale(d) ? a.T.Warn : a.T.Muted, a.Bounds, Draw.Right); } });
            attention.ItemActivated += delegate { Shell.ShowDevice(attention.SelectedItem as DeviceHistory, 0); };
            attention.Stripe = o => { var c = Ws.Latest((DeviceHistory)o); return c != null && c.Highest.HasValue && c.Highest.Value <= Severity.High ? (Color?)T.For(c.Highest.Value) : null; };

            controls.ItemClicked += (s, e) => Shell.Navigate("posture", e.Item.Tag);
        }

        public override string Key { get { return "dashboard"; } }
        public override string PageTitle { get { return Ws.IsEmpty ? "Welcome" : "Overview"; } }
        public override string PageSubtitle
        {
            get
            {
                if (Ws.IsEmpty) return "Turn DeviceDossier audits into continuous security monitoring";
                return Fmt.Plural(Ws.Devices.Count, "device") + " · " + Fmt.Plural(Ws.ReportCount, "audit") + (string.IsNullOrEmpty(Ws.Folder) ? "" : " · " + Ws.Folder);
            }
        }

        private int Risk(DeviceHistory d) { var c = Ws.Latest(d); return c != null && c.Highest.HasValue ? (int)c.Highest.Value : 9; }

        public override void Activate(object arg)
        {
            RefreshData();
        }

        public override void RefreshData()
        {
            bool empty = Ws.IsEmpty;
            welcome.Visible = empty;
            foreach (Control c in Controls) if (c != welcome) c.Visible = !empty;
            welcome.RefreshData();
            RaiseTitleChanged();
            if (empty) { PerformLayout(); return; }

            var devices = Ws.Devices;
            int n = devices.Count;
            devicesTile.Value = n.ToString(CultureInfo.InvariantCulture);
            var latestAudit = devices.Max(d => d.Latest.Generated);
            devicesTile.Note = Fmt.Plural(Ws.ReportCount, "audit") + " · newest " + Fmt.Ago(latestAudit);

            int avg = (int)Math.Round(Ws.AverageScore);
            scoreTile.Value = avg.ToString(CultureInfo.InvariantCulture);
            scoreTile.Suffix = "/ 100";
            scoreTile.Ring = avg;
            scoreTile.Color = T.ForScore(avg);
            int below = devices.Count(d => Ws.Posture(d.Latest).Score < Ws.Policy.MinimumScore);
            scoreTile.Note = below == 0 ? "Every PC meets the minimum of " + Ws.Policy.MinimumScore : below + " below the minimum of " + Ws.Policy.MinimumScore;
            scoreTile.NoteColor = below == 0 ? T.Ok : T.Warn;

            int attn = devices.Count(d => Ws.NeedsAttention(d));
            attentionTile.Value = attn.ToString(CultureInfo.InvariantCulture);
            attentionTile.Suffix = "of " + n;
            attentionTile.Color = attn > 0 ? T.Bad : T.Ok;
            attentionTile.Note = attn > 0 ? "High-risk changes or a low score" : "Nothing urgent right now";
            attentionTile.NoteColor = attn > 0 ? T.Bad : T.Ok;
            attentionTile.IconName = attn > 0 ? "alert" : "check-circle";

            int stale = devices.Count(d => Ws.IsStale(d));
            staleTile.Value = stale.ToString(CultureInfo.InvariantCulture);
            staleTile.Suffix = "of " + n;
            staleTile.Color = stale > 0 ? T.Warn : T.Ok;
            staleTile.Note = "No audit in " + Ws.Policy.StaleAuditDays + "+ days";
            foreach (var t in new[] { devicesTile, scoreTile, attentionTile, staleTile }) t.Invalidate();

            var series = new List<ChartSeries>();
            int i = 0;
            foreach (var d in devices)
            {
                var s = new ChartSeries(d.Device, Cells.DevicePalette(T, i++), false);
                foreach (var r in d.Reports) s.Points.Add(new ChartPoint(r.Generated, Ws.Posture(r).Score, r));
                series.Add(s);
            }
            var fleet = new ChartSeries("Fleet average", T.Accent, true);
            foreach (var when in Ws.AllReports.Select(r => r.Generated).Distinct().OrderBy(x => x))
            {
                var scores = devices.Select(d => d.Reports.LastOrDefault(r => r.Generated <= when)).Where(r => r != null).Select(r => (double)Ws.Posture(r).Score).ToList();
                if (scores.Count > 0) fleet.Points.Add(new ChartPoint(when, scores.Average(), null));
            }
            series.Add(fleet);
            trend.Threshold = Ws.Policy.MinimumScore;
            trend.ThresholdLabel = "MINIMUM " + Ws.Policy.MinimumScore;
            trend.SetSeries(series);

            var counts = new int[5];
            foreach (var d in devices) { var c = Ws.Latest(d); if (c != null) foreach (var ch in c.Active) counts[(int)ch.Severity]++; }
            donut.CenterText = counts.Sum().ToString(CultureInfo.InvariantCulture);
            donut.CenterCaption = "open changes";
            donut.SetSegments(SeverityText.All.Select(s => new Segment(s.ToString(), counts[(int)s], T.For(s))));

            var ordered = devices.OrderBy(d => { var c = Ws.Latest(d); return c != null && c.Highest.HasValue ? (int)c.Highest.Value : 9; })
                                 .ThenBy(d => Ws.Posture(d.Latest).Score).ToList();
            attention.SetItems(ordered, true);

            var results = devices.Select(d => Ws.Posture(d.Latest)).ToList();
            var bars = new List<BarItem>();
            foreach (var check in results[0].Checks)
            {
                var known = results.Select(r => r.Check(check.Id)).Where(c => c != null && c.Status != CheckStatus.Unknown).ToList();
                if (known.Count == 0) continue;
                int pass = known.Count(c => c.Status == CheckStatus.Pass);
                double rate = pass / (double)known.Count;
                bars.Add(new BarItem(check.Title, rate, pass + " / " + known.Count + " PCs", rate >= 0.999 ? T.Ok : rate >= 0.75 ? T.Accent : rate >= 0.5 ? T.Warn : T.Bad, check.Id));
            }
            controls.EmptyText = "Every control passes on every PC.";
            controls.SetItems(bars.OrderBy(b => b.Fraction).ThenBy(b => b.Label).Take(8));
            PerformLayout();
            Invalidate(true);
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (welcome == null) return;
            int pad = Pad, gap = Gap;
            int w = Width - pad * 2;
            welcome.Bounds = new Rectangle(0, 0, Width, Height);
            if (Ws.IsEmpty) return;
            int y = Dpi.S(4);
            int tw = (w - gap * 3) / 4, th = Dpi.S(132);
            var tiles = new[] { devicesTile, scoreTile, attentionTile, staleTile };
            for (int i = 0; i < 4; i++) tiles[i].Bounds = new Rectangle(pad + i * (tw + gap), y, i == 3 ? w - 3 * (tw + gap) : tw, th);
            y += th + gap;
            int rest = Height - y - Dpi.S(24);
            int row2 = Math.Max(Dpi.S(220), (int)((rest - gap) * 0.47));
            int row3 = Math.Max(Dpi.S(200), rest - gap - row2);
            int left = (int)((w - gap) * 0.62);
            trendCard.Bounds = new Rectangle(pad, y, left, row2);
            donutCard.Bounds = new Rectangle(pad + left + gap, y, w - left - gap, row2);
            y += row2 + gap;
            attentionCard.Bounds = new Rectangle(pad, y, left, row3);
            controlsCard.Bounds = new Rectangle(pad + left + gap, y, w - left - gap, row3);
        }
    }

    /// <summary>The first-run screen: what DriftWatch does and how to start.</summary>
    public sealed class WelcomeView : Surface
    {
        private readonly IShell shell;
        private readonly Btn open, add;
        private readonly List<KeyValuePair<Rectangle, string>> recent = new List<KeyValuePair<Rectangle, string>>();
        private int hover = -1;

        public WelcomeView(IShell shell)
        {
            this.shell = shell;
            open = new Btn("Open a folder of audits", "folder", ButtonStyle.Primary) { Height = Dpi.S(42) };
            open.FitWidth();
            add = new Btn("Compare two reports", "compare", ButtonStyle.Secondary) { Height = Dpi.S(42) };
            add.FitWidth();
            open.Click += delegate { shell.OpenFolderDialog(); };
            add.Click += delegate { shell.AddReportsDialog(); };
            Controls.Add(open);
            Controls.Add(add);
        }

        public void RefreshData() { Invalidate(); PerformLayout(); }

        private Rectangle Hero { get { int pad = Dpi.S(28); return new Rectangle(pad, Dpi.S(4), Width - pad * 2, Dpi.S(300)); } }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            var h = Hero;
            int x = h.X + Dpi.S(40), y = h.Bottom - Dpi.S(42) - Dpi.S(40);
            open.Location = new Point(x, y);
            add.Location = new Point(x + open.Width + Dpi.S(12), y);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = recent.FindIndex(r => r.Key.Contains(e.Location));
            if (h != hover) { hover = h; Cursor = h >= 0 ? Cursors.Hand : Cursors.Default; Invalidate(); }
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            if (hover >= 0 && hover < recent.Count) shell.OpenFolder(recent[hover].Value);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Draw.Smooth(g);
            var h = Hero;
            float rad = Dpi.S(16f);
            Draw.Gradient(g, h, rad, T.Dark ? Theme.Hex("#0E1A33") : Theme.Hex("#EAF6FB"), T.Dark ? Theme.Hex("#1A1440") : Theme.Hex("#F1ECFE"), 20f);
            Draw.Stroke(g, h, rad, T.Line, 1f);
            {
                // A rectangular clip: path clips this large are slow (and fail on Mono).
                var old = g.Clip;
                g.SetClip(new Rectangle(h.X + (int)rad, h.Y + 1, h.Width - (int)rad * 2, h.Height - 2), CombineMode.Intersect);
                float cx = h.Right - Dpi.S(170), cy = h.Y + h.Height / 2f;
                for (int i = 1; i <= 6; i++)
                {
                    float r = Dpi.S(44) * i;
                    using (var pen = new Pen(Theme.Alpha(T.Accent, 46 - i * 6), 1f)) g.DrawEllipse(pen, cx - r, cy - r, r * 2, r * 2);
                }
                using (var pen = new Pen(Theme.Alpha(T.Accent, 30), 1f))
                {
                    g.DrawLine(pen, cx - Dpi.S(280), cy, cx + Dpi.S(280), cy);
                    g.DrawLine(pen, cx, cy - Dpi.S(280), cx, cy + Dpi.S(280));
                }
                var pts = new[] { new PointF(cx - Dpi.S(240), cy + Dpi.S(50)), new PointF(cx - Dpi.S(150), cy + Dpi.S(20)), new PointF(cx - Dpi.S(70), cy + Dpi.S(44)), new PointF(cx, cy - Dpi.S(10)), new PointF(cx + Dpi.S(80), cy - Dpi.S(60)), new PointF(cx + Dpi.S(170), cy - Dpi.S(30)) };
                using (var br = new LinearGradientBrush(new RectangleF(cx - Dpi.S(240), cy - Dpi.S(80), Dpi.S(420), Dpi.S(160)), T.Accent, T.Accent2, 0f))
                using (var pen = new Pen(br, Dpi.S(3f)) { LineJoin = LineJoin.Round }) g.DrawCurve(pen, pts, 0.4f);
                foreach (var p in pts)
                {
                    float d = Dpi.S(9f);
                    using (var b = new SolidBrush(T.Dark ? Theme.Hex("#0E1A33") : Color.White)) g.FillEllipse(b, p.X - d / 2, p.Y - d / 2, d, d);
                    using (var pen = new Pen(T.Accent, Dpi.S(2f))) g.DrawEllipse(pen, p.X - d / 2, p.Y - d / 2, d, d);
                }
                var alert = pts[4];
                float ad = Dpi.S(16f);
                using (var b = new SolidBrush(Theme.Alpha(T.Bad, 60))) g.FillEllipse(b, alert.X - ad, alert.Y - ad, ad * 2, ad * 2);
                using (var b = new SolidBrush(T.Bad)) g.FillEllipse(b, alert.X - Dpi.S(6f), alert.Y - Dpi.S(6f), Dpi.S(12f), Dpi.S(12f));
                g.Clip = old;
            }
            int x = h.X + Dpi.S(40);
            int y = h.Y + Dpi.S(38);
            Draw.Text(g, "SECURITY DRIFT MONITORING", Fonts.Tiny, T.Accent, new Rectangle(x, y, Dpi.S(400), Dpi.S(16)));
            y += Dpi.S(26);
            int tw = Math.Min(Dpi.S(560), h.Width - Dpi.S(420));
            Draw.Text(g, "Know what changed on every PC,", Fonts.H1, T.Ink, new Rectangle(x, y, tw, Fonts.H1.Height + 4));
            y += Fonts.H1.Height + Dpi.S(2);
            Draw.Text(g, "and whether it matters.", Fonts.H1, T.Ink, new Rectangle(x, y, tw, Fonts.H1.Height + 4));
            y += Fonts.H1.Height + Dpi.S(14);
            Draw.Text(g, "DriftWatch reads the XML reports DeviceDossier saves, scores each PC's security, and compares every audit with the last one, so a new admin account, antivirus switched off or a remote-access tool stands out the day it appears.", Fonts.Body, T.Soft, new Rectangle(x, y, tw, Dpi.S(70)), Draw.Wrap);

            int pad = Dpi.S(28), gap = Dpi.S(16);
            int sy = h.Bottom + Dpi.S(20);
            bool showRecent = shell.Settings.RecentFolders.Any(Directory.Exists);
            int cols = 3;
            int areaW = Width - pad * 2;
            int recentW = showRecent ? Math.Max(Dpi.S(300), areaW / 3) : 0;
            int stepsW = areaW - (showRecent ? recentW + gap : 0);
            int sw = (stepsW - gap * (cols - 1)) / cols;
            int sh = Math.Max(Dpi.S(170), Math.Min(Dpi.S(210), Height - sy - Dpi.S(24)));
            var steps = new[]
            {
                new[] { "1", "Audit with DeviceDossier", "Run DeviceDossier on each PC and save the audit as XML, ideally to one shared folder. Re-audit weekly or monthly.", "file" },
                new[] { "2", "Point DriftWatch at the folder", "DriftWatch groups the reports by PC, scores each one and compares it with the audit before. It watches for new reports.", "folder" },
                new[] { "3", "Act on what matters", "Changes are ranked Critical to Info with a plain note on why. Mark expected ones as acknowledged and export reports.", "shield-check" }
            };
            for (int i = 0; i < 3; i++)
            {
                var r = new Rectangle(pad + i * (sw + gap), sy, sw, sh);
                Draw.Fill(g, r, Dpi.S(12), T.Card);
                Draw.Stroke(g, r, Dpi.S(12), T.Line, 1f);
                int bx = r.X + Dpi.S(20), by = r.Y + Dpi.S(20);
                var badge = new Rectangle(bx, by, Dpi.S(34), Dpi.S(34));
                Draw.Fill(g, badge, Dpi.S(9), T.Tint(T.Accent, T.Dark ? 0.16 : 0.1));
                Icons.Draw(g, steps[i][3], new RectangleF(badge.X + Dpi.S(8), badge.Y + Dpi.S(8), Dpi.S(18), Dpi.S(18)), T.Accent, 2f);
                Draw.Text(g, "STEP " + steps[i][0], Fonts.Tiny, T.Faint, new Rectangle(badge.Right + Dpi.S(12), by, Dpi.S(100), badge.Height));
                Draw.Text(g, steps[i][1], Fonts.H3, T.Ink, new Rectangle(bx, by + Dpi.S(48), r.Width - Dpi.S(40), Dpi.S(22)));
                Draw.Text(g, steps[i][2], Fonts.Body, T.Muted, new Rectangle(bx, by + Dpi.S(76), r.Width - Dpi.S(40), r.Height - Dpi.S(96)), Draw.Wrap);
            }
            recent.Clear();
            if (showRecent)
            {
                var r = new Rectangle(pad + stepsW + gap, sy, recentW, sh);
                Draw.Fill(g, r, Dpi.S(12), T.Card);
                Draw.Stroke(g, r, Dpi.S(12), T.Line, 1f);
                Draw.Text(g, "Recent folders", Fonts.H3, T.Ink, new Rectangle(r.X + Dpi.S(20), r.Y + Dpi.S(16), r.Width, Dpi.S(22)));
                int ry = r.Y + Dpi.S(48);
                foreach (var f in shell.Settings.RecentFolders.Where(Directory.Exists).Take(4))
                {
                    var row = new Rectangle(r.X + Dpi.S(10), ry, r.Width - Dpi.S(20), Dpi.S(28));
                    if (ry + row.Height > r.Bottom - Dpi.S(8)) break;
                    recent.Add(new KeyValuePair<Rectangle, string>(row, f));
                    if (recent.Count - 1 == hover) Draw.Fill(g, row, Dpi.S(7), T.CardHover);
                    Icons.Draw(g, "folder", new RectangleF(row.X + Dpi.S(10), row.Y + Dpi.S(6), Dpi.S(15), Dpi.S(15)), T.Muted, 2f);
                    Draw.Text(g, f, Fonts.Body, T.Soft, new Rectangle(row.X + Dpi.S(34), row.Y, row.Width - Dpi.S(40), row.Height), Draw.Left | TextFormatFlags.PathEllipsis);
                    ry += Dpi.S(30);
                }
            }
            Draw.Text(g, "Tip: drag reports or a folder anywhere onto this window. Press Ctrl+K to search.", Fonts.Small, T.Faint, new Rectangle(pad, sy + sh + Dpi.S(14), Width - pad * 2, Dpi.S(18)));
        }
    }
}
