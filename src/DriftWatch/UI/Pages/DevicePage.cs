using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using DriftWatch.Core;

namespace DriftWatch.UI.Pages
{
    /// <summary>One PC: identity, score, security checks, audit timeline and the raw report data.</summary>
    public sealed class DevicePage : Page
    {
        private DeviceHistory device;
        private Snapshot shown;
        private readonly DeviceHeader header;
        private readonly Gauge gauge = new Gauge();
        private readonly Btn back = new Btn("All devices", "chevron-left", ButtonStyle.Ghost) { Compact = true, Height = Dpi.S(30) };
        private readonly Tabs tabs = new Tabs("Overview", "Security checks", "Audit timeline", "Report data");
        private readonly Surface[] panes = new Surface[4];

        private readonly Card historyCard = new Card { Title = "Score history", Subtitle = "Click a point to view that audit", Icon = "activity" };
        private readonly LineChart history = new LineChart();
        private readonly Card factsCard = new Card { Title = "System", Icon = "cpu" };
        private readonly FactsView facts = new FactsView();
        private readonly Card latestCard = new Card { Title = "Latest changes", Icon = "compare" };
        private readonly GridView latest = new GridView();

        private readonly ChipBar checkChips = new ChipBar();
        private readonly Card checksCard = new Card();
        private readonly GridView checks = new GridView();
        private readonly Card checkDetailCard = new Card();
        private readonly CheckDetailView checkDetail = new CheckDetailView();

        private readonly Card timelineCard = new Card { Title = "Audits", Subtitle = "Newest first · select one, then compare", Icon = "timeline" };
        private readonly GridView timeline = new GridView();
        private readonly Btn cmpPrev = new Btn("Compare with previous", "compare", ButtonStyle.Secondary) { Compact = true, Height = Dpi.S(32) };
        private readonly Btn cmpLatest = new Btn("Compare with latest", "compare", ButtonStyle.Secondary) { Compact = true, Height = Dpi.S(32) };
        private readonly Btn reveal = new Btn("Show file", "folder", ButtonStyle.Ghost) { Compact = true, Height = Dpi.S(32) };

        private readonly Card sectionsCard = new Card { Title = "Sections", Icon = "file" };
        private readonly GridView sections = new GridView();
        private readonly Card dataCard = new Card();
        private readonly ChipBar blockChips = new ChipBar();
        private readonly GridView data = new GridView();

        public DevicePage(IShell shell) : base(shell)
        {
            header = new DeviceHeader(this);
            Controls.Add(header);
            header.Controls.Add(gauge);
            header.Controls.Add(back);
            back.FitWidth();
            back.Click += delegate { Shell.Navigate("devices", null); };
            Controls.Add(tabs);
            tabs.SelectionChanged += delegate { ShowTab(); };
            for (int i = 0; i < panes.Length; i++) { panes[i] = new PaneHost(); Controls.Add(panes[i]); }

            history.Dock = DockStyle.Fill;
            history.ValueSuffix = " / 100";
            historyCard.Controls.Add(history);
            history.PointClicked += (s, e) => { var snap = e.Point.Tag as Snapshot; if (snap != null) ShowAudit(snap); };
            facts.Dock = DockStyle.Fill;
            factsCard.Controls.Add(facts);
            latest.Dock = DockStyle.Fill;
            latest.RowHeight = Dpi.S(54);
            latest.ShowHeader = false;
            latest.EmptyTitle = "No open changes";
            latest.EmptyText = "Nothing changed since the audit before, or every change is acknowledged.";
            latest.Columns.Add(new GridColumn("Severity", 100) { Paint = a => Cells.SeverityPill(a, ((Change)a.Item).Severity, false) });
            latest.Columns.Add(new GridColumn("Change", 0) { Fill = 1, Paint = a => { var c = (Change)a.Item; Cells.TwoLine(a, c.Title, c.Category + " · " + c.SectionTitle, null, null); } });
            latest.Stripe = o => T.For(((Change)o).Severity);
            latest.ItemActivated += delegate { OpenChange(latest.SelectedItem as Change); };
            latestCard.Controls.Add(latest);
            var openCompare = new Btn("Open in Compare", "chevron-right", ButtonStyle.Ghost) { Compact = true, Height = Dpi.S(30) };
            openCompare.FitWidth();
            openCompare.Click += delegate { OpenChange(latest.SelectedItem as Change); };
            latestCard.AddHeaderControl(openCompare);
            panes[0].Controls.Add(historyCard);
            panes[0].Controls.Add(factsCard);
            panes[0].Controls.Add(latestCard);

            checkChips.SetItems(new[] { new ChipItem("all", "All"), new ChipItem("fail", "Failing") { Dot = T.Bad }, new ChipItem("warn", "Warnings") { Dot = T.Warn }, new ChipItem("pass", "Passing") { Dot = T.Ok }, new ChipItem("unknown", "Not reported") });
            checkChips.SelectionChanged += delegate { FillChecks(); };
            checks.Dock = DockStyle.Fill;
            checks.RowHeight = Dpi.S(56);
            checks.Columns.Add(new GridColumn("Status", 120) { SortKey = o => (int)((PostureCheck)o).Status, Paint = a => Cells.Status(a, ((PostureCheck)a.Item).Status, true) });
            checks.Columns.Add(new GridColumn("Check", 0) { Fill = 1.2f, Text = o => ((PostureCheck)o).Title, Paint = a => { var c = (PostureCheck)a.Item; Cells.TwoLine(a, c.Title, c.Group, null, null); } });
            checks.Columns.Add(new GridColumn("What the audit shows", 0) { Fill = 1.6f, Text = o => ((PostureCheck)o).Detail, CellTip = o => ((PostureCheck)o).Detail });
            checks.SelectionChanged += delegate { checkDetail.SetCheck(checks.SelectedItem as PostureCheck, Ws.Posture(shown), shown == null ? "" : shown.Device + " · " + Fmt.Date(shown.Generated)); };
            checksCard.ContentPadding = Dpi.P(10, 6, 10, 8);
            checksCard.Controls.Add(checks);
            checkDetail.Dock = DockStyle.Fill;
            checkDetailCard.ContentPadding = Dpi.P(22, 20, 22, 20);
            checkDetailCard.Controls.Add(checkDetail);
            panes[1].Controls.Add(checkChips);
            panes[1].Controls.Add(checksCard);
            panes[1].Controls.Add(checkDetailCard);

            timeline.Dock = DockStyle.Fill;
            timeline.RowHeight = Dpi.S(56);
            timeline.Columns.Add(new GridColumn("Audit", 0) { Fill = 1, MinWidth = 150, SortKey = o => ((Snapshot)o).Generated, Paint = a => { var s = (Snapshot)a.Item; Cells.TwoLine(a, Fmt.When(s.Generated), Fmt.Ago(s.Generated) + (s == device.Latest ? " · latest" : ""), "file", s == shown ? (Color?)a.T.Accent : a.T.Info); } });
            timeline.Columns.Add(new GridColumn("Score", 150) { SortKey = o => Ws.Posture((Snapshot)o).Score, Paint = a => PaintScoreDelta(a, (Snapshot)a.Item) });
            timeline.Columns.Add(new GridColumn("Changes from the audit before", 0) { Fill = 1.4f, MinWidth = 160, Paint = a => PaintAuditChanges(a, (Snapshot)a.Item) });
            timeline.Columns.Add(new GridColumn("Run as", 0) { Fill = 0.7f, MinWidth = 110, Paint = a => { var s = (Snapshot)a.Item; Cells.TwoLine(a, s.RunAs.Length > 0 ? s.RunAs : "Unknown", s.Administrator ? "Administrator" : "Limited rights", null, null); } });
            timeline.Columns.Add(new GridColumn("File", 0) { Fill = 0.9f, MinWidth = 110, Text = o => ((Snapshot)o).FileName, CellTip = o => ((Snapshot)o).Path });
            timeline.SelectionChanged += delegate { UpdateTimelineButtons(); };
            timeline.ItemActivated += delegate { var s = timeline.SelectedItem as Snapshot; if (s != null) ShowAudit(s); };
            timelineCard.Controls.Add(timeline);
            cmpPrev.FitWidth();
            cmpLatest.FitWidth();
            reveal.FitWidth();
            timelineCard.AddHeaderControl(reveal);
            timelineCard.AddHeaderControl(cmpLatest);
            timelineCard.AddHeaderControl(cmpPrev);
            cmpPrev.Click += delegate { var s = timeline.SelectedItem as Snapshot; int i = device.Reports.IndexOf(s); if (i > 0) Shell.Compare(device.Reports[i - 1], s, null); };
            cmpLatest.Click += delegate { var s = timeline.SelectedItem as Snapshot; if (s != null && s != device.Latest) Shell.Compare(s, device.Latest, null); };
            reveal.Click += delegate { var s = timeline.SelectedItem as Snapshot; if (s != null) DevicesPage.Reveal(s.Path); };
            panes[2].Controls.Add(timelineCard);

            sections.Dock = DockStyle.Fill;
            sections.ShowHeader = false;
            sections.RowHeight = Dpi.S(44);
            sections.Columns.Add(new GridColumn("Section", 0) { Fill = 1, Paint = a => PaintSection(a, (SnapshotSection)a.Item) });
            sections.SelectionChanged += delegate { FillBlocks(); };
            sectionsCard.ContentPadding = Dpi.P(10, 0, 10, 8);
            sectionsCard.Controls.Add(sections);
            blockChips.SelectionChanged += delegate { FillData(); };
            data.Dock = DockStyle.Fill;
            data.RowHeight = Dpi.S(36);
            data.Zebra = true;
            dataCard.ContentPadding = Dpi.P(10, 44, 10, 8);
            dataCard.Controls.Add(blockChips);
            dataCard.Controls.Add(data);
            panes[3].Controls.Add(sectionsCard);
            panes[3].Controls.Add(dataCard);
        }

        private sealed class PaneHost : Surface { }

        public override string Key { get { return "device"; } }
        public override string PageTitle { get { return device == null ? "Device" : device.Device; } }
        public override string PageSubtitle
        {
            get
            {
                if (device == null) return "";
                string s = Fmt.Plural(device.Reports.Count, "audit") + " · first " + Fmt.Date(device.Reports[0].Generated) + " · latest " + Fmt.Ago(device.Latest.Generated);
                if (shown != null && shown != device.Latest) s += " · viewing the audit from " + Fmt.Date(shown.Generated);
                return s;
            }
        }

        public DeviceHistory Device { get { return device; } }
        public Snapshot Shown { get { return shown; } }
        public Workspace Workspace { get { return Ws; } }

        public override void Activate(object arg)
        {
            var parts = arg as object[];
            if (parts != null && parts.Length > 0 && parts[0] is DeviceHistory)
            {
                device = (DeviceHistory)parts[0];
                shown = device.Latest;
                if (parts.Length > 1 && parts[1] is int) tabs.SelectedIndex = (int)parts[1];
            }
            RefreshData();
        }

        public override void RefreshData()
        {
            if (device != null)
            {
                // The workspace may have been reloaded: find the same device again.
                var fresh = Ws.Find(device.Device);
                if (fresh == null) { device = null; shown = null; }
                else if (fresh != device)
                {
                    DateTime when = shown != null ? shown.Generated : DateTime.MinValue;
                    device = fresh;
                    shown = device.Reports.FirstOrDefault(r => r.Generated == when) ?? device.Latest;
                }
            }
            if (device == null) { RaiseTitleChanged(); return; }
            var p = Ws.Posture(shown);
            gauge.SetScore(p.Score, Visible);
            header.Invalidate();

            var s = new ChartSeries(device.Device, T.Accent, true);
            foreach (var r in device.Reports) s.Points.Add(new ChartPoint(r.Generated, Ws.Posture(r).Score, r));
            history.Threshold = Ws.Policy.MinimumScore;
            history.ThresholdLabel = "MINIMUM " + Ws.Policy.MinimumScore;
            history.EmptyText = "One audit so far.";
            history.SetSeries(new[] { s });
            facts.SetFacts(Facts(shown));
            int idx = device.Reports.IndexOf(shown);
            var cmp = idx > 0 ? Ws.Compare(device.Reports[idx - 1], shown) : null;
            latestCard.Subtitle = cmp == null ? "This is the first audit of this PC" : "Since " + Fmt.Date(cmp.Baseline.Generated) + " · " + Fmt.SeverityCounts(cmp);
            latest.SetItems(cmp == null ? new List<Change>() : cmp.Active.ToList(), false);
            if (cmp == null) { latest.EmptyTitle = "Nothing to compare yet"; latest.EmptyText = "Audit this PC again with DeviceDossier and the changes appear here."; }
            else { latest.EmptyTitle = "No open changes"; latest.EmptyText = "Nothing changed since the audit before, or every change is acknowledged."; }

            checkChips.SetCount("all", p.Checks.Count);
            checkChips.SetCount("fail", p.Failed);
            checkChips.SetCount("warn", p.Warnings);
            checkChips.SetCount("pass", p.Passed);
            checkChips.SetCount("unknown", p.Checks.Count(c => c.Status == CheckStatus.Unknown));
            FillChecks();
            checkDetailCard.Accent = p.Failed > 0 ? (Color?)T.Bad : null;

            timeline.SetItems(Enumerable.Reverse(device.Reports).ToList(), true);
            if (timeline.SelectedItem == null) timeline.SelectedItem = shown;
            UpdateTimelineButtons();

            sectionsCard.Subtitle = "";
            sections.SetItems(shown.Sections, true);
            if (sections.SelectedItem == null && shown.Sections.Count > 0) sections.SelectedIndex = 0;
            FillBlocks();

            ShowTab();
            RaiseTitleChanged();
            PerformLayout();
            Invalidate(true);
        }

        private void ShowAudit(Snapshot s)
        {
            shown = s;
            RefreshData();
        }

        private void OpenChange(Change c)
        {
            int idx = device.Reports.IndexOf(shown);
            if (idx <= 0) return;
            Shell.Compare(device.Reports[idx - 1], shown, c);
        }

        private void ShowTab()
        {
            for (int i = 0; i < panes.Length; i++) panes[i].Visible = i == tabs.SelectedIndex;
        }

        private IEnumerable<KeyValuePair<string, string>> Facts(Snapshot s)
        {
            var cpu = FirstRow(s, "cpu");
            var gpu = FirstRow(s, "gpu");
            string ram = TitleTotal(s, "ram");
            var disks = Rows(s, "storage").Select(r => Val(r, "Drive Model") + " (" + Math.Round(TextUtil.Number(Val(r, "Size (GB)")) ?? 0) + " GB)").ToList();
            var ips = Rows(s, "network").Select(r => Val(r, "IP Address")).Where(v => !TextUtil.IsBlank(v)).ToList();
            yield return Kv("Model", Fmt.Model(s));
            yield return Kv("Serial number", Fmt.Field(s, "device", "BIOS Serial Number"));
            yield return Kv("Operating system", Fmt.Os(s) + (Fmt.Field(s, "os", "Build Number").Length > 0 ? " (build " + Fmt.Field(s, "os", "Build Number") + ")" : ""));
            yield return Kv("Processor", cpu == null ? "" : Val(cpu, "CPU Model").Replace("(R)", "").Replace("(TM)", ""));
            yield return Kv("Memory", ram);
            yield return Kv("Graphics", gpu == null ? "" : Val(gpu, "GPU Name"));
            yield return Kv("Storage", string.Join(", ", disks));
            yield return Kv("IP address", string.Join(", ", ips));
            yield return Kv("Domain / workgroup", Fmt.Field(s, "device", "Domain / Workgroup"));
            yield return Kv("Assigned to", Fmt.Field(s, "asset", "Assigned To"));
            yield return Kv("Asset tag", Fmt.Field(s, "asset", "Asset Tag"));
            yield return Kv("Windows installed", Fmt.Field(s, "os", "Install Date"));
            yield return Kv("BIOS", Fmt.Field(s, "device", "BIOS Version") + (Fmt.Field(s, "device", "BIOS Release Date").Length > 0 ? " · " + Fmt.Field(s, "device", "BIOS Release Date") : ""));
            yield return Kv("Audited by", s.RunAs + (s.Administrator ? " (administrator)" : " (limited rights)"));
        }

        private static KeyValuePair<string, string> Kv(string k, string v) { return new KeyValuePair<string, string>(k, (v ?? "").Trim().Trim('·').Trim()); }

        private static IEnumerable<Dictionary<string, string>> Rows(Snapshot s, string section)
        {
            var sec = s.Section(section);
            if (sec == null) return Enumerable.Empty<Dictionary<string, string>>();
            return sec.Blocks.Where(b => b.Kind == BlockKind.Table).SelectMany(b => b.Rows).Where(r => !PostureEngine.Placeholder(r.Values.FirstOrDefault()));
        }

        private static Dictionary<string, string> FirstRow(Snapshot s, string section) { return Rows(s, section).FirstOrDefault(); }
        private static string Val(Dictionary<string, string> r, string c) { return PostureEngine.Cell(r, c); }

        private static string TitleTotal(Snapshot s, string section)
        {
            var sec = s.Section(section);
            if (sec == null) return "";
            int i = sec.Title.IndexOf("Total:", StringComparison.OrdinalIgnoreCase);
            if (i < 0) return "";
            return sec.Title.Substring(i + 6).Replace(" - Sticks:", ",").Trim() + (sec.Title.Contains("Sticks") ? " sticks" : "");
        }

        private void FillChecks()
        {
            var p = Ws.Posture(shown);
            if (p == null) return;
            IEnumerable<PostureCheck> list = p.Checks;
            switch (checkChips.SelectedKey)
            {
                case "fail": list = list.Where(c => c.Status == CheckStatus.Fail); break;
                case "warn": list = list.Where(c => c.Status == CheckStatus.Warn); break;
                case "pass": list = list.Where(c => c.Status == CheckStatus.Pass); break;
                case "unknown": list = list.Where(c => c.Status == CheckStatus.Unknown); break;
            }
            var ordered = list.OrderBy(c => c.Status == CheckStatus.Fail ? 0 : c.Status == CheckStatus.Warn ? 1 : c.Status == CheckStatus.Pass ? 2 : 3).ThenByDescending(c => c.Weight).ToList();
            object keepId = checks.SelectedItem is PostureCheck ? ((PostureCheck)checks.SelectedItem).Id : null;
            checks.SetItems(ordered, false);
            var again = ordered.FirstOrDefault(c => (string)keepId == c.Id) ?? ordered.FirstOrDefault();
            checks.SelectedItem = again;
            checkDetail.SetCheck(again, p, shown.Device + " · " + Fmt.Date(shown.Generated));
        }

        private void UpdateTimelineButtons()
        {
            var s = timeline.SelectedItem as Snapshot;
            int i = s == null ? -1 : device.Reports.IndexOf(s);
            cmpPrev.Enabled = i > 0;
            cmpLatest.Enabled = s != null && s != device.Latest;
            reveal.Enabled = s != null;
        }

        private void PaintScoreDelta(CellArgs a, Snapshot s)
        {
            int score = Ws.Posture(s).Score;
            Cells.Score(a, score);
            int i = device.Reports.IndexOf(s);
            if (i <= 0) return;
            int delta = score - Ws.Posture(device.Reports[i - 1]).Score;
            if (delta == 0) return;
            string t = (delta > 0 ? "+" : "−") + Math.Abs(delta);
            var c = delta > 0 ? a.T.Ok : a.T.Bad;
            int tw = Draw.Measure(t, Fonts.SmallSemi).Width;
            Draw.Text(a.G, t, Fonts.SmallSemi, c, new Rectangle(a.Bounds.Right - tw - Dpi.S(4), a.Bounds.Y, tw + Dpi.S(4), a.Bounds.Height), Draw.Right);
        }

        private void PaintAuditChanges(CellArgs a, Snapshot s)
        {
            int i = device.Reports.IndexOf(s);
            if (i <= 0) { Draw.Text(a.G, "First audit", Fonts.Small, a.T.Faint, a.Bounds); return; }
            var c = Ws.Compare(device.Reports[i - 1], s);
            var bar = new Rectangle(a.Bounds.X, a.Bounds.Y + a.Bounds.Height / 2 - Dpi.S(3), Math.Min(Dpi.S(90), a.Bounds.Width / 3), Dpi.S(6));
            Draw.SeverityBar(a.G, bar, Fmt.Counts(c), a.T);
            Draw.Text(a.G, Fmt.SeverityCounts(c), Fonts.Small, c.AnyAtOrAbove(Severity.High) ? a.T.Ink : a.T.Muted, new Rectangle(bar.Right + Dpi.S(12), a.Bounds.Y, a.Bounds.Right - bar.Right - Dpi.S(12), a.Bounds.Height));
        }

        private void PaintSection(CellArgs a, SnapshotSection s)
        {
            Color c = s.State == "Ok" ? a.T.Ok : s.State == "Warning" ? a.T.Warn : s.State == "Error" ? a.T.Bad : a.T.Faint;
            int d = Dpi.S(8);
            Draw.Smooth(a.G);
            using (var b = new SolidBrush(c)) a.G.FillEllipse(b, a.Bounds.X + Dpi.S(4), a.Bounds.Y + (a.Bounds.Height - d) / 2, d, d);
            Draw.Text(a.G, s.CleanTitle, a.Selected ? Fonts.Semi : Fonts.Body, a.T.Ink, new Rectangle(a.Bounds.X + Dpi.S(22), a.Bounds.Y, a.Bounds.Width - Dpi.S(60), a.Bounds.Height));
            int rows = s.Blocks.Sum(b => b.Kind == BlockKind.Table ? b.Rows.Count : b.Fields.Count);
            Draw.Text(a.G, rows.ToString(CultureInfo.InvariantCulture), Fonts.Small, a.T.Faint, a.Bounds, Draw.Right);
        }

        private void FillBlocks()
        {
            var sec = sections.SelectedItem as SnapshotSection;
            var items = new List<ChipItem>();
            if (sec != null)
                for (int i = 0; i < sec.Blocks.Count; i++)
                {
                    var b = sec.Blocks[i];
                    string name = b.Caption.Length > 0 ? b.Caption : (b.Kind == BlockKind.Table ? "Table" : "Details");
                    if (name.StartsWith("Known servers", StringComparison.OrdinalIgnoreCase)) name = "Mapped drives";
                    items.Add(new ChipItem(i.ToString(CultureInfo.InvariantCulture), name) { Count = b.Kind == BlockKind.Table ? b.Rows.Count : b.Fields.Count });
                }
            blockChips.SetItems(items);
            blockChips.SelectedKey = "0";
            dataCard.Title = sec == null ? "" : sec.CleanTitle;
            dataCard.Subtitle = sec == null ? "" : (sec.State != "Ok" ? sec.State + (sec.Note.Length > 0 ? ": " + sec.Note : "") : "");
            dataCard.Layout -= DataCardLayout;
            dataCard.Layout += DataCardLayout;
            FillData();
            dataCard.PerformLayout();
            dataCard.Invalidate();
        }

        private void DataCardLayout(object sender, LayoutEventArgs e)
        {
            blockChips.Bounds = new Rectangle(Dpi.S(16), dataCard.HeaderHeight, dataCard.Width - Dpi.S(32), Dpi.S(32));
        }

        private void FillData()
        {
            var sec = sections.SelectedItem as SnapshotSection;
            data.Columns.Clear();
            int bi;
            if (sec == null || !int.TryParse(blockChips.SelectedKey ?? "0", out bi) || bi >= sec.Blocks.Count) { data.SetItems(new object[0], false); return; }
            var b = sec.Blocks[bi];
            if (b.Kind == BlockKind.Fields)
            {
                data.Columns.Add(new GridColumn("Field", 220) { Text = o => ((KeyValuePair<string, string>)o).Key, Paint = a => Draw.Text(a.G, ((KeyValuePair<string, string>)a.Item).Key, Fonts.Body, a.T.Muted, a.Bounds) });
                data.Columns.Add(new GridColumn("Value", 0) { Fill = 1, Text = o => ((KeyValuePair<string, string>)o).Value, CellTip = o => ((KeyValuePair<string, string>)o).Value, Paint = a => Draw.Text(a.G, ((KeyValuePair<string, string>)a.Item).Value, Fonts.Mono, a.T.Ink, a.Bounds) });
                data.SetItems(b.Fields.Cast<object>().ToList(), false);
            }
            else
            {
                bool first = true;
                foreach (var col in b.Columns)
                {
                    string name = col;
                    int w = Math.Max(80, Math.Min(260, Draw.Measure(col, Fonts.Tiny).Width / Math.Max(1, (int)Dpi.Factor) + 40));
                    data.Columns.Add(new GridColumn(col, w) { Fill = first ? 1.5f : (b.Columns.Count <= 4 ? 1f : 0), MinWidth = 120, Text = o => PostureEngine.Cell((Dictionary<string, string>)o, name), CellTip = o => PostureEngine.Cell((Dictionary<string, string>)o, name), Mono = !first });
                    first = false;
                }
                data.SetItems(b.Rows.Cast<object>().ToList(), false);
            }
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (panes[panes.Length - 1] == null || header == null) return;
            int pad = Pad, gap = Gap, w = Width - pad * 2, y = Dpi.S(4);
            int hh = Dpi.S(164);
            header.Bounds = new Rectangle(pad, y, w, hh);
            int gs = hh - Dpi.S(16);
            gauge.Bounds = new Rectangle(w - gs - Dpi.S(14), Dpi.S(8), gs, gs);
            back.Location = new Point(Dpi.S(10), Dpi.S(10));
            y += hh + Dpi.S(8);
            tabs.Bounds = new Rectangle(pad, y, w, Dpi.S(42));
            y += Dpi.S(42) + gap;
            var area = new Rectangle(pad, y, w, Height - y - Dpi.S(24));
            foreach (var p in panes) p.Bounds = area;
            int left = (int)((w - gap) * 0.58);

            int top = (int)((area.Height - gap) * 0.5);
            historyCard.Bounds = new Rectangle(0, 0, left, top);
            factsCard.Bounds = new Rectangle(0, top + gap, left, area.Height - top - gap);
            latestCard.Bounds = new Rectangle(left + gap, 0, w - left - gap, area.Height);

            checkChips.Bounds = new Rectangle(0, 0, w, Dpi.S(32));
            int cy = Dpi.S(32) + gap;
            checksCard.Bounds = new Rectangle(0, cy, left, area.Height - cy);
            checkDetailCard.Bounds = new Rectangle(left + gap, cy, w - left - gap, area.Height - cy);

            timelineCard.Bounds = new Rectangle(0, 0, w, area.Height);

            int sw = Math.Min(Dpi.S(320), w / 3);
            sectionsCard.Bounds = new Rectangle(0, 0, sw, area.Height);
            dataCard.Bounds = new Rectangle(sw + gap, 0, w - sw - gap, area.Height);
        }

        /// <summary>The identity card at the top of the device page.</summary>
        private sealed class DeviceHeader : Surface
        {
            private readonly DevicePage page;

            public DeviceHeader(DevicePage page) { this.page = page; }

            public override Color InnerColor { get { return T.Card; } }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                Draw.Smooth(g);
                var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
                float rad = Dpi.S(14f);
                Draw.Fill(g, r, rad, T.Card);
                Draw.Stroke(g, r, rad, T.Line, 1f);
                var s = page.Shown;
                var d = page.Device;
                if (s == null || d == null) return;
                var ws = page.Workspace;
                int x = Dpi.S(24), y = Dpi.S(50);
                int box = Dpi.S(56);
                var ib = new Rectangle(x, y, box, box);
                Draw.Gradient(g, ib, Dpi.S(14), T.Tint(T.Accent, T.Dark ? 0.22 : 0.14), T.Tint(T.Accent2, T.Dark ? 0.22 : 0.14), 45f);
                Icons.Draw(g, Cells.DeviceIcon(s), new RectangleF(ib.X + Dpi.S(14), ib.Y + Dpi.S(14), box - Dpi.S(28), box - Dpi.S(28)), T.Accent, 1.8f);
                int tx = ib.Right + Dpi.S(18);
                int right = Width - Height - Dpi.S(20);
                Draw.Text(g, s.Device, Fonts.H1, T.Ink, new Rectangle(tx, y - Dpi.S(4), right - tx, Fonts.H1.Height + 2));
                Draw.Text(g, Fmt.Model(s) + "  ·  " + Fmt.Os(s), Fonts.Body, T.Soft, new Rectangle(tx, y + Fonts.H1.Height - Dpi.S(2), right - tx, Dpi.S(20)));
                int py = y + Fonts.H1.Height + Dpi.S(32);
                int px = tx;
                px += Draw.Tag(g, s.Laptop ? "Laptop" : "Desktop", T.Info, T, px, py) + Dpi.S(6);
                px += Draw.Tag(g, s.Administrator ? "Full audit (admin)" : "Limited audit", s.Administrator ? T.Ok : T.Warn, T, px, py) + Dpi.S(6);
                if (ws.IsStale(d)) px += Draw.Tag(g, "Audit overdue", T.Warn, T, px, py) + Dpi.S(6);
                var cmp = ws.Latest(d);
                if (s == d.Latest && cmp != null && cmp.Highest.HasValue) px += Draw.Tag(g, cmp.Verdict, T.For(cmp.Highest.Value), T, px, py) + Dpi.S(6);
                if (s != d.Latest) Draw.Tag(g, "Older audit", T.Accent2, T, px, py);

                var p = ws.Posture(s);
                int sx = Math.Max(px + Dpi.S(30), right - Dpi.S(380));
                if (sx + Dpi.S(360) < right + Dpi.S(10))
                {
                    var stats = new[]
                    {
                        new KeyValuePair<string, string>("Failing", p.Failed.ToString(CultureInfo.InvariantCulture)),
                        new KeyValuePair<string, string>("Warnings", p.Warnings.ToString(CultureInfo.InvariantCulture)),
                        new KeyValuePair<string, string>("Passing", p.Passed.ToString(CultureInfo.InvariantCulture)),
                        new KeyValuePair<string, string>("Audits", d.Reports.Count.ToString(CultureInfo.InvariantCulture))
                    };
                    var colors = new[] { p.Failed > 0 ? T.Bad : T.Ink, p.Warnings > 0 ? T.Warn : T.Ink, T.Ok, T.Ink };
                    int cw = Dpi.S(88);
                    for (int i = 0; i < stats.Length; i++)
                    {
                        int cx = right - (stats.Length - i) * cw;
                        if (i > 0) using (var pen = new Pen(T.Line, 1f)) g.DrawLine(pen, cx, y, cx, y + Dpi.S(52));
                        Draw.Text(g, stats[i].Value, Fonts.Number, colors[i], new Rectangle(cx, y - Dpi.S(4), cw, Fonts.Number.Height), Draw.Center);
                        Draw.Text(g, stats[i].Key.ToUpperInvariant(), Fonts.Tiny, T.Muted, new Rectangle(cx, y + Fonts.Number.Height - Dpi.S(2), cw, Dpi.S(16)), Draw.Center);
                    }
                }
            }
        }
    }
}
