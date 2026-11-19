using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using DriftWatch.Core;
using DriftWatch.Export;

namespace DriftWatch.UI.Pages
{
    /// <summary>Two audits of one PC side by side: every change, riskiest first, with what it means.</summary>
    public sealed class ComparePage : Page
    {
        private Snapshot older, newer;
        private Comparison cmp;
        private readonly ReportSlot olderSlot, newerSlot;
        private readonly Btn swap = new Btn("", "compare", ButtonStyle.Secondary) { Height = Dpi.S(40) };
        private readonly VerdictCard verdict;
        private readonly SeverityTile[] tiles = new SeverityTile[5];
        private readonly Banner notices = new Banner { Kind = BannerKind.Warn };
        private readonly Card listCard = new Card { Title = "Changes", Icon = "compare" };
        private readonly SearchBox search = new SearchBox("Search changes…");
        private readonly ChipBar chips = new ChipBar();
        private readonly GridView grid = new GridView();
        private readonly Card detailCard = new Card();
        private readonly ChangeDetailView detail = new ChangeDetailView();
        private readonly Btn export = new Btn("Export", "download", ButtonStyle.Secondary) { Compact = true, Height = Dpi.S(30) };
        private readonly Card pickCard = new Card { Title = "PCs ready to compare", Subtitle = "Each PC's latest audit against the one before · double-click to open", Icon = "devices" };
        private readonly GridView pick = new GridView();
        private readonly Card tipsCard = new Card { Title = "What you'll see", Subtitle = "Open a folder of audits, or pick two report files above", Icon = "sparkle" };
        private readonly TipsView tips = new TipsView();
        private readonly Btn riskiest = new Btn("Compare the riskiest PC", "alert", ButtonStyle.Primary) { Compact = true, Height = Dpi.S(32) };

        public ComparePage(IShell shell) : base(shell)
        {
            olderSlot = new ReportSlot("OLDER AUDIT", "Baseline");
            newerSlot = new ReportSlot("NEWER AUDIT", "Current");
            verdict = new VerdictCard();
            Controls.Add(olderSlot);
            Controls.Add(newerSlot);
            Controls.Add(swap);
            Controls.Add(verdict);
            for (int i = 0; i < 5; i++)
            {
                var t = new SeverityTile((Severity)i);
                t.Click += delegate { t.Selected = !t.Selected; t.Invalidate(); Fill(); };
                tiles[i] = t;
                Controls.Add(t);
            }
            Controls.Add(notices);

            var add = new Btn("Add report files", "file-plus", ButtonStyle.Secondary) { Compact = true, Height = Dpi.S(32) };
            add.FitWidth();
            add.Click += delegate { Shell.AddReportsDialog(); };
            var openFolder = new Btn("Open folder", "folder", ButtonStyle.Primary) { Compact = true, Height = Dpi.S(32) };
            openFolder.FitWidth();
            openFolder.Click += delegate { Shell.OpenFolderDialog(); };
            tipsCard.AddHeaderControl(add);
            tipsCard.AddHeaderControl(openFolder);
            tips.Dock = DockStyle.Fill;
            tipsCard.Controls.Add(tips);
            Controls.Add(tipsCard);

            riskiest.FitWidth();
            riskiest.Click += delegate
            {
                var d = Ws.Devices.Where(x => x.Previous != null).OrderBy(x => { var c = Ws.Latest(x); return c.Highest.HasValue ? (int)c.Highest.Value : 9; }).FirstOrDefault();
                if (d != null) Load(d.Previous, d.Latest, null);
            };
            pickCard.AddHeaderControl(riskiest);
            pick.Dock = DockStyle.Fill;
            pick.RowHeight = Dpi.S(54);
            pick.Columns.Add(new GridColumn("Device", 0) { Fill = 1.1f, MinWidth = 150, Text = o => ((DeviceHistory)o).Device, Paint = a => { var d = (DeviceHistory)a.Item; Cells.TwoLine(a, d.Device, Fmt.Model(d.Latest), Cells.DeviceIcon(d.Latest), null); } });
            pick.Columns.Add(new GridColumn("Audits compared", 0) { Fill = 1f, MinWidth = 150, SortKey = o => ((DeviceHistory)o).Latest.Generated, Paint = a => { var d = (DeviceHistory)a.Item; Cells.TwoLine(a, Fmt.Date(d.Previous.Generated) + "  →  " + Fmt.Date(d.Latest.Generated), Ws.Latest(d).Span, null, null); } });
            pick.Columns.Add(new GridColumn("Open changes", 120) { SortKey = o => Ws.Latest((DeviceHistory)o).ActiveCount, Paint = a => Cells.Bar(a, Ws.Latest((DeviceHistory)a.Item)) });
            pick.Columns.Add(new GridColumn("Latest changes", 0) { Fill = 1.5f, MinWidth = 160, SortKey = o => { var c = Ws.Latest((DeviceHistory)o); return c.Highest.HasValue ? (int)c.Highest.Value : 9; }, Paint = a => Cells.Changes(a, Ws.Latest((DeviceHistory)a.Item)) });
            pick.Stripe = o => { var c = Ws.Latest((DeviceHistory)o); return c != null && c.Highest.HasValue && c.Highest.Value <= Severity.High ? (Color?)T.For(c.Highest.Value) : null; };
            pick.ItemActivated += delegate { var d = pick.SelectedItem as DeviceHistory; if (d != null) Load(d.Previous, d.Latest, null); };
            pickCard.ContentPadding = Dpi.P(10, 0, 10, 8);
            pickCard.Controls.Add(pick);
            Controls.Add(pickCard);

            swap.Width = Dpi.S(40);
            swap.Click += delegate { if (older != null || newer != null) Load(newer, older, null); };
            olderSlot.Click += delegate { PickMenu(olderSlot, true); };
            newerSlot.Click += delegate { PickMenu(newerSlot, false); };
            olderSlot.FileDropped += (s, path) => LoadFile(path, true);
            newerSlot.FileDropped += (s, path) => LoadFile(path, false);

            listCard.ContentPadding = Dpi.P(10, 82, 10, 8);
            listCard.Controls.Add(search);
            listCard.Controls.Add(chips);
            grid.Dock = DockStyle.Fill;
            grid.RowHeight = Dpi.S(58);
            grid.EmptyTitle = "No changes to show";
            grid.EmptyText = "Nothing matches the current filters.";
            grid.Columns.Add(new GridColumn("Severity", 104) { SortKey = o => (int)C(o).Severity, Paint = a => Cells.SeverityPill(a, C(a.Item).Severity, C(a.Item).Acknowledged) });
            grid.Columns.Add(new GridColumn("Change", 0) { Fill = 1, MinWidth = 200, Text = o => C(o).Title, Paint = a => PaintChange(a, C(a.Item)) });
            grid.Columns.Add(new GridColumn("Category", 100) { Text = o => C(o).Category });
            grid.Columns.Add(new GridColumn("Type", 84) { Text = o => C(o).KindText, Paint = a => { var c = C(a.Item); Draw.Text(a.G, c.KindText, Fonts.Small, c.Kind == ChangeKind.Added ? a.T.Ok : c.Kind == ChangeKind.Removed ? a.T.Bad : a.T.Soft, a.Bounds); } });
            grid.Stripe = o => C(o).Acknowledged ? (Color?)null : T.For(C(o).Severity);
            grid.Dim = o => C(o).Acknowledged;
            grid.SelectionChanged += delegate { detail.SetChange(grid.SelectedItem as Change); };
            grid.RowRightClick += (s, e) => RowMenu(e.Location);
            listCard.Controls.Add(grid);
            export.FitWidth();
            export.Click += delegate { ExportMenu(); };
            listCard.AddHeaderControl(export);
            listCard.Layout += delegate
            {
                int y = listCard.HeaderHeight;
                search.Bounds = new Rectangle(Dpi.S(16), y, Math.Min(Dpi.S(260), listCard.Width / 3), Dpi.S(34));
                chips.Bounds = new Rectangle(Dpi.S(16), y + Dpi.S(40), listCard.Width - Dpi.S(32), Dpi.S(32));
            };
            search.TextChanged2 += delegate { Fill(); };
            chips.SelectionChanged += delegate { Fill(); };

            detail.Dock = DockStyle.Fill;
            detailCard.ContentPadding = Dpi.P(22, 20, 22, 18);
            detailCard.Controls.Add(detail);
            detail.AckButton.Click += delegate { ToggleAck(detail.Change); };
            detail.CopyButton.Click += delegate { var c = detail.Change; if (c != null) Cells.Copy(Shell, MarkdownReport.Change(c, cmp), "Change"); };
            Controls.Add(listCard);
            Controls.Add(detailCard);
        }

        public override string Key { get { return "compare"; } }
        public override string PageTitle { get { return cmp == null ? "Compare" : "Compare · " + cmp.Current.Device; } }
        public override string PageSubtitle
        {
            get
            {
                if (cmp == null) return "What changed between two audits of the same PC";
                return Fmt.When(cmp.Baseline.Generated) + "  →  " + Fmt.When(cmp.Current.Generated) + (cmp.Span.Length > 0 ? " · " + cmp.Span : "");
            }
        }

        private static Change C(object o) { return (Change)o; }

        public override void FocusSearch() { search.FocusInput(); }

        public void Load(Snapshot a, Snapshot b, Change focus)
        {
            older = a;
            newer = b;
            cmp = a != null && b != null ? Ws.Compare(a, b) : null;
            if (cmp != null && cmp.Baseline != a) { older = cmp.Baseline; newer = cmp.Current; }
            foreach (var t in tiles) t.Selected = false;
            search.Value = "";
            chips.SelectedKey = "all";
            RefreshData();
            if (focus != null && cmp != null)
            {
                var match = cmp.Changes.FirstOrDefault(c => c == focus) ?? cmp.Changes.FirstOrDefault(c => c.Title == focus.Title);
                if (match != null) grid.SelectedItem = match;
            }
        }

        private void LoadFile(string path, bool isOlder)
        {
            var errors = new List<string>();
            var added = Ws.AddFiles(new[] { path }, errors);
            if (errors.Count > 0) { Shell.Toast(errors[0], BannerKind.Bad); return; }
            if (added.Count == 0) return;
            if (isOlder) Load(added[0], newer, null); else Load(older, added[0], null);
        }

        public override void RefreshData()
        {
            // After a reload or a policy change, find the same audits again so the view stays put.
            if (older != null) older = Ws.AllReports.FirstOrDefault(r => string.Equals(r.Path, older.Path, StringComparison.OrdinalIgnoreCase)) ?? older;
            if (newer != null) newer = Ws.AllReports.FirstOrDefault(r => string.Equals(r.Path, newer.Path, StringComparison.OrdinalIgnoreCase)) ?? newer;
            cmp = older != null && newer != null ? Ws.Compare(older, newer) : null;
            olderSlot.Snapshot = older;
            newerSlot.Snapshot = newer;
            olderSlot.Score = older == null ? (int?)null : Ws.Posture(older).Score;
            newerSlot.Score = newer == null ? (int?)null : Ws.Posture(newer).Score;
            olderSlot.Invalidate();
            newerSlot.Invalidate();
            bool has = cmp != null;
            verdict.Visible = listCard.Visible = detailCard.Visible = has;
            foreach (var t in tiles) t.Visible = has;
            var ready = Ws.Devices.Where(d => d.Previous != null).ToList();
            pickCard.Visible = !has && ready.Count > 0;
            tipsCard.Visible = !has && ready.Count == 0;
            olderSlot.Large = newerSlot.Large = !has;
            if (!has && ready.Count > 0)
                pick.SetItems(ready.OrderBy(d => { var c = Ws.Latest(d); return c.Highest.HasValue ? (int)c.Highest.Value : 9; }).ThenBy(d => d.Device).ToList(), true);
            if (has)
            {
                verdict.Set(cmp, Ws.Posture(cmp.Baseline).Score, Ws.Posture(cmp.Current).Score);
                for (int i = 0; i < 5; i++) { tiles[i].Count = cmp.Count((Severity)i); tiles[i].Invalidate(); }
                var notes = new List<string>(cmp.Notices);
                if (!string.Equals(cmp.Baseline.Device, cmp.Current.Device, StringComparison.OrdinalIgnoreCase) && !notes.Any(n => n.Contains("different devices")))
                    notes.Add("These reports are from different devices.");
                notices.Text = string.Join("  ", notes);
                notices.Visible = notes.Count > 0;
                var cats = cmp.Changes.Select(c => c.Category).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(c => c).ToList();
                var items = new List<ChipItem> { new ChipItem("all", "All") { Count = cmp.ActiveCount } };
                foreach (var c in cats) items.Add(new ChipItem(c, c) { Count = cmp.Active.Count(x => x.Category == c) });
                items.Add(new ChipItem("#ack", "Acknowledged") { Count = cmp.AcknowledgedCount, Dot = T.Faint });
                chips.SetItems(items);
                Fill();
            }
            else notices.Visible = false;
            RaiseTitleChanged();
            PerformLayout();
            Invalidate(true);
        }

        private void Fill()
        {
            if (cmp == null) return;
            IEnumerable<Change> list = cmp.Changes;
            string key = chips.SelectedKey;
            if (key == "#ack") list = list.Where(c => c.Acknowledged);
            else
            {
                list = list.Where(c => !c.Acknowledged);
                if (key != null && key != "all") list = list.Where(c => c.Category == key);
            }
            var sel = tiles.Where(t => t.Selected).Select(t => t.Severity).ToList();
            if (sel.Count > 0) list = list.Where(c => sel.Contains(c.Severity));
            string q = search.Value.Trim();
            if (q.Length > 0) list = list.Where(c => TextUtil.Contains(c.Title, q) || TextUtil.Contains(c.Item, q) || TextUtil.Contains(c.Before, q) || TextUtil.Contains(c.After, q) || TextUtil.Contains(c.SectionTitle, q));
            var result = list.ToList();
            grid.SetItems(result, true);
            if (grid.SelectedItem == null && result.Count > 0) grid.SelectedIndex = 0;
            detail.SetChange(grid.SelectedItem as Change);
            listCard.Subtitle = result.Count == cmp.Changes.Count ? Fmt.Plural(result.Count, "change") : result.Count + " of " + Fmt.Plural(cmp.Changes.Count, "change");
            listCard.Invalidate();
        }

        private void PaintChange(CellArgs a, Change c)
        {
            int mid = a.Bounds.Y + a.Bounds.Height / 2;
            Draw.Text(a.G, c.Title, Fonts.Semi, c.Acknowledged ? a.T.Muted : a.T.Ink, new Rectangle(a.Bounds.X, mid - Dpi.S(19), a.Bounds.Width, Dpi.S(20)));
            string sub = c.Kind == ChangeKind.Changed ? Change.Display(c.Before) + "  →  " + Change.Display(c.After) : c.SectionTitle + (string.IsNullOrEmpty(c.Table) ? "" : " › " + c.Table);
            if (!string.IsNullOrEmpty(c.PolicyNote)) sub = c.PolicyNote + " · " + sub;
            Draw.Text(a.G, sub, Fonts.Small, a.T.Muted, new Rectangle(a.Bounds.X, mid + Dpi.S(1), a.Bounds.Width, Dpi.S(18)));
        }

        private void ToggleAck(Change c)
        {
            if (c == null || cmp == null) return;
            Ws.Acknowledge(cmp, c, !c.Acknowledged);
            Shell.PolicyChanged(true);
            Shell.Toast(c.Acknowledged ? "Marked as expected. It won't count towards the verdict for " + cmp.Current.Device + "." : "Acknowledgement removed.", BannerKind.Ok);
        }

        private void RowMenu(Point p)
        {
            var c = grid.SelectedItem as Change;
            if (c == null) return;
            var m = MenuRenderer.Create();
            m.Items.Add(MenuRenderer.Item(c.Acknowledged ? "Undo acknowledge" : "Mark as expected", delegate { ToggleAck(c); }));
            m.Items.Add(MenuRenderer.Item("Copy as text", delegate { Cells.Copy(Shell, MarkdownReport.Change(c, cmp), "Change"); }));
            if (c.SectionId == "software" && c.Kind == ChangeKind.Added)
            {
                m.Items.Add(new ToolStripSeparator());
                string name = Workspace.SoftwareKey(c.Item);
                m.Items.Add(MenuRenderer.Item("Ban \"" + name + "\" in the policy", delegate { Ws.Policy.BannedSoftware.Add(name); Shell.PolicyChanged(true); Shell.Toast(name + " is now banned.", BannerKind.Ok); }));
                m.Items.Add(MenuRenderer.Item("Approve \"" + name + "\" in the policy", delegate { Ws.Policy.ApprovedSoftware.Add(name); Shell.PolicyChanged(true); Shell.Toast(name + " is now approved.", BannerKind.Ok); }));
            }
            m.Show(grid, p);
        }

        private void ExportMenu()
        {
            if (cmp == null) return;
            string baseName = "DriftWatch_" + Cells.SafeName(cmp.Current.Device) + "_" + cmp.Current.Generated.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var m = MenuRenderer.Create();
            m.Items.Add(MenuRenderer.Item("HTML report…", delegate { Cells.SaveText(Shell, "Save change report", baseName + ".html", "HTML report (*.html)|*.html", () => HtmlReport.Build(cmp, Ws.Posture(cmp.Baseline), Ws.Posture(cmp.Current))); }));
            m.Items.Add(MenuRenderer.Item("CSV spreadsheet…", delegate { Cells.SaveText(Shell, "Save changes as CSV", baseName + ".csv", "CSV file (*.csv)|*.csv", () => CsvReport.Changes(cmp)); }));
            m.Items.Add(MenuRenderer.Item("JSON…", delegate { Cells.SaveText(Shell, "Save changes as JSON", baseName + ".json", "JSON (*.json)|*.json", () => JsonReport.Build(cmp)); }));
            m.Items.Add(MenuRenderer.Item("Markdown summary…", delegate { Cells.SaveText(Shell, "Save summary", baseName + ".md", "Markdown (*.md)|*.md", () => MarkdownReport.Summary(cmp, Ws.Posture(cmp.Baseline), Ws.Posture(cmp.Current))); }));
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(MenuRenderer.Item("Copy summary for a ticket", delegate { Cells.Copy(Shell, MarkdownReport.Summary(cmp, Ws.Posture(cmp.Baseline), Ws.Posture(cmp.Current)), "Summary"); }));
            m.Show(export, new Point(0, export.Height + Dpi.S(4)));
        }

        private void PickMenu(ReportSlot slot, bool isOlder)
        {
            var m = MenuRenderer.Create();
            var other = isOlder ? newer : older;
            IEnumerable<DeviceHistory> devices = Ws.Devices;
            if (other != null) devices = devices.OrderBy(d => string.Equals(d.Device, other.Device, StringComparison.OrdinalIgnoreCase) ? 0 : 1);
            foreach (var d in devices.Take(30))
            {
                var sub = MenuRenderer.Item(d.Device + "  (" + d.Reports.Count + ")", null);
                foreach (var r in Enumerable.Reverse(d.Reports))
                {
                    var snap = r;
                    var it = MenuRenderer.Item(Fmt.When(r.Generated) + (r == d.Latest ? "  · latest" : "") + "  · score " + Ws.Posture(r).Score, delegate { if (isOlder) Load(snap, newer, null); else Load(older, snap, null); });
                    it.Checked = r == (isOlder ? older : newer);
                    sub.DropDownItems.Add(it);
                }
                sub.DropDown.BackColor = T.Card;
                ((ToolStripDropDownMenu)sub.DropDown).ShowImageMargin = false;
                ((ToolStripDropDownMenu)sub.DropDown).ShowCheckMargin = true;
                m.Items.Add(sub);
            }
            if (Ws.Devices.Count > 0) m.Items.Add(new ToolStripSeparator());
            m.Items.Add(MenuRenderer.Item("Browse for a report file…", delegate
            {
                using (var dlg = new OpenFileDialog { Title = "Choose a DeviceDossier XML report", Filter = "DeviceDossier XML reports (*.xml)|*.xml|All files (*.*)|*.*" })
                    if (dlg.ShowDialog(Shell.Window) == DialogResult.OK) LoadFile(dlg.FileName, isOlder);
            }));
            m.Show(slot, new Point(0, slot.Height + Dpi.S(4)));
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (olderSlot == null || newerSlot == null || verdict == null || tipsCard == null || pickCard == null || tiles[4] == null) return;
            int pad = Pad, gap = Gap, w = Width - pad * 2, y = Dpi.S(4);
            int avail = Height - y - Dpi.S(24);
            int sw = (w - Dpi.S(40) - gap * 2) / 2;
            int tipsH = tipsCard.HeaderHeight + Dpi.S(190) + Dpi.S(16);
            int sh = cmp == null ? (tipsCard.Visible ? Math.Max(Dpi.S(190), avail - tipsH - gap) : Math.Max(Dpi.S(190), Math.Min(Dpi.S(260), (int)(avail * 0.36)))) : Dpi.S(76);
            olderSlot.Bounds = new Rectangle(pad, y, sw, sh);
            swap.Location = new Point(pad + sw + gap, y + (sh - swap.Height) / 2);
            newerSlot.Bounds = new Rectangle(swap.Right + gap, y, w - sw - Dpi.S(40) - gap * 2, sh);
            y += sh + gap;
            if (cmp == null)
            {
                var rest = new Rectangle(pad, y, w, Height - y - Dpi.S(24));
                pickCard.Bounds = rest;
                tipsCard.Bounds = new Rectangle(pad, y, w, Math.Min(rest.Height, tipsH));
                return;
            }
            int vh = Dpi.S(96);
            int vw = Math.Min(Dpi.S(380), (int)(w * 0.34));
            verdict.Bounds = new Rectangle(pad, y, vw, vh);
            int tw = (w - vw - gap * 5) / 5;
            for (int i = 0; i < 5; i++) tiles[i].Bounds = new Rectangle(pad + vw + gap + i * (tw + gap), y, i == 4 ? w - vw - gap - 4 * (tw + gap) - gap : tw, vh);
            y += vh + gap;
            if (notices.Visible)
            {
                int nh = notices.HeightFor(w);
                notices.Bounds = new Rectangle(pad, y, w, nh);
                y += nh + gap;
            }
            int left = (int)((w - gap) * 0.6);
            int h = Height - y - Dpi.S(24);
            listCard.Bounds = new Rectangle(pad, y, left, h);
            detailCard.Bounds = new Rectangle(pad + left + gap, y, w - left - gap, h);
        }

        /// <summary>A drop zone showing which audit is loaded on one side.</summary>
        private sealed class ReportSlot : Surface
        {
            private readonly string label;
            private bool hover, dragOver;

            public ReportSlot(string label, string role)
            {
                this.label = label;
                AllowDrop = true;
                Cursor = Cursors.Hand;
            }

            public Snapshot Snapshot;
            public int? Score;
            /// <summary>The big drop-zone look, used before both audits are chosen.</summary>
            public bool Large;
            public event EventHandler<string> FileDropped;

            protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
            protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

            protected override void OnDragEnter(DragEventArgs e)
            {
                if (e.Data.GetDataPresent(DataFormats.FileDrop)) { e.Effect = DragDropEffects.Copy; dragOver = true; Invalidate(); }
            }

            protected override void OnDragLeave(EventArgs e) { dragOver = false; Invalidate(); }

            protected override void OnDragDrop(DragEventArgs e)
            {
                dragOver = false;
                Invalidate();
                var files = e.Data.GetData(DataFormats.FileDrop) as string[];
                var h = FileDropped;
                if (files != null && files.Length > 0 && File.Exists(files[0]) && h != null) h(this, files[0]);
            }

            private void PaintLarge(Graphics g)
            {
                int box = Dpi.S(60);
                int textH = Dpi.S(16) + Dpi.S(28) + Dpi.S(22) + Dpi.S(20);
                int top = Math.Max(Dpi.S(16), (Height - box - Dpi.S(16) - textH) / 2);
                var ib = new Rectangle((Width - box) / 2, top, box, box);
                using (var glowPath = new System.Drawing.Drawing2D.GraphicsPath())
                {
                    var glow = RectangleF.Inflate(ib, box * 0.5f, box * 0.5f);
                    glowPath.AddEllipse(glow);
                    using (var br = new System.Drawing.Drawing2D.PathGradientBrush(glowPath) { CenterColor = Theme.Alpha(T.Accent, T.Dark ? 36 : 22), SurroundColors = new[] { Theme.Alpha(T.Accent, 0) } }) g.FillPath(br, glowPath);
                }
                Draw.Fill(g, ib, Dpi.S(15), T.Tint(T.Accent, T.Dark ? 0.16 : 0.1));
                Draw.Stroke(g, ib, Dpi.S(15), Theme.Alpha(T.Accent, 90), 1f);
                Icons.Draw(g, Snapshot == null ? "file-plus" : Cells.DeviceIcon(Snapshot), new RectangleF(ib.X + Dpi.S(17), ib.Y + Dpi.S(17), box - Dpi.S(34), box - Dpi.S(34)), T.Accent, 1.8f);
                int y = ib.Bottom + Dpi.S(16);
                Draw.Text(g, label, Fonts.Tiny, T.Muted, new Rectangle(0, y, Width, Dpi.S(16)), Draw.Center);
                y += Dpi.S(22);
                string title = Snapshot == null ? (label.StartsWith("OLDER") ? "Drop the older audit here" : "Drop the newer audit here") : Snapshot.Device + "  ·  " + Fmt.When(Snapshot.Generated);
                Draw.Text(g, title, Fonts.H3, T.Ink, new Rectangle(Dpi.S(16), y, Width - Dpi.S(32), Dpi.S(24)), Draw.Center);
                y += Dpi.S(28);
                string sub = Snapshot == null ? "or click to choose from loaded PCs or a file" : (Snapshot.Administrator ? "Admin audit" : "Limited audit") + (Score.HasValue ? " · score " + Score.Value : "") + " · click to change";
                Draw.Text(g, sub, Fonts.Small, T.Muted, new Rectangle(Dpi.S(16), y, Width - Dpi.S(32), Dpi.S(18)), Draw.Center);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                Draw.Smooth(g);
                var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
                float rad = Dpi.S(12f);
                Draw.Fill(g, r, rad, hover || dragOver ? T.CardHover : T.Card);
                if (dragOver || Snapshot == null)
                    using (var p = Draw.Round(new RectangleF(1, 1, Width - 3, Height - 3), rad))
                    using (var pen = new Pen(dragOver ? T.Accent : T.Line, dragOver ? Dpi.S(2f) : Dpi.S(1.2f)) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash }) g.DrawPath(pen, p);
                else Draw.Stroke(g, r, rad, hover ? Theme.Mix(T.Line, T.Accent, 0.4) : T.Line, 1f);
                if (Large) { PaintLarge(g); return; }
                int box = Dpi.S(44);
                var ib = new Rectangle(Dpi.S(16), (Height - box) / 2, box, box);
                Draw.Fill(g, ib, Dpi.S(11), T.Tint(T.Accent, T.Dark ? 0.14 : 0.1));
                Icons.Draw(g, Snapshot == null ? "file-plus" : Cells.DeviceIcon(Snapshot), new RectangleF(ib.X + Dpi.S(12), ib.Y + Dpi.S(12), box - Dpi.S(24), box - Dpi.S(24)), T.Accent, 2f);
                int x = ib.Right + Dpi.S(14);
                int cs = Dpi.S(14);
                Icons.Draw(g, "chevron-down", new RectangleF(Width - Dpi.S(18) - cs, (Height - cs) / 2f, cs, cs), hover ? T.Soft : T.Muted, 2.2f);
                int right = Width - Dpi.S(18) - cs - Dpi.S(14);
                if (Score.HasValue)
                {
                    int rs = Dpi.S(40);
                    var rr = new RectangleF(right - rs, (Height - rs) / 2f, rs, rs);
                    var c = T.ForScore(Score.Value);
                    Draw.Ring(g, rr, Dpi.S(4f), Score.Value / 100.0, T.LineSoft, c, Theme.Mix(c, T.Accent2, 0.5));
                    Draw.Text(g, Score.Value.ToString(CultureInfo.InvariantCulture), Fonts.SmallSemi, T.Ink, Rectangle.Round(rr), Draw.Center);
                    right -= rs + Dpi.S(12);
                }
                Draw.Text(g, label, Fonts.Tiny, T.Muted, new Rectangle(x, Dpi.S(14), right - x, Dpi.S(16)));
                if (Snapshot == null)
                {
                    Draw.Text(g, "Choose a report", Fonts.Semi, T.Ink, new Rectangle(x, Dpi.S(31), right - x, Dpi.S(20)));
                    Draw.Text(g, "Click to pick, or drop an XML file here", Fonts.Small, T.Muted, new Rectangle(x, Dpi.S(50), right - x, Dpi.S(18)));
                }
                else
                {
                    Draw.Text(g, Snapshot.Device + "  ·  " + Fmt.When(Snapshot.Generated), Fonts.Semi, T.Ink, new Rectangle(x, Dpi.S(31), right - x, Dpi.S(20)));
                    Draw.Text(g, (Snapshot.Administrator ? "Admin audit" : "Limited audit") + " · " + Snapshot.FileName, Fonts.Small, T.Muted, new Rectangle(x, Dpi.S(50), right - x, Dpi.S(18)));
                }
            }
        }

        /// <summary>Three short notes on what a comparison shows, for when nothing is loaded yet.</summary>
        private sealed class TipsView : Surface
        {
            private static readonly string[][] Tips =
            {
                new[] { "shield-check", "Ranked by risk", "Every change gets a level from Critical to Info, with a short note on why it matters and what to check." },
                new[] { "compare", "Before and after", "Old and new values side by side. Mark changes you expected so they stop counting towards the verdict." },
                new[] { "download", "Share the result", "Save an HTML report, CSV or JSON, or copy a short summary straight into a ticket." }
            };

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                int gap = Dpi.S(16);
                int cw = (Width - gap * 2) / 3;
                for (int i = 0; i < 3; i++)
                {
                    var r = new Rectangle(i * (cw + gap), 0, cw, Math.Min(Height - Dpi.S(30), Dpi.S(170)));
                    Draw.Smooth(g);
                    Draw.Fill(g, r, Dpi.S(10), T.Field);
                    Draw.Stroke(g, r, Dpi.S(10), T.LineSoft, 1f);
                    int box = Dpi.S(36);
                    var ib = new Rectangle(r.X + Dpi.S(18), r.Y + Dpi.S(18), box, box);
                    Draw.Fill(g, ib, Dpi.S(9), T.Tint(T.Accent, T.Dark ? 0.16 : 0.1));
                    Icons.Draw(g, Tips[i][0], new RectangleF(ib.X + Dpi.S(9), ib.Y + Dpi.S(9), box - Dpi.S(18), box - Dpi.S(18)), T.Accent, 2f);
                    Draw.Text(g, Tips[i][1], Fonts.H3, T.Ink, new Rectangle(r.X + Dpi.S(18), ib.Bottom + Dpi.S(12), r.Width - Dpi.S(36), Dpi.S(22)));
                    Draw.Text(g, Tips[i][2], Fonts.Body, T.Muted, new Rectangle(r.X + Dpi.S(18), ib.Bottom + Dpi.S(38), r.Width - Dpi.S(36), r.Bottom - ib.Bottom - Dpi.S(44)), Draw.Wrap);
                }
                int ty = Math.Min(Height - Dpi.S(30), Dpi.S(170)) + Dpi.S(10);
                if (ty + Dpi.S(18) < Height)
                    Draw.Text(g, "Tip: drag two reports of the same PC onto the window to compare them straight away.", Fonts.Small, T.Faint, new Rectangle(0, ty, Width, Dpi.S(18)));
            }
        }

        /// <summary>The headline: verdict, span and score movement.</summary>
        private sealed class VerdictCard : Surface
        {
            private Comparison cmp;
            private int before, after;

            public void Set(Comparison c, int scoreBefore, int scoreAfter)
            {
                cmp = c;
                before = scoreBefore;
                after = scoreAfter;
                Invalidate();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                if (cmp == null) return;
                var g = e.Graphics;
                Draw.Smooth(g);
                var h = cmp.Highest;
                Color c = h.HasValue ? T.For(h.Value) : T.Ok;
                var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
                float rad = Dpi.S(12f);
                Draw.Gradient(g, r, rad, T.Tint(c, T.Dark ? 0.16 : 0.1), T.Card, 0f);
                Draw.Stroke(g, r, rad, T.Tint(c, T.Dark ? 0.45 : 0.35), 1f);
                int pad = Dpi.S(18);
                string icon = !h.HasValue ? "check-circle" : h.Value <= Severity.High ? "alert" : "info";
                Icons.Draw(g, icon, new RectangleF(pad, pad, Dpi.S(18), Dpi.S(18)), c, 2f);
                Draw.Text(g, "VERDICT", Fonts.Tiny, T.Muted, new Rectangle(pad + Dpi.S(26), pad, Dpi.S(120), Dpi.S(18)));
                Draw.Text(g, cmp.Verdict, Fonts.H2, T.Ink, new Rectangle(pad, pad + Dpi.S(24), Width - pad * 2 - Dpi.S(110), Fonts.H2.Height + 2));
                string sub = Fmt.Plural(cmp.ActiveCount, "open change") + (cmp.AcknowledgedCount > 0 ? " · " + cmp.AcknowledgedCount + " acknowledged" : "");
                Draw.Text(g, sub, Fonts.Small, T.Muted, new Rectangle(pad, pad + Dpi.S(28) + Fonts.H2.Height, Width - pad * 2 - Dpi.S(110), Dpi.S(18)));
                int delta = after - before;
                int bx = Width - pad - Dpi.S(100);
                Draw.Text(g, "SCORE", Fonts.Tiny, T.Muted, new Rectangle(bx, pad, Dpi.S(100), Dpi.S(18)), Draw.Right);
                Draw.Text(g, before + " → " + after, Fonts.H3, T.Ink, new Rectangle(bx - Dpi.S(20), pad + Dpi.S(22), Dpi.S(120), Dpi.S(24)), Draw.Right);
                if (delta != 0)
                {
                    string d = (delta > 0 ? "▲ +" : "▼ −") + Math.Abs(delta);
                    Draw.Text(g, d, Fonts.SmallSemi, delta > 0 ? T.Ok : T.Bad, new Rectangle(bx, pad + Dpi.S(48), Dpi.S(100), Dpi.S(18)), Draw.Right);
                }
                else Draw.Text(g, "no change", Fonts.Small, T.Muted, new Rectangle(bx, pad + Dpi.S(48), Dpi.S(100), Dpi.S(18)), Draw.Right);
            }
        }

        /// <summary>A count for one severity. Click to filter by it.</summary>
        private sealed class SeverityTile : Surface
        {
            private bool hover;

            public SeverityTile(Severity s)
            {
                Severity = s;
                Cursor = Cursors.Hand;
            }

            public Severity Severity { get; private set; }
            public int Count;
            public bool Selected;

            protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
            protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                Draw.Smooth(g);
                var c = T.For(Severity);
                var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
                float rad = Dpi.S(12f);
                Draw.Fill(g, r, rad, Selected ? T.Tint(c, T.Dark ? 0.2 : 0.12) : hover ? T.CardHover : T.Card);
                Draw.Stroke(g, r, rad, Selected ? c : hover ? Theme.Mix(T.Line, c, 0.5) : T.Line, Selected ? Dpi.S(1.5f) : 1f);
                int pad = Dpi.S(16);
                Draw.Fill(g, new RectangleF(pad, pad + Dpi.S(4), Dpi.S(8), Dpi.S(8)), Dpi.S(2.5f), c);
                Draw.Text(g, Severity.ToString().ToUpperInvariant(), Fonts.Tiny, T.Muted, new Rectangle(pad + Dpi.S(16), pad, Width - pad * 2, Dpi.S(16)));
                Draw.Text(g, Count.ToString(CultureInfo.InvariantCulture), Fonts.Number, Count > 0 ? T.Ink : T.Faint, new Rectangle(pad - Dpi.S(1), pad + Dpi.S(22), Width - pad * 2, Fonts.Number.Height));
                if (Selected) Icons.Draw(g, "filter", new RectangleF(Width - pad - Dpi.S(14), pad, Dpi.S(14), Dpi.S(14)), c, 2f);
            }
        }
    }
}
