using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using DriftWatch.Core;
using DriftWatch.Export;

namespace DriftWatch.UI.Pages
{
    /// <summary>Every PC in the workspace with its score, trend and latest changes.</summary>
    public sealed class DevicesPage : Page
    {
        private readonly SearchBox search = new SearchBox("Filter by name, model, OS or user…");
        private readonly ChipBar chips = new ChipBar();
        private readonly Btn export = new Btn("Export", "download", ButtonStyle.Secondary);
        private readonly Card card = new Card();
        private readonly GridView grid = new GridView();
        private readonly EmptyState empty;

        public DevicesPage(IShell shell) : base(shell)
        {
            empty = new EmptyState("devices", "No devices yet", "Open a folder of DeviceDossier XML reports, or drag reports onto this window. Each PC appears here with its security score and latest changes.");
            var open = new Btn("Open folder", "folder", ButtonStyle.Primary);
            open.Click += delegate { Shell.OpenFolderDialog(); };
            empty.AddAction(open);
            Controls.Add(empty);
            Controls.Add(search);
            Controls.Add(chips);
            Controls.Add(export);
            card.ContentPadding = Dpi.P(10, 6, 10, 8);
            grid.Dock = DockStyle.Fill;
            card.Controls.Add(grid);
            Controls.Add(card);

            chips.SetItems(new[]
            {
                new ChipItem("all", "All"), new ChipItem("attention", "Needs attention") { Dot = T.Bad }, new ChipItem("stale", "Overdue") { Dot = T.Warn },
                new ChipItem("below", "Below minimum"), new ChipItem("laptops", "Laptops"), new ChipItem("desktops", "Desktops"), new ChipItem("single", "One audit")
            });
            chips.SelectionChanged += delegate { Fill(); };
            search.TextChanged2 += delegate { Fill(); };
            export.Click += delegate { ExportMenu(); };

            grid.RowHeight = Dpi.S(58);
            grid.EmptyTitle = "No matching devices";
            grid.EmptyText = "Try another filter or clear the search.";
            grid.Columns.Add(new GridColumn("Device", 0) { Fill = 1.3f, MinWidth = 170, Text = o => D(o).Device, Paint = a => { var d = D(a.Item); Cells.TwoLine(a, d.Device, Fmt.Model(d.Latest), Cells.DeviceIcon(d.Latest), Ws.IsStale(d) ? (Color?)a.T.Warn : null); } });
            grid.Columns.Add(new GridColumn("Score", 100) { SortKey = o => Ws.Posture(D(o).Latest).Score, Paint = a => Cells.Score(a, Ws.Posture(D(a.Item).Latest).Score), Tip = "Security score of the latest audit (0-100)" });
            grid.Columns.Add(new GridColumn("Trend", 110) { SortKey = o => Delta(D(o)), Paint = a => Cells.Spark(a, D(a.Item).Reports.Select(r => (double)Ws.Posture(r).Score).ToList()), Tip = "Score across every audit of this PC" });
            grid.Columns.Add(new GridColumn("Latest changes", 0) { Fill = 1.7f, MinWidth = 180, SortKey = o => Risk(D(o)), Paint = a => Cells.Changes(a, Ws.Latest(D(a.Item))) });
            grid.Columns.Add(new GridColumn("Open", 92) { SortKey = o => { var c = Ws.Latest(D(o)); return c == null ? -1 : c.ActiveCount; }, Paint = a => Cells.Bar(a, Ws.Latest(D(a.Item))), Tip = "Changes in the latest audit that aren't acknowledged, by severity" });
            grid.Columns.Add(new GridColumn("Operating system", 0) { Fill = 0.8f, MinWidth = 120, Text = o => Fmt.Os(D(o).Latest) });
            grid.Columns.Add(new GridColumn("Audits", 64) { Align = Draw.Center, SortKey = o => D(o).Reports.Count, Text = o => D(o).Reports.Count.ToString(CultureInfo.InvariantCulture) });
            grid.Columns.Add(new GridColumn("Last audit", 110) { Align = Draw.Right, SortKey = o => D(o).Latest.Generated, Paint = a => { var d = D(a.Item); int mid = a.Bounds.Y + a.Bounds.Height / 2; Draw.Text(a.G, Fmt.Ago(d.Latest.Generated), Fonts.Semi, Ws.IsStale(d) ? a.T.Warn : a.T.Soft, new Rectangle(a.Bounds.X, mid - Dpi.S(19), a.Bounds.Width, Dpi.S(20)), Draw.Right); Draw.Text(a.G, Fmt.Date(d.Latest.Generated), Fonts.Small, a.T.Muted, new Rectangle(a.Bounds.X, mid + Dpi.S(1), a.Bounds.Width, Dpi.S(18)), Draw.Right); } });
            grid.Stripe = o => { var c = Ws.Latest(D(o)); return c != null && c.Highest.HasValue && c.Highest.Value <= Severity.High ? (Color?)T.For(c.Highest.Value) : null; };
            grid.ItemActivated += delegate { Shell.ShowDevice(grid.SelectedItem as DeviceHistory, 0); };
            grid.RowRightClick += (s, e) => RowMenu(e.Location);
        }

        public override string Key { get { return "devices"; } }
        public override string PageTitle { get { return "Devices"; } }
        public override string PageSubtitle { get { return Ws.IsEmpty ? "Every PC you audit, at a glance" : Fmt.Plural(Ws.Devices.Count, "device") + " · double-click a row for details, right-click for more"; } }

        private static DeviceHistory D(object o) { return (DeviceHistory)o; }

        private int Risk(DeviceHistory d)
        {
            var c = Ws.Latest(d);
            return c != null && c.Highest.HasValue ? 10 - (int)c.Highest.Value : 0;
        }

        private int Delta(DeviceHistory d)
        {
            return d.Previous == null ? 0 : Ws.Posture(d.Latest).Score - Ws.Posture(d.Previous).Score;
        }

        public override void Activate(object arg)
        {
            var key = arg as string;
            if (key != null) chips.SelectedKey = key;
            RefreshData();
        }

        public override void FocusSearch() { search.FocusInput(); }

        public override void RefreshData()
        {
            bool none = Ws.IsEmpty;
            empty.Visible = none;
            search.Visible = chips.Visible = export.Visible = card.Visible = !none;
            RaiseTitleChanged();
            if (none) return;
            chips.SetCount("all", Ws.Devices.Count);
            chips.SetCount("attention", Ws.Devices.Count(d => Ws.NeedsAttention(d)));
            chips.SetCount("stale", Ws.Devices.Count(d => Ws.IsStale(d)));
            chips.SetCount("below", Ws.Devices.Count(d => Ws.Posture(d.Latest).Score < Ws.Policy.MinimumScore));
            chips.SetCount("laptops", Ws.Devices.Count(d => d.Latest.Laptop));
            chips.SetCount("desktops", Ws.Devices.Count(d => !d.Latest.Laptop));
            chips.SetCount("single", Ws.Devices.Count(d => d.Reports.Count == 1));
            Fill();
            PerformLayout();
        }

        private void Fill()
        {
            IEnumerable<DeviceHistory> list = Ws.Devices;
            switch (chips.SelectedKey)
            {
                case "attention": list = list.Where(d => Ws.NeedsAttention(d)); break;
                case "stale": list = list.Where(d => Ws.IsStale(d)); break;
                case "below": list = list.Where(d => Ws.Posture(d.Latest).Score < Ws.Policy.MinimumScore); break;
                case "laptops": list = list.Where(d => d.Latest.Laptop); break;
                case "desktops": list = list.Where(d => !d.Latest.Laptop); break;
                case "single": list = list.Where(d => d.Reports.Count == 1); break;
            }
            string q = search.Value.Trim();
            if (q.Length > 0)
                list = list.Where(d => TextUtil.Contains(d.Device, q) || TextUtil.Contains(Fmt.Model(d.Latest), q) || TextUtil.Contains(Fmt.Os(d.Latest), q) ||
                                       TextUtil.Contains(Fmt.Field(d.Latest, "asset", "Assigned To"), q) || TextUtil.Contains(d.Latest.RunAs, q) ||
                                       TextUtil.Contains(Fmt.Field(d.Latest, "device", "BIOS Serial Number"), q));
            var ordered = list.OrderByDescending(d => Risk(d)).ThenBy(d => Ws.Posture(d.Latest).Score).ThenBy(d => d.Device, StringComparer.OrdinalIgnoreCase).ToList();
            grid.SetItems(ordered, true);
        }

        private void RowMenu(Point p)
        {
            var d = grid.SelectedItem as DeviceHistory;
            if (d == null) return;
            var m = MenuRenderer.Create();
            m.Items.Add(MenuRenderer.Item("Open device details", delegate { Shell.ShowDevice(d, 0); }));
            m.Items.Add(MenuRenderer.Item("Security checks", delegate { Shell.ShowDevice(d, 1); }));
            var cmp = MenuRenderer.Item("Compare latest two audits", delegate { Shell.Compare(d.Previous, d.Latest, null); });
            cmp.Enabled = d.Previous != null;
            m.Items.Add(cmp);
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(MenuRenderer.Item("Show latest report in folder", delegate { Reveal(d.Latest.Path); }));
            m.Items.Add(MenuRenderer.Item("Copy device name", delegate { Cells.Copy(Shell, d.Device, "Device name"); }));
            m.Show(grid, p);
        }

        public static void Reveal(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                if (Native.IsWindows) Process.Start("explorer.exe", "/select,\"" + path + "\"");
                else Process.Start(Path.GetDirectoryName(path));
            }
            catch (Exception) { }
        }

        private void ExportMenu()
        {
            var m = MenuRenderer.Create();
            m.Items.Add(MenuRenderer.Item("Fleet report (HTML)…", delegate { Cells.SaveText(Shell, "Save fleet report", "DriftWatch_Fleet_" + Cells.Stamp() + ".html", "HTML report (*.html)|*.html", () => HtmlReport.BuildFleet(Ws)); }));
            m.Items.Add(MenuRenderer.Item("Device list (CSV)…", delegate { Cells.SaveText(Shell, "Save device list", "DriftWatch_Devices_" + Cells.Stamp() + ".csv", "CSV file (*.csv)|*.csv", () => CsvReport.Devices(Ws)); }));
            m.Items.Add(MenuRenderer.Item("Fleet data (JSON)…", delegate { Cells.SaveText(Shell, "Save fleet data", "DriftWatch_Fleet_" + Cells.Stamp() + ".json", "JSON (*.json)|*.json", () => JsonReport.BuildFleet(Ws)); }));
            m.Show(export, new Point(0, export.Height + Dpi.S(4)));
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (empty == null) return;
            int pad = Pad, y = Dpi.S(4), w = Width - pad * 2;
            empty.Bounds = new Rectangle(0, 0, Width, Height);
            search.Bounds = new Rectangle(pad, y, Math.Min(Dpi.S(260), w / 4), Dpi.S(36));
            export.Location = new Point(Width - pad - export.Width, y);
            chips.Bounds = new Rectangle(search.Right + Dpi.S(14), y + Dpi.S(2), export.Left - search.Right - Dpi.S(28), Dpi.S(32));
            y += Dpi.S(36) + Gap;
            card.Bounds = new Rectangle(pad, y, w, Height - y - Dpi.S(24));
        }
    }
}
