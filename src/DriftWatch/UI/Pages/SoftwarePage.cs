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
    /// <summary>Every program across the fleet (latest audits), with risk flags and one-click policy rules.</summary>
    public sealed class SoftwarePage : Page
    {
        private readonly SearchBox search = new SearchBox("Search programs or publishers…");
        private readonly ChipBar chips = new ChipBar();
        private readonly Btn export = new Btn("Export CSV", "download", ButtonStyle.Secondary);
        private readonly Card listCard = new Card();
        private readonly GridView grid = new GridView();
        private readonly Card detailCard = new Card();
        private readonly SoftwareHeader header = new SoftwareHeader();
        private readonly GridView installs = new GridView();
        private readonly Btn ban = new Btn("Ban", "x-circle", ButtonStyle.Danger) { Compact = true, Height = Dpi.S(32) };
        private readonly Btn approve = new Btn("Approve", "check", ButtonStyle.Secondary) { Compact = true, Height = Dpi.S(32) };
        private readonly Btn require = new Btn("Require", "shield", ButtonStyle.Secondary) { Compact = true, Height = Dpi.S(32) };
        private readonly Btn clear = new Btn("Remove rule", "refresh", ButtonStyle.Ghost) { Compact = true, Height = Dpi.S(32) };
        private readonly EmptyState empty;

        public SoftwarePage(IShell shell) : base(shell)
        {
            empty = new EmptyState("package", "No software inventory yet", "Open a folder of DeviceDossier reports to see every program installed across your PCs, which ones are risky, and where each version is.");
            var open = new Btn("Open folder", "folder", ButtonStyle.Primary);
            open.Click += delegate { Shell.OpenFolderDialog(); };
            empty.AddAction(open);
            Controls.Add(empty);
            Controls.Add(search);
            Controls.Add(chips);
            Controls.Add(export);

            chips.SetItems(new[]
            {
                new ChipItem("all", "All"), new ChipItem("flagged", "Flagged") { Dot = T.High }, new ChipItem("rare", "On one PC"),
                new ChipItem("versions", "Mixed versions"), new ChipItem("security", "Security"), new ChipItem("policy", "In policy") { Dot = T.Accent2 }
            });
            chips.SelectionChanged += delegate { Fill(); };
            search.TextChanged2 += delegate { Fill(); };
            export.Click += delegate { Cells.SaveText(Shell, "Save software inventory", "DriftWatch_Software_" + Cells.Stamp() + ".csv", "CSV file (*.csv)|*.csv", () => CsvReport.Software(Ws)); };

            grid.Dock = DockStyle.Fill;
            grid.RowHeight = Dpi.S(54);
            grid.EmptyTitle = "No matching programs";
            grid.EmptyText = "Try another filter or clear the search.";
            grid.Columns.Add(new GridColumn("Program", 0) { Fill = 1.5f, MinWidth = 180, Text = o => S(o).Name, Paint = a => { var s = S(a.Item); Cells.TwoLine(a, s.Name, s.Publisher.Length > 0 ? s.Publisher : "Unknown publisher", null, null); } });
            grid.Columns.Add(new GridColumn("Flag", 140) { SortKey = o => FlagRank(S(o)), Paint = a => PaintFlag(a, S(a.Item)) });
            grid.Columns.Add(new GridColumn("PCs", 130) { SortKey = o => S(o).DeviceCount, Paint = a => PaintCount(a, S(a.Item)) });
            grid.Columns.Add(new GridColumn("Versions", 0) { Fill = 0.7f, MinWidth = 110, SortKey = o => S(o).Versions.Count, Paint = a => { var s = S(a.Item); var v = s.Versions; Cells.TwoLine(a, v.Count == 0 ? "–" : v[0], v.Count > 1 ? v.Count + " versions in use" : "", null, null); } });
            grid.Stripe = o => { var s = S(o); return s.Risky ? (Color?)(s.Flag == "Banned" ? T.Bad : T.High) : null; };
            grid.SelectionChanged += delegate { ShowDetail(); };
            listCard.ContentPadding = Dpi.P(10, 6, 10, 8);
            listCard.Controls.Add(grid);
            Controls.Add(listCard);

            detailCard.ContentPadding = Dpi.P(12, 168, 12, 10);
            detailCard.Controls.Add(header);
            installs.Dock = DockStyle.Fill;
            installs.RowHeight = Dpi.S(44);
            installs.Columns.Add(new GridColumn("Device", 0) { Fill = 1, Text = o => ((SoftwareInstall)o).Device.Device, Paint = a => { var i = (SoftwareInstall)a.Item; Cells.TwoLine(a, i.Device.Device, Fmt.Model(i.Device.Latest), null, null); } });
            installs.Columns.Add(new GridColumn("Version", 120) { Text = o => ((SoftwareInstall)o).Version, Mono = true });
            installs.Columns.Add(new GridColumn("Installed", 100) { Align = Draw.Right, Text = o => ((SoftwareInstall)o).InstallDate });
            installs.ItemActivated += delegate { var i = installs.SelectedItem as SoftwareInstall; if (i != null) Shell.ShowDevice(i.Device, 0); };
            detailCard.Controls.Add(installs);
            foreach (var b in new[] { ban, approve, require, clear }) { b.FitWidth(); detailCard.Controls.Add(b); }
            ban.Click += delegate { Rule(Ws.Policy.BannedSoftware, "banned"); };
            approve.Click += delegate { Rule(Ws.Policy.ApprovedSoftware, "approved"); };
            require.Click += delegate { Rule(Ws.Policy.RequiredSoftware, "required"); };
            clear.Click += delegate { ClearRule(); };
            detailCard.Layout += delegate
            {
                header.Bounds = new Rectangle(Dpi.S(18), Dpi.S(18), detailCard.Width - Dpi.S(36), Dpi.S(98));
                int x = Dpi.S(18), by = Dpi.S(122);
                foreach (var b in new[] { ban, approve, require, clear }) { if (!b.Visible) continue; b.Location = new Point(x, by); x += b.Width + Dpi.S(8); }
            };
            Controls.Add(detailCard);
        }

        public override string Key { get { return "software"; } }
        public override string PageTitle { get { return "Software"; } }
        public override string PageSubtitle
        {
            get
            {
                if (Ws.IsEmpty) return "Every program across your PCs";
                var inv = Ws.Inventory;
                return Fmt.Plural(inv.Count, "program") + " across " + Fmt.Plural(Ws.Devices.Count, "PC") + " · from each PC's latest audit · " + inv.Count(x => x.Risky) + " flagged";
            }
        }

        private static SoftwareEntry S(object o) { return (SoftwareEntry)o; }

        private static int FlagRank(SoftwareEntry s)
        {
            switch (s.Flag)
            {
                case "Banned": return 0;
                case "Remote access": case "Tunnelling": case "Unwanted": return 1;
                case "Admin tool": return 2;
                case "Required": return 3;
                case "Approved": return 4;
                case "Security": return 5;
                default: return 9;
            }
        }

        private Color FlagColor(string flag)
        {
            switch (flag)
            {
                case "Banned": return T.Bad;
                case "Remote access": case "Tunnelling": case "Unwanted": return T.High;
                case "Admin tool": return T.Medium;
                case "Required": return T.Accent2;
                case "Approved": return T.Ok;
                case "Security": return T.Low;
                default: return T.Faint;
            }
        }

        public override void Activate(object arg)
        {
            RefreshData();
            var name = arg as string;
            if (name != null)
            {
                chips.SelectedKey = "all";
                search.Value = name;
                Fill();
                var match = Ws.Inventory.FirstOrDefault(s => s.Name == name);
                if (match != null) grid.SelectedItem = match;
            }
        }

        public override void FocusSearch() { search.FocusInput(); }

        public override void RefreshData()
        {
            bool none = Ws.IsEmpty;
            empty.Visible = none;
            search.Visible = chips.Visible = export.Visible = listCard.Visible = detailCard.Visible = !none;
            RaiseTitleChanged();
            if (none) return;
            var inv = Ws.Inventory;
            chips.SetCount("all", inv.Count);
            chips.SetCount("flagged", inv.Count(s => s.Risky || s.Flag == "Admin tool"));
            chips.SetCount("rare", inv.Count(s => s.DeviceCount == 1 && Ws.Devices.Count > 1));
            chips.SetCount("versions", inv.Count(s => s.Versions.Count > 1));
            chips.SetCount("security", inv.Count(s => s.Flag == "Security"));
            chips.SetCount("policy", inv.Count(s => s.Flag == "Banned" || s.Flag == "Approved" || s.Flag == "Required"));
            Fill();
            PerformLayout();
        }

        private void Fill()
        {
            IEnumerable<SoftwareEntry> list = Ws.Inventory;
            switch (chips.SelectedKey)
            {
                case "flagged": list = list.Where(s => s.Risky || s.Flag == "Admin tool"); break;
                case "rare": list = list.Where(s => s.DeviceCount == 1 && Ws.Devices.Count > 1); break;
                case "versions": list = list.Where(s => s.Versions.Count > 1); break;
                case "security": list = list.Where(s => s.Flag == "Security"); break;
                case "policy": list = list.Where(s => s.Flag == "Banned" || s.Flag == "Approved" || s.Flag == "Required"); break;
            }
            string q = search.Value.Trim();
            if (q.Length > 0) list = list.Where(s => TextUtil.Contains(s.Name, q) || TextUtil.Contains(s.Publisher, q));
            var result = list.ToList();
            grid.SetItems(result, true);
            if (grid.SelectedItem == null && result.Count > 0) grid.SelectedIndex = 0;
            ShowDetail();
        }

        private void PaintFlag(CellArgs a, SoftwareEntry s)
        {
            if (string.IsNullOrEmpty(s.Flag)) { Draw.Text(a.G, "–", Fonts.Small, a.T.Faint, a.Bounds); return; }
            Draw.Tag(a.G, s.Flag, FlagColor(s.Flag), a.T, a.Bounds.X, a.Bounds.Y + a.Bounds.Height / 2);
        }

        private void PaintCount(CellArgs a, SoftwareEntry s)
        {
            int total = Math.Max(1, Ws.Devices.Count);
            string t = s.DeviceCount + " of " + total;
            Draw.Text(a.G, t, Fonts.SmallSemi, a.T.Ink, new Rectangle(a.Bounds.X, a.Bounds.Y + Dpi.S(10), a.Bounds.Width, Dpi.S(18)));
            var bar = new Rectangle(a.Bounds.X, a.Bounds.Y + a.Bounds.Height - Dpi.S(18), Math.Min(a.Bounds.Width, Dpi.S(100)), Dpi.S(5));
            Draw.Smooth(a.G);
            Draw.Fill(a.G, bar, Dpi.S(2.5f), a.T.LineSoft);
            int w = (int)(bar.Width * s.DeviceCount / (double)total);
            if (w > 0) Draw.Fill(a.G, new Rectangle(bar.X, bar.Y, Math.Max(w, bar.Height), bar.Height), Dpi.S(2.5f), s.Risky ? a.T.High : a.T.Accent);
        }

        private void ShowDetail()
        {
            var s = grid.SelectedItem as SoftwareEntry;
            header.Entry = s;
            header.FlagColor = s == null ? T.Faint : FlagColor(s.Flag);
            header.Total = Ws.Devices.Count;
            header.Invalidate();
            installs.SetItems(s == null ? new List<SoftwareInstall>() : s.Installs.OrderBy(i => i.Device.Device).ToList(), false);
            bool has = s != null;
            bool inPolicy = has && (s.Flag == "Banned" || s.Flag == "Approved" || s.Flag == "Required");
            ban.Visible = approve.Visible = require.Visible = has && !inPolicy;
            clear.Visible = inPolicy;
            detailCard.PerformLayout();
        }

        private void Rule(List<string> list, string what)
        {
            var s = grid.SelectedItem as SoftwareEntry;
            if (s == null) return;
            if (!list.Any(x => string.Equals(x, s.Name, StringComparison.OrdinalIgnoreCase))) list.Add(s.Name);
            Shell.PolicyChanged(true);
            Shell.Toast(s.Name + " is now " + what + " in your policy. Scores and changes have been updated.", BannerKind.Ok);
            var again = Ws.Inventory.FirstOrDefault(x => x.Name == s.Name);
            if (again != null) grid.SelectedItem = again;
        }

        private void ClearRule()
        {
            var s = grid.SelectedItem as SoftwareEntry;
            if (s == null) return;
            var p = Ws.Policy;
            Predicate<string> match = x => string.Equals(x, s.Name, StringComparison.OrdinalIgnoreCase);
            p.BannedSoftware.RemoveAll(match);
            p.ApprovedSoftware.RemoveAll(match);
            p.RequiredSoftware.RemoveAll(match);
            Shell.PolicyChanged(true);
            Shell.Toast("Removed the policy rule for " + s.Name + ". Rules using wildcards are kept; edit them on the Policy page.", BannerKind.Info);
            var again = Ws.Inventory.FirstOrDefault(x => x.Name == s.Name);
            if (again != null) grid.SelectedItem = again;
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (empty == null) return;
            int pad = Pad, y = Dpi.S(4), w = Width - pad * 2, gap = Gap;
            empty.Bounds = new Rectangle(0, 0, Width, Height);
            search.Bounds = new Rectangle(pad, y, Math.Min(Dpi.S(260), w / 4), Dpi.S(36));
            export.Location = new Point(Width - pad - export.Width, y);
            chips.Bounds = new Rectangle(search.Right + Dpi.S(14), y + Dpi.S(2), export.Left - search.Right - Dpi.S(28), Dpi.S(32));
            y += Dpi.S(36) + gap;
            int left = (int)((w - gap) * 0.6);
            int h = Height - y - Dpi.S(24);
            listCard.Bounds = new Rectangle(pad, y, left, h);
            detailCard.Bounds = new Rectangle(pad + left + gap, y, w - left - gap, h);
        }

        private sealed class SoftwareHeader : Surface
        {
            public SoftwareEntry Entry;
            public Color FlagColor;
            public int Total;

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                if (Entry == null) { Draw.Text(g, "Select a program.", Fonts.Body, T.Muted, ClientRectangle, Draw.Center); return; }
                int box = Dpi.S(46);
                var ib = new Rectangle(0, 0, box, box);
                Draw.Smooth(g);
                Draw.Fill(g, ib, Dpi.S(11), T.Tint(Entry.Risky ? FlagColor : T.Accent, T.Dark ? 0.16 : 0.1));
                Icons.Draw(g, "package", new RectangleF(Dpi.S(12), Dpi.S(12), box - Dpi.S(24), box - Dpi.S(24)), Entry.Risky ? FlagColor : T.Accent, 1.9f);
                int x = box + Dpi.S(14);
                Draw.Text(g, Entry.Name, Fonts.H2, T.Ink, new Rectangle(x, -Dpi.S(2), Width - x, Fonts.H2.Height + 2));
                Draw.Text(g, (Entry.Publisher.Length > 0 ? Entry.Publisher : "Unknown publisher") + " · on " + Entry.DeviceCount + " of " + Total + " PCs", Fonts.Small, T.Muted, new Rectangle(x, Fonts.H2.Height, Width - x, Dpi.S(18)));
                if (!string.IsNullOrEmpty(Entry.Flag))
                {
                    Draw.Tag(g, Entry.Flag, FlagColor, T, x, Dpi.S(64));
                    Draw.Text(g, Explain(Entry.Flag), Fonts.Small, T.Soft, new Rectangle(x, Dpi.S(80), Width - x, Dpi.S(18)));
                }
            }

            private static string Explain(string flag)
            {
                switch (flag)
                {
                    case "Banned": return "Your policy bans this program.";
                    case "Required": return "Your policy requires this on every PC.";
                    case "Approved": return "Approved in your policy.";
                    case "Remote access": return "Lets someone control the PC from elsewhere.";
                    case "Tunnelling": return "Can bypass network controls or hide traffic.";
                    case "Unwanted": return "Common source of malware or legal risk.";
                    case "Admin tool": return "Useful to IT, also used by attackers.";
                    case "Security": return "Security or monitoring software.";
                    default: return "";
                }
            }
        }
    }
}
