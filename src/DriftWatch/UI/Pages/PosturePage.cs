using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using DriftWatch.Core;
using DriftWatch.Export;

namespace DriftWatch.UI.Pages
{
    /// <summary>Security posture across the fleet: a device-by-control matrix, pass rates and details.</summary>
    public sealed class PosturePage : Page
    {
        private readonly Card matrixCard = new Card { Title = "Compliance matrix", Subtitle = "Latest audit of each PC · click a cell for details", Icon = "shield-check" };
        private readonly GridView matrix = new GridView();
        private readonly Legend legend = new Legend();
        private readonly Btn export = new Btn("Export CSV", "download", ButtonStyle.Ghost) { Compact = true, Height = Dpi.S(30) };
        private readonly Card ratesCard = new Card { Title = "Pass rate by control", Subtitle = "Click a control to find the PCs that fail it", Icon = "activity" };
        private readonly BarList rates = new BarList();
        private readonly Card detailCard = new Card();
        private readonly CheckDetailView detail = new CheckDetailView();
        private readonly EmptyState empty;
        private string checkId;
        private List<string> checkIds = new List<string>();

        public PosturePage(IShell shell) : base(shell)
        {
            empty = new EmptyState("shield-check", "No posture data yet", "Open a folder of DeviceDossier reports. Each PC's latest audit is checked against 20 security controls such as antivirus, BitLocker, firewall and patch age, and scored out of 100.");
            var open = new Btn("Open folder", "folder", ButtonStyle.Primary);
            open.Click += delegate { Shell.OpenFolderDialog(); };
            empty.AddAction(open);
            Controls.Add(empty);

            matrix.Dock = DockStyle.Fill;
            matrix.RowHeight = Dpi.S(44);
            matrix.CellClicked += (s, e) =>
            {
                var tag = matrix.Columns[e.Column].Tag as string;
                if (tag != null) checkId = tag;
                ShowDetail();
            };
            matrix.SelectionChanged += delegate { ShowDetail(); };
            matrix.ItemActivated += delegate { Shell.ShowDevice(matrix.SelectedItem as DeviceHistory, 1); };
            matrixCard.ContentPadding = Dpi.P(10, 0, 10, 8);
            matrixCard.Controls.Add(matrix);
            legend.Size = Dpi.S(330, 24);
            export.FitWidth();
            export.Click += delegate { Cells.SaveText(Shell, "Save compliance matrix", "DriftWatch_Posture_" + Cells.Stamp() + ".csv", "CSV file (*.csv)|*.csv", () => CsvReport.Posture(Ws)); };
            matrixCard.AddHeaderControl(legend);
            matrixCard.AddHeaderControl(export);
            Controls.Add(matrixCard);

            rates.Dock = DockStyle.Fill;
            rates.ItemClicked += (s, e) => Focus((string)e.Item.Tag);
            ratesCard.Controls.Add(rates);
            Controls.Add(ratesCard);

            detail.Dock = DockStyle.Fill;
            detail.EmptyText = "Click a cell in the matrix to see the details.";
            detailCard.ContentPadding = Dpi.P(22, 20, 22, 20);
            detailCard.Controls.Add(detail);
            Controls.Add(detailCard);
        }

        public override string Key { get { return "posture"; } }
        public override string PageTitle { get { return "Posture"; } }
        public override string PageSubtitle
        {
            get
            {
                if (Ws.IsEmpty) return "How well each PC meets security best practice";
                int below = Ws.Devices.Count(d => Ws.Posture(d.Latest).Score < Ws.Policy.MinimumScore);
                return "Fleet average " + Math.Round(Ws.AverageScore) + " / 100 · " + (below == 0 ? "every PC meets the minimum" : Fmt.Plural(below, "PC") + " below the minimum of " + Ws.Policy.MinimumScore);
            }
        }

        public override void Activate(object arg)
        {
            RefreshData();
            var id = arg as string;
            if (id != null) Focus(id);
        }

        private void Focus(string id)
        {
            checkId = id;
            var failing = Ws.Devices.OrderBy(d => { var c = Ws.Posture(d.Latest).Check(id); return c == null ? 9 : c.Status == CheckStatus.Fail ? 0 : c.Status == CheckStatus.Warn ? 1 : 2; }).FirstOrDefault();
            if (failing != null) matrix.SelectedItem = failing;
            ShowDetail();
            matrix.Invalidate();
        }

        public override void RefreshData()
        {
            bool none = Ws.IsEmpty;
            empty.Visible = none;
            matrixCard.Visible = ratesCard.Visible = detailCard.Visible = !none;
            RaiseTitleChanged();
            if (none) return;

            var results = Ws.Devices.Select(d => Ws.Posture(d.Latest)).ToList();
            var template = results[0].Checks;
            checkIds = template.Select(c => c.Id).Where(id => results.Any(r => r.Check(id) != null && r.Check(id).Status != CheckStatus.Unknown)).ToList();
            matrix.Columns.Clear();
            matrix.Columns.Add(new GridColumn("Device", 0) { Fill = 1, MinWidth = 130, Text = o => ((DeviceHistory)o).Device, Paint = a => { var d = (DeviceHistory)a.Item; Cells.TwoLine(a, d.Device, Fmt.Ago(d.Latest.Generated), null, null); } });
            matrix.Columns.Add(new GridColumn("Score", 86) { SortKey = o => Ws.Posture(((DeviceHistory)o).Latest).Score, Paint = a => Cells.Score(a, Ws.Posture(((DeviceHistory)a.Item).Latest).Score) });
            foreach (var id in checkIds)
            {
                var t = template.First(c => c.Id == id);
                string cid = id;
                matrix.Columns.Add(new GridColumn(t.Short, 44)
                {
                    Pad = 2,
                    Align = Draw.Center,
                    Tip = t.Title,
                    Tag = cid,
                    SortKey = o => { var c = Ws.Posture(((DeviceHistory)o).Latest).Check(cid); return c == null ? 9 : (int)c.Status; },
                    CellTip = o => { var c = Ws.Posture(((DeviceHistory)o).Latest).Check(cid); return c == null ? "" : c.Title + ": " + c.StatusText + "\n" + c.Detail; },
                    Paint = a => PaintCell(a, (DeviceHistory)a.Item, cid)
                });
            }
            matrix.SetItems(Ws.Devices.OrderBy(d => Ws.Posture(d.Latest).Score).ToList(), true);
            if (matrix.SelectedItem == null && matrix.Count > 0) matrix.SelectedIndex = 0;

            var bars = new List<BarItem>();
            foreach (var id in checkIds)
            {
                var known = results.Select(r => r.Check(id)).Where(c => c != null && c.Status != CheckStatus.Unknown).ToList();
                int pass = known.Count(c => c.Status == CheckStatus.Pass);
                double rate = known.Count == 0 ? 0 : pass / (double)known.Count;
                bars.Add(new BarItem(template.First(c => c.Id == id).Title, rate, pass + " / " + known.Count, rate >= 0.999 ? T.Ok : rate >= 0.75 ? T.Accent : rate >= 0.5 ? T.Warn : T.Bad, id));
            }
            rates.SetItems(bars.OrderBy(b => b.Fraction).ThenBy(b => b.Label));
            if (checkId == null || !checkIds.Contains(checkId))
            {
                var worst = bars.OrderBy(b => b.Fraction).FirstOrDefault();
                checkId = worst == null ? null : (string)worst.Tag;
            }
            ShowDetail();
            PerformLayout();
            Invalidate(true);
        }

        private void PaintCell(CellArgs a, DeviceHistory d, string id)
        {
            var c = Ws.Posture(d.Latest).Check(id);
            if (c == null) return;
            if (a.Selected && id == checkId)
            {
                var r = new Rectangle(a.Bounds.X + (a.Bounds.Width - Dpi.S(30)) / 2, a.Bounds.Y + (a.Bounds.Height - Dpi.S(30)) / 2, Dpi.S(30), Dpi.S(30));
                Draw.Stroke(a.G, r, Dpi.S(8), a.T.Accent, Dpi.S(1.5f));
            }
            Cells.Status(a, c.Status, false);
        }

        private void ShowDetail()
        {
            var d = matrix.SelectedItem as DeviceHistory;
            if (d == null || checkId == null) { detail.SetCheck(null, null, ""); return; }
            var p = Ws.Posture(d.Latest);
            var c = p.Check(checkId);
            detail.SetCheck(c, p, d.Device + " · audited " + Fmt.Date(d.Latest.Generated));
            detailCard.Accent = c != null && c.Status == CheckStatus.Fail ? (Color?)T.Bad : c != null && c.Status == CheckStatus.Warn ? (Color?)T.Warn : null;
            detailCard.Invalidate();
            matrix.Invalidate();
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (empty == null) return;
            int pad = Pad, gap = Gap, w = Width - pad * 2, y = Dpi.S(4);
            empty.Bounds = new Rectangle(0, 0, Width, Height);
            int h = Height - y - Dpi.S(24);
            int top = Math.Max(Dpi.S(260), (int)((h - gap) * 0.56));
            matrixCard.Bounds = new Rectangle(pad, y, w, top);
            y += top + gap;
            int left = (int)((w - gap) * 0.5);
            ratesCard.Bounds = new Rectangle(pad, y, left, h - top - gap);
            detailCard.Bounds = new Rectangle(pad + left + gap, y, w - left - gap, h - top - gap);
        }

        private sealed class Legend : Surface
        {
            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                int x = 0;
                foreach (var s in new[] { CheckStatus.Pass, CheckStatus.Warn, CheckStatus.Fail, CheckStatus.Unknown })
                {
                    var c = T.For(s);
                    Draw.Smooth(g);
                    Draw.Fill(g, new RectangleF(x, (Height - Dpi.S(10)) / 2f, Dpi.S(10), Dpi.S(10)), Dpi.S(3), c);
                    string t = Cells.StatusWord(s);
                    int tw = Draw.Measure(t, Fonts.Small).Width;
                    Draw.Text(g, t, Fonts.Small, T.Muted, new Rectangle(x + Dpi.S(15), 0, tw + 2, Height));
                    x += tw + Dpi.S(30);
                }
            }
        }
    }
}
