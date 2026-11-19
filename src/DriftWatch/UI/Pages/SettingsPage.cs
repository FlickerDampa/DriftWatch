using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using DriftWatch.Core;

namespace DriftWatch.UI.Pages
{
    /// <summary>Appearance, folder watching, privacy, shortcuts and about.</summary>
    public sealed class SettingsPage : Page
    {
        private readonly ScrollHost host = new ScrollHost();
        private readonly Card appearance = new Card { Title = "Appearance", Icon = "sun" };
        private readonly ChipBar theme = new ChipBar();
        private readonly Lbl themeHint = new Lbl("System follows the Windows app colour setting.", Fonts.Small, Tone.Muted);
        private readonly Card monitoring = new Card { Title = "Monitoring", Icon = "activity" };
        private readonly Toggle watch = new Toggle("Watch the folder for new audits", "Re-check automatically when DeviceDossier saves a new report");
        private readonly Toggle recurse = new Toggle("Include subfolders", "Read reports in every folder below the one you open");
        private readonly Toggle notify = new Toggle("Desktop notifications", "Show a Windows notification for new audits while DriftWatch is in the background");
        private readonly Card privacy = new Card { Title = "Privacy and data", Icon = "lock" };
        private readonly Toggle remember = new Toggle("Reopen the last folder at start-up", "Remembers the folder path and a short list of recent folders");
        private readonly Lbl privacyText = new Lbl("", Fonts.Body, Tone.Soft) { WrapText = true };
        private readonly Btn openData = new Btn("Open settings folder", "folder", ButtonStyle.Secondary) { Compact = true, Height = Dpi.S(32) };
        private readonly Btn resetSettings = new Btn("Reset settings", "refresh", ButtonStyle.Ghost) { Compact = true, Height = Dpi.S(32) };
        private readonly Card shortcuts = new Card { Title = "Keyboard shortcuts", Icon = "keyboard" };
        private readonly ShortcutList keys = new ShortcutList();
        private readonly Card about = new Card { Title = "About", Icon = "info" };
        private readonly AboutView aboutView = new AboutView();
        private readonly Btn github = new Btn("Source code", "external", ButtonStyle.Secondary) { Compact = true, Height = Dpi.S(32) };
        private readonly Btn site = new Btn("FlickerDampa", "globe", ButtonStyle.Ghost) { Compact = true, Height = Dpi.S(32) };
        private readonly Btn issues = new Btn("Report a problem", "alert", ButtonStyle.Ghost) { Compact = true, Height = Dpi.S(32) };
        private bool loading;

        public SettingsPage(IShell shell) : base(shell)
        {
            host.Dock = DockStyle.Fill;
            Controls.Add(host);
            foreach (var c in new Control[] { appearance, monitoring, privacy, shortcuts, about }) host.Controls.Add(c);

            theme.SetItems(new[] { new ChipItem("system", "System"), new ChipItem("dark", "Dark"), new ChipItem("light", "Light") });
            theme.SelectionChanged += delegate
            {
                if (loading) return;
                Shell.Settings.Theme = theme.SelectedKey;
                Shell.Settings.Save();
                Theme.Apply(theme.SelectedKey);
            };
            appearance.Controls.Add(theme);
            appearance.Controls.Add(themeHint);

            foreach (var t in new[] { watch, recurse, notify }) monitoring.Controls.Add(t);
            watch.CheckedChanged += delegate { if (loading) return; Shell.Settings.Watch = watch.Checked; Shell.SettingsChanged(); };
            recurse.CheckedChanged += delegate
            {
                if (loading) return;
                Shell.Settings.Recurse = recurse.Checked;
                Shell.SettingsChanged();
                if (!string.IsNullOrEmpty(Ws.Folder)) Shell.OpenFolder(Ws.Folder);
            };
            notify.CheckedChanged += delegate { if (loading) return; Shell.Settings.Notify = notify.Checked; Shell.SettingsChanged(); };

            privacy.Controls.Add(remember);
            privacy.Controls.Add(privacyText);
            privacy.Controls.Add(openData);
            privacy.Controls.Add(resetSettings);
            openData.FitWidth();
            resetSettings.FitWidth();
            remember.CheckedChanged += delegate { if (loading) return; Shell.Settings.RememberFolder = remember.Checked; Shell.SettingsChanged(); };
            openData.Click += delegate
            {
                try { Directory.CreateDirectory(AppSettings.Folder); Process.Start(AppSettings.Folder); }
                catch (Exception) { Shell.Toast("Couldn't open " + AppSettings.Folder, BannerKind.Warn); }
            };
            resetSettings.Click += delegate
            {
                AppSettings.Reset();
                var fresh = AppSettings.Load();
                var s = Shell.Settings;
                s.Theme = fresh.Theme; s.Watch = fresh.Watch; s.Recurse = fresh.Recurse; s.Notify = fresh.Notify; s.RememberFolder = fresh.RememberFolder;
                s.RecentFolders.Clear();
                s.LastFolder = null;
                Shell.SettingsChanged();
                Theme.Apply(s.Theme);
                RefreshData();
                Shell.Toast("Settings reset. Your policy wasn't changed.", BannerKind.Ok);
            };

            keys.Dock = DockStyle.Fill;
            shortcuts.Controls.Add(keys);

            about.Controls.Add(aboutView);
            foreach (var b in new[] { github, site, issues }) { b.FitWidth(); about.Controls.Add(b); }
            github.Click += delegate { Open(AppInfo.Repository); };
            site.Click += delegate { Open("https://flickerdampa.github.io/"); };
            issues.Click += delegate { Open(AppInfo.Repository + "/issues"); };

            host.Resize += delegate { LayoutForm(); };
            RefreshData();
        }

        public override string Key { get { return "settings"; } }
        public override string PageTitle { get { return "Settings"; } }
        public override string PageSubtitle { get { return "Appearance, monitoring and privacy"; } }

        private void Open(string url)
        {
            try { Process.Start(url); }
            catch (Exception) { Cells.Copy(Shell, url, "Link"); }
        }

        public override void Activate(object arg)
        {
            RefreshData();
            var key = arg as string;
            if (key == "shortcuts") host.ScrollControlIntoView(shortcuts);
            else if (key == "about") host.ScrollControlIntoView(about);
        }

        public override void RefreshData()
        {
            loading = true;
            var s = Shell.Settings;
            theme.SelectedKey = string.IsNullOrEmpty(s.Theme) ? "system" : s.Theme;
            watch.Checked = s.Watch;
            recurse.Checked = s.Recurse;
            notify.Checked = s.Notify;
            remember.Checked = s.RememberFolder;
            loading = false;
            privacyText.Text = "DriftWatch works offline. It only reads the reports you open, never contacts the PCs they describe, and sends nothing anywhere. Settings and your policy are stored in " + AppSettings.Folder + ".";
            LayoutForm();
        }

        private void LayoutForm()
        {
            int pad = Pad, gap = Gap, oy = host.AutoScrollPosition.Y;
            int w = host.ClientSize.Width - pad * 2;
            int left = (int)((w - gap) * 0.5), right = w - left - gap;
            int li = left - Dpi.S(36), ri = right - Dpi.S(36);

            int y = appearance.HeaderHeight;
            theme.Bounds = new Rectangle(Dpi.S(18), y, li, Dpi.S(32));
            themeHint.Bounds = new Rectangle(Dpi.S(18), y + Dpi.S(42), li, Dpi.S(18));
            int appH = y + Dpi.S(42) + Dpi.S(18) + Dpi.S(18);

            y = monitoring.HeaderHeight;
            foreach (var t in new[] { watch, recurse, notify }) { t.Bounds = new Rectangle(Dpi.S(18), y, li, t.Height); y += t.Height + Dpi.S(14); }
            int monH = y + Dpi.S(4);

            y = privacy.HeaderHeight;
            remember.Bounds = new Rectangle(Dpi.S(18), y, li, remember.Height);
            y += remember.Height + Dpi.S(14);
            int ph = privacyText.HeightFor(li);
            privacyText.Bounds = new Rectangle(Dpi.S(18), y, li, ph);
            y += ph + Dpi.S(14);
            openData.Location = new Point(Dpi.S(18), y);
            resetSettings.Location = new Point(openData.Right + Dpi.S(8), y);
            int privH = y + openData.Height + Dpi.S(18);

            int keysH = shortcuts.HeaderHeight + keys.PreferredHeight + Dpi.S(16);
            int aboutContent = Dpi.S(150);
            aboutView.Bounds = new Rectangle(Dpi.S(18), about.HeaderHeight, ri, aboutContent);
            int by = about.HeaderHeight + aboutContent + Dpi.S(8);
            github.Location = new Point(Dpi.S(18), by);
            site.Location = new Point(github.Right + Dpi.S(8), by);
            issues.Location = new Point(site.Right + Dpi.S(8), by);
            int aboutH = by + github.Height + Dpi.S(18);

            int ly = Dpi.S(4);
            appearance.Bounds = new Rectangle(pad, ly + oy, left, appH);
            ly += appH + gap;
            monitoring.Bounds = new Rectangle(pad, ly + oy, left, monH);
            ly += monH + gap;
            privacy.Bounds = new Rectangle(pad, ly + oy, left, privH);
            ly += privH;
            int ry = Dpi.S(4);
            about.Bounds = new Rectangle(pad + left + gap, ry + oy, right, aboutH);
            ry += aboutH + gap;
            shortcuts.Bounds = new Rectangle(pad + left + gap, ry + oy, right, keysH);
            ry += keysH;
            host.AutoScrollMinSize = new Size(0, Math.Max(ly, ry) + Dpi.S(24));
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            LayoutForm();
        }

        private sealed class ShortcutList : Surface
        {
            private static readonly string[][] Keys =
            {
                new[] { "Ctrl+K", "Search and jump anywhere" },
                new[] { "Ctrl+O", "Open a folder of reports" },
                new[] { "Ctrl+Shift+O", "Add report files" },
                new[] { "F5", "Rescan the folder" },
                new[] { "Ctrl+F", "Search on the current page" },
                new[] { "Ctrl+1 … 6", "Overview, Devices, Compare, Posture, Software, Policy" },
                new[] { "Ctrl+,", "Settings" },
                new[] { "Alt+Left", "Back to all devices" },
                new[] { "Enter / double-click", "Open the selected row" }
            };

            public int PreferredHeight { get { return Keys.Length * Dpi.S(34); } }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                int y = 0;
                foreach (var k in Keys)
                {
                    int x = 0;
                    foreach (var part in k[0].Split(new[] { " / " }, StringSplitOptions.None))
                    {
                        int w = Draw.Measure(part, Fonts.MonoSmall).Width + Dpi.S(14);
                        var r = new Rectangle(x, y + Dpi.S(5), w, Dpi.S(24));
                        Draw.Smooth(g);
                        Draw.Fill(g, r, Dpi.S(6), T.Dark ? T.LineSoft : T.Bg);
                        Draw.Stroke(g, r, Dpi.S(6), T.Line, 1f);
                        Draw.Text(g, part, Fonts.MonoSmall, T.Ink, r, Draw.Center);
                        x += w + Dpi.S(6);
                    }
                    Draw.Text(g, k[1], Fonts.Body, T.Soft, new Rectangle(Math.Max(x, Dpi.S(150)) + Dpi.S(8), y, Width, Dpi.S(34)));
                    y += Dpi.S(34);
                }
            }
        }

        private sealed class AboutView : Surface
        {
            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                var logo = new RectangleF(0, Dpi.S(4), Dpi.S(64), Dpi.S(64));
                Draw.Logo(g, logo, T);
                int x = Dpi.S(82);
                Draw.Text(g, AppInfo.Name, Fonts.H1, T.Ink, new Rectangle(x, 0, Width - x, Fonts.H1.Height + 2));
                Draw.Text(g, "Version " + AppInfo.Version + " · public beta", Fonts.Body, T.Muted, new Rectangle(x, Fonts.H1.Height + Dpi.S(2), Width - x, Dpi.S(20)));
                Draw.Text(g, "Made by FlickerDampa · MIT licence", Fonts.Small, T.Faint, new Rectangle(x, Fonts.H1.Height + Dpi.S(24), Width - x, Dpi.S(18)));
                string text = "Security drift monitoring for Windows PCs, built on DeviceDossier audits: posture scores, risk-ranked changes between audits, a fleet software inventory and policy checks, all offline.";
                Draw.Text(g, text, Fonts.Body, T.Soft, new Rectangle(0, Dpi.S(86), Width, Height - Dpi.S(86)), Draw.Wrap);
            }
        }
    }
}
