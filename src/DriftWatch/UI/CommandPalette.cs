using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using DriftWatch.Core;

namespace DriftWatch.UI
{
    /// <summary>Ctrl+K: jump to any page, device or program, or run an action, by typing.</summary>
    public sealed class CommandPalette : Form
    {
        private sealed class Entry
        {
            public string Kind, Title, Detail, Icon;
            public Action Run;
        }

        private readonly IShell shell;
        private readonly TextBox input = new TextBox { BorderStyle = BorderStyle.None };
        private readonly GridView list = new GridView();
        private readonly Frame frame = new Frame();
        private readonly List<Entry> all = new List<Entry>();

        private sealed class Frame : Surface
        {
            public override Color InnerColor { get { return T.Card; } }

            protected override void OnPaintBackground(PaintEventArgs e) { e.Graphics.Clear(T.Card); }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                using (var pen = new Pen(T.Line, 1f)) g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
                int s = Dpi.S(18);
                Icons.Draw(g, "search", new RectangleF(Dpi.S(18), Dpi.S(18), s, s), T.Accent, 2f);
                using (var pen = new Pen(T.Line, 1f)) g.DrawLine(pen, 0, Dpi.S(54), Width, Dpi.S(54));
                string hint = "↑↓ to move · Enter to open · Esc to close";
                Draw.Text(g, hint, Fonts.Small, T.Faint, new Rectangle(Dpi.S(16), Height - Dpi.S(30), Width - Dpi.S(32), Dpi.S(22)));
            }
        }

        public CommandPalette(IShell shell)
        {
            this.shell = shell;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            KeyPreview = true;
            Size = Dpi.S(640, 460);
            var owner = shell.Window;
            Location = new Point(owner.Left + (owner.Width - Width) / 2, owner.Top + Dpi.S(110));
            BackColor = Theme.Current.Card;

            frame.Dock = DockStyle.Fill;
            Controls.Add(frame);
            input.Font = Fonts.H3;
            input.BackColor = Theme.Current.Card;
            input.ForeColor = Theme.Current.Ink;
            input.SetBounds(Dpi.S(48), Dpi.S(16), Width - Dpi.S(64), Dpi.S(26));
            frame.Controls.Add(input);
            list.SetBounds(Dpi.S(8), Dpi.S(62), Width - Dpi.S(16), Height - Dpi.S(62) - Dpi.S(36));
            list.ShowHeader = false;
            list.RowHeight = Dpi.S(46);
            list.EmptyText = "No matches.";
            list.Columns.Add(new GridColumn("", 0)
            {
                Fill = 1,
                Paint = a =>
                {
                    var e = (Entry)a.Item;
                    int s = Dpi.S(28);
                    var ib = new Rectangle(a.Bounds.X, a.Bounds.Y + (a.Bounds.Height - s) / 2, s, s);
                    Draw.Fill(a.G, ib, Dpi.S(7), a.T.Tint(a.T.Accent, a.T.Dark ? 0.14 : 0.1));
                    Icons.Draw(a.G, e.Icon, new RectangleF(ib.X + Dpi.S(6), ib.Y + Dpi.S(6), s - Dpi.S(12), s - Dpi.S(12)), a.T.Accent, 2f);
                    int x = ib.Right + Dpi.S(12);
                    int kw = Draw.Measure(e.Kind, Fonts.Tiny).Width;
                    Draw.Text(a.G, e.Title, Fonts.Semi, a.T.Ink, new Rectangle(x, a.Bounds.Y + Dpi.S(5), a.Bounds.Width - x + a.Bounds.X - kw - Dpi.S(20), Dpi.S(20)));
                    Draw.Text(a.G, e.Detail, Fonts.Small, a.T.Muted, new Rectangle(x, a.Bounds.Y + Dpi.S(24), a.Bounds.Width - x + a.Bounds.X - kw - Dpi.S(20), Dpi.S(17)));
                    Draw.Text(a.G, e.Kind.ToUpperInvariant(), Fonts.Tiny, a.T.Faint, a.Bounds, Draw.Right);
                }
            });
            frame.Controls.Add(list);

            Build();
            Filter();
            input.TextChanged += delegate { Filter(); };
            list.ItemActivated += delegate { Execute(); };
            Deactivate += delegate { if (Visible) Close(); };
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            input.Focus();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.Escape: Close(); return true;
                case Keys.Down: list.SelectedIndex = Math.Min(list.Count - 1, list.SelectedIndex + 1); return true;
                case Keys.Up: list.SelectedIndex = Math.Max(0, list.SelectedIndex - 1); return true;
                case Keys.Enter: Execute(); return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void Execute()
        {
            var e = list.SelectedItem as Entry;
            if (e == null) return;
            Close();
            shell.Window.BeginInvoke(e.Run);
        }

        private void Add(string kind, string title, string detail, string icon, Action run)
        {
            all.Add(new Entry { Kind = kind, Title = title, Detail = detail, Icon = icon, Run = run });
        }

        private void Build()
        {
            var ws = shell.Workspace;
            Add("Page", "Overview", "Fleet summary, trends and what needs attention", "dashboard", () => shell.Navigate("dashboard", null));
            Add("Page", "Devices", "Every PC with its score and latest changes", "devices", () => shell.Navigate("devices", null));
            Add("Page", "Compare", "Compare two audits of the same PC", "compare", () => shell.Navigate("compare", null));
            Add("Page", "Posture", "Security checks across the fleet", "shield-check", () => shell.Navigate("posture", null));
            Add("Page", "Software", "Every program installed across the fleet", "package", () => shell.Navigate("software", null));
            Add("Page", "Policy", "Banned and approved software, thresholds", "policy", () => shell.Navigate("policy", null));
            Add("Page", "Settings", "Theme, folder watching and notifications", "settings", () => shell.Navigate("settings", null));
            Add("Action", "Open a folder of reports…", "Ctrl+O", "folder", shell.OpenFolderDialog);
            Add("Action", "Add report files…", "Ctrl+Shift+O", "file-plus", shell.AddReportsDialog);
            Add("Action", "Rescan the folder", "F5", "refresh", shell.Rescan);
            Add("Action", "Switch to " + (Theme.Current.Dark ? "light" : "dark") + " theme", "Appearance", Theme.Current.Dark ? "sun" : "moon", () =>
            {
                shell.Settings.Theme = Theme.Current.Dark ? "light" : "dark";
                shell.Settings.Save();
                Theme.Apply(shell.Settings.Theme);
            });
            foreach (var d in ws.Devices)
            {
                var dev = d;
                var p = ws.Posture(d.Latest);
                var c = ws.Latest(d);
                Add("Device", d.Device, "Score " + p.Score + " · " + Fmt.SeverityCounts(c) + " · audited " + Fmt.Ago(d.Latest.Generated), d.Latest.Laptop ? "laptop" : "desktop", () => shell.ShowDevice(dev, 0));
                if (c != null) Add("Compare", d.Device + ": latest changes", Fmt.Date(d.Previous.Generated) + " → " + Fmt.Date(d.Latest.Generated), "compare", () => shell.Compare(dev.Previous, dev.Latest, null));
            }
            if (!ws.IsEmpty)
                foreach (var s in ws.Inventory.Take(400))
                {
                    var name = s.Name;
                    Add("Software", s.Name, (s.Publisher.Length > 0 ? s.Publisher + " · " : "") + Fmt.Plural(s.DeviceCount, "PC"), "package", () => shell.Navigate("software", name));
                }
        }

        private void Filter()
        {
            var words = input.Text.ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            IEnumerable<Entry> found = all;
            if (words.Length > 0) found = all.Where(e => words.All(w => (e.Title + " " + e.Detail + " " + e.Kind).ToLowerInvariant().Contains(w)))
                                              .OrderBy(e => e.Title.ToLowerInvariant().StartsWith(words[0]) ? 0 : 1);
            else found = all.Where(e => e.Kind != "Software");
            list.SetItems(found.Take(60).ToList(), false);
            if (list.Count > 0) list.SelectedIndex = 0;
        }
    }
}
