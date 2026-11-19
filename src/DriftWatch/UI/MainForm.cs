using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using DriftWatch.Core;
using DriftWatch.UI.Pages;

namespace DriftWatch.UI
{
    /// <summary>The main window: navigation rail, top bar, pages, folder watching and notifications.</summary>
    public sealed class MainForm : Form, IShell
    {
        private readonly NavRail rail = new NavRail();
        private readonly Surface content = new ContentHost();
        private readonly TopBar top = new TopBar();
        private readonly Surface host = new ContentHost();
        private readonly ToastView toast = new ToastView();
        private readonly Overlay overlay = new Overlay();
        private readonly Dictionary<string, Page> pages = new Dictionary<string, Page>();
        private Page current;
        private Workspace workspace;
        private readonly AppSettings settings;
        private FileSystemWatcher watcher;
        private readonly Timer watchDebounce = new Timer { Interval = 2000 };
        private NotifyIcon tray;
        private bool scanning;
        private readonly string[] startupFiles;

        private sealed class ContentHost : Surface
        {
        }

        public MainForm(string[] files)
        {
            startupFiles = files ?? new string[0];
            settings = AppSettings.Load();
            Theme.Apply(settings.Theme);
            workspace = new Workspace(Policy.LoadDefault());

            Text = AppInfo.Name;
            Font = Fonts.Body;
            KeyPreview = true;
            AllowDrop = true;
            AutoScaleMode = AutoScaleMode.None;
            StartPosition = FormStartPosition.CenterScreen;
            var area = Screen.PrimaryScreen.WorkingArea;
            Size = new Size(Math.Min(Dpi.S(1480), area.Width - Dpi.S(40)), Math.Min(Dpi.S(920), area.Height - Dpi.S(40)));
            MinimumSize = new Size(Math.Min(Dpi.S(1180), area.Width), Math.Min(Dpi.S(720), area.Height));
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch (Exception) { }

            rail.Dock = DockStyle.Left;
            top.Dock = DockStyle.Top;
            host.Dock = DockStyle.Fill;
            content.Dock = DockStyle.Fill;
            content.Controls.Add(host);
            content.Controls.Add(top);
            content.Controls.Add(overlay);
            content.Controls.Add(toast);
            Controls.Add(content);
            Controls.Add(rail);

            rail.Navigate += (s, key) => Navigate(key, null);
            rail.WorkspaceClicked += delegate { OpenFolderDialog(); };
            rail.ThemeClicked += delegate { ToggleTheme(); };
            rail.ShortcutsClicked += delegate { Navigate("settings", "shortcuts"); };
            rail.AboutClicked += delegate { Navigate("settings", "about"); };
            top.OpenButton.Click += delegate { OpenFolderDialog(); };
            top.AddButton.Click += delegate { AddReportsDialog(); };
            top.Search.Launched += delegate { ShowPalette(); };
            content.Resize += delegate { LayoutOverlays(); };
            watchDebounce.Tick += delegate { watchDebounce.Stop(); Rescan(); };
            Theme.Changed += OnThemeChanged;
            BackColor = Theme.Current.Bg;

            Navigate("dashboard", null);
            UpdateRail();
        }


        public Workspace Workspace { get { return workspace; } }
        public AppSettings Settings { get { return settings; } }
        public Form Window { get { return this; } }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.TitleBar(this, Theme.Current);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            string folder = Environment.GetEnvironmentVariable("DRIFTWATCH_OPEN_FOLDER");
            if (startupFiles.Length > 0) AddFiles(startupFiles);
            else if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder)) OpenFolder(folder);
            else if (settings.RememberFolder && !string.IsNullOrEmpty(settings.LastFolder) && Directory.Exists(settings.LastFolder)) OpenFolder(settings.LastFolder);
        }

        private void OnThemeChanged(object sender, EventArgs e)
        {
            BackColor = Theme.Current.Bg;
            Native.TitleBar(this, Theme.Current);
            Invalidate(true);
        }

        private void LayoutOverlays()
        {
            overlay.Bounds = new Rectangle(0, 0, content.ClientSize.Width, content.ClientSize.Height);
            if (toast.Visible) toast.Location = new Point(content.ClientSize.Width - toast.Width - Dpi.S(24), content.ClientSize.Height - toast.Height - Dpi.S(24));
        }

        private Page GetPage(string key)
        {
            Page p;
            if (pages.TryGetValue(key, out p)) return p;
            switch (key)
            {
                case "dashboard": p = new DashboardPage(this); break;
                case "devices": p = new DevicesPage(this); break;
                case "device": p = new DevicePage(this); break;
                case "compare": p = new ComparePage(this); break;
                case "posture": p = new PosturePage(this); break;
                case "software": p = new SoftwarePage(this); break;
                case "policy": p = new PolicyPage(this); break;
                case "settings": p = new SettingsPage(this); break;
                default: return null;
            }
            p.Dock = DockStyle.Fill;
            p.Visible = false;
            p.TitleChanged += delegate { if (p == current) UpdateTitle(); };
            host.Controls.Add(p);
            pages[key] = p;
            p.RefreshData();
            return p;
        }

        public void Navigate(string key, object arg)
        {
            var p = GetPage(key);
            if (p == null) return;
            if (current != null && current != p) current.Visible = false;
            current = p;
            p.Visible = true;
            p.BringToFront();
            p.Activate(arg);
            rail.Selected = key == "device" ? "devices" : key;
            UpdateTitle();
        }

        private void UpdateTitle()
        {
            if (current == null) return;
            top.Title = current.PageTitle;
            top.Subtitle = current.PageSubtitle;
            top.Invalidate();
            Text = current.PageTitle + " · " + AppInfo.Name;
        }

        public void Toast(string text, BannerKind kind)
        {
            toast.Show(text, kind);
        }

        public void ShowPalette()
        {
            using (var palette = new CommandPalette(this)) palette.ShowDialog(this);
        }

        public void Compare(Snapshot older, Snapshot newer, Change focus)
        {
            var page = (ComparePage)GetPage("compare");
            page.Load(older, newer, focus);
            Navigate("compare", null);
        }

        public void ShowDevice(DeviceHistory d, int tab)
        {
            if (d == null) return;
            Navigate("device", new object[] { d, tab });
        }

        public void PolicyChanged(bool save)
        {
            if (save)
            {
                try
                {
                    workspace.Policy.Save(string.IsNullOrEmpty(workspace.Policy.FilePath) ? Policy.DefaultPath : workspace.Policy.FilePath);
                }
                catch (Exception ex)
                {
                    if (!(ex is IOException || ex is UnauthorizedAccessException)) throw;
                    Toast("Couldn't save the policy: " + ex.Message, BannerKind.Bad);
                }
            }
            workspace.Invalidate();
            RefreshAll();
        }

        public string SaveFileDialog(string title, string fileName, string filter)
        {
            using (var dlg = new SaveFileDialog { Title = title, FileName = fileName, Filter = filter, OverwritePrompt = true })
                return dlg.ShowDialog(this) == DialogResult.OK ? dlg.FileName : null;
        }


        public void OpenFolderDialog()
        {
            using (var dlg = new FolderBrowserDialog { Description = "Choose a folder of DeviceDossier XML reports. Subfolders are included.", ShowNewFolderButton = false })
            {
                if (!string.IsNullOrEmpty(workspace.Folder)) dlg.SelectedPath = workspace.Folder;
                else if (!string.IsNullOrEmpty(settings.LastFolder) && Directory.Exists(settings.LastFolder)) dlg.SelectedPath = settings.LastFolder;
                if (dlg.ShowDialog(this) == DialogResult.OK) OpenFolder(dlg.SelectedPath);
            }
        }

        public void OpenFolder(string folder)
        {
            if (scanning) return;
            scanning = true;
            rail.Busy = true;
            rail.Invalidate();
            overlay.ShowBusy("Reading audits…", folder);
            LayoutOverlays();
            var worker = new BackgroundWorker();
            var policy = workspace.Policy;
            bool recurse = settings.Recurse;
            worker.DoWork += (s, e) =>
            {
                var ws = Workspace.FromFolder(folder, recurse, policy);
                // Warm the caches off the UI thread so every page opens instantly.
                foreach (var d in ws.Devices)
                {
                    foreach (var r in d.Reports) ws.Posture(r);
                    for (int i = 1; i < d.Reports.Count; i++) ws.Compare(d.Reports[i - 1], d.Reports[i]);
                }
                var inv = ws.Inventory;
                e.Result = ws;
            };
            worker.RunWorkerCompleted += (s, e) =>
            {
                scanning = false;
                rail.Busy = false;
                overlay.HideOverlay();
                if (e.Error != null)
                {
                    Toast("Couldn't read that folder: " + e.Error.Message, BannerKind.Bad);
                    UpdateRail();
                    return;
                }
                var old = workspace;
                workspace = (Workspace)e.Result;
                settings.LastFolder = workspace.Folder;
                settings.AddRecent(workspace.Folder);
                settings.Save();
                StartWatching();
                RefreshAll();
                if (workspace.IsEmpty)
                    Toast("No DeviceDossier XML reports found in that folder" + (workspace.Skipped.Count > 0 ? " (" + workspace.Skipped.Count + " files skipped)." : "."), BannerKind.Warn);
                else
                {
                    string msg = "Loaded " + Fmt.Plural(workspace.ReportCount, "audit") + " of " + Fmt.Plural(workspace.Devices.Count, "device") + ".";
                    if (workspace.Skipped.Count > 0) msg += " Skipped " + Fmt.Plural(workspace.Skipped.Count, "file") + " that weren't DeviceDossier reports.";
                    Toast(msg, workspace.Skipped.Count > 0 ? BannerKind.Warn : BannerKind.Ok);
                    if (old.IsEmpty && current != null && current.Key == "dashboard") current.Activate(null);
                }
                string page = Environment.GetEnvironmentVariable("DRIFTWATCH_PAGE");
                if (!string.IsNullOrEmpty(page)) OpenStartupPage(page);
            };
            worker.RunWorkerAsync();
        }

        private void OpenStartupPage(string page)
        {
            // Used for screenshots and support: DRIFTWATCH_PAGE=device:NAME:TAB, compare, posture, etc.
            var parts = page.Split(':');
            if (parts[0] == "device" && parts.Length > 1)
            {
                int tab = 0;
                if (parts.Length > 2) int.TryParse(parts[2], out tab);
                ShowDevice(workspace.Find(parts[1]), tab);
            }
            else if (parts[0] == "compare")
            {
                var d = parts.Length > 1 ? workspace.Find(parts[1]) : workspace.Devices.FirstOrDefault(x => x.Previous != null && workspace.Latest(x).AnyAtOrAbove(Severity.Critical));
                if (d != null && d.Previous != null) Compare(d.Previous, d.Latest, null);
                else Navigate("compare", null);
            }
            else Navigate(parts[0], parts.Length > 1 ? parts[1] : null);
        }

        public void AddReportsDialog()
        {
            using (var dlg = new OpenFileDialog { Title = "Add DeviceDossier XML reports", Filter = "DeviceDossier XML reports (*.xml)|*.xml|All files (*.*)|*.*", Multiselect = true })
                if (dlg.ShowDialog(this) == DialogResult.OK) AddFiles(dlg.FileNames);
        }

        private void AddFiles(IEnumerable<string> paths)
        {
            var list = paths.ToList();
            var folders = list.Where(Directory.Exists).ToList();
            if (folders.Count > 0) { OpenFolder(folders[0]); return; }
            var errors = new List<string>();
            var added = workspace.AddFiles(list.Where(File.Exists), errors);
            RefreshAll();
            if (errors.Count > 0) Toast(string.Join("\n", errors.Take(3)), BannerKind.Bad);
            if (added.Count == 0) return;
            if (added.Count == 2 && string.Equals(added[0].Device, added[1].Device, StringComparison.OrdinalIgnoreCase))
            {
                var pair = added.OrderBy(a => a.Generated).ToList();
                Compare(pair[0], pair[1], null);
            }
            else if (added.Count == 1)
            {
                var d = workspace.DeviceOf(added[0]);
                if (d != null && d.Reports.Count >= 2 && d.Latest == added[0]) Compare(d.Previous, d.Latest, null);
                else ShowDevice(d, 0);
                if (errors.Count == 0) Toast("Added " + added[0].FileName + ".", BannerKind.Ok);
            }
            else if (errors.Count == 0) Toast("Added " + Fmt.Plural(added.Count, "report") + ".", BannerKind.Ok);
        }

        public void Rescan()
        {
            if (string.IsNullOrEmpty(workspace.Folder) || scanning) return;
            var before = new HashSet<string>(workspace.AllReports.Select(r => r.Path ?? ""), StringComparer.OrdinalIgnoreCase);
            Workspace fresh;
            try { fresh = Workspace.FromFolder(workspace.Folder, settings.Recurse, workspace.Policy); }
            catch (Exception ex)
            {
                if (!(ex is IOException || ex is UnauthorizedAccessException)) throw;
                return;
            }
            var added = fresh.AllReports.Where(r => !before.Contains(r.Path ?? "")).ToList();
            workspace = fresh;
            RefreshAll();
            foreach (var r in added.Take(3))
            {
                var d = workspace.DeviceOf(r);
                var cmp = d != null && d.Latest == r ? workspace.Latest(d) : null;
                string text = "New audit: " + r.Device + (cmp != null ? " · " + cmp.Verdict + (cmp.ActiveCount > 0 ? " (" + Fmt.SeverityCounts(cmp) + ")" : "") : "");
                var kind = cmp != null && cmp.AnyAtOrAbove(Severity.High) ? BannerKind.Bad : BannerKind.Info;
                Toast(text, kind);
                Notify(r.Device, text, kind);
            }
        }

        private void StartWatching()
        {
            if (watcher != null) { watcher.EnableRaisingEvents = false; watcher.Dispose(); watcher = null; }
            if (!settings.Watch || string.IsNullOrEmpty(workspace.Folder)) { UpdateRail(); return; }
            try
            {
                watcher = new FileSystemWatcher(workspace.Folder, "*.xml") { IncludeSubdirectories = settings.Recurse, NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite };
                FileSystemEventHandler h = (s, e) => BeginInvoke(new Action(() => { watchDebounce.Stop(); watchDebounce.Start(); }));
                watcher.Created += h;
                watcher.Changed += h;
                watcher.Deleted += h;
                watcher.Renamed += (s, e) => BeginInvoke(new Action(() => { watchDebounce.Stop(); watchDebounce.Start(); }));
                watcher.EnableRaisingEvents = true;
            }
            catch (Exception ex)
            {
                if (!(ex is ArgumentException || ex is IOException || ex is PlatformNotSupportedException || ex is NotImplementedException)) throw;
                watcher = null;
            }
            UpdateRail();
        }

        public void SettingsChanged()
        {
            settings.Save();
            StartWatching();
        }

        private void Notify(string title, string text, BannerKind kind)
        {
            if (!settings.Notify || ContainsFocus) return;
            try
            {
                if (tray == null)
                {
                    tray = new NotifyIcon { Icon = Icon ?? SystemIcons.Information, Text = AppInfo.Name, Visible = true };
                    tray.BalloonTipClicked += delegate { Activate(); if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal; };
                    tray.Click += delegate { Activate(); };
                }
                tray.ShowBalloonTip(6000, title, text, kind == BannerKind.Bad ? ToolTipIcon.Warning : ToolTipIcon.Info);
            }
            catch (Exception) { }
        }

        private void RefreshAll()
        {
            foreach (var p in pages.Values) p.RefreshData();
            if (current != null) UpdateTitle();
            UpdateRail();
        }

        private void UpdateRail()
        {
            rail.FolderName = string.IsNullOrEmpty(workspace.Folder) ? (workspace.IsEmpty ? "" : "Loose reports") : Path.GetFileName(workspace.Folder.TrimEnd('\\', '/'));
            if (string.IsNullOrEmpty(rail.FolderName) && !string.IsNullOrEmpty(workspace.Folder)) rail.FolderName = workspace.Folder;
            rail.FolderStats = workspace.IsEmpty ? "No reports loaded" : Fmt.Plural(workspace.Devices.Count, "device") + " · " + Fmt.Plural(workspace.ReportCount, "audit");
            rail.Watching = watcher != null && watcher.EnableRaisingEvents;
            int attention = workspace.Devices.Count(d => workspace.NeedsAttention(d));
            rail.SetBadge("devices", attention, Theme.Current.Bad);
            int risky = workspace.IsEmpty ? 0 : workspace.Inventory.Count(x => x.Risky);
            rail.SetBadge("software", risky, Theme.Current.High);
            int failing = workspace.Devices.Count(d => workspace.Posture(d.Latest).Score < workspace.Policy.MinimumScore);
            rail.SetBadge("posture", failing, Theme.Current.Warn);
            rail.Invalidate();
        }

        private void ToggleTheme()
        {
            settings.Theme = Theme.Current.Dark ? "light" : "dark";
            settings.Save();
            Theme.Apply(settings.Theme);
        }


        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.Control | Keys.K: ShowPalette(); return true;
                case Keys.Control | Keys.O: OpenFolderDialog(); return true;
                case Keys.Control | Keys.Shift | Keys.O: AddReportsDialog(); return true;
                case Keys.Control | Keys.F: if (current != null) current.FocusSearch(); return true;
                case Keys.F5: Rescan(); return true;
                case Keys.Control | Keys.D1: Navigate("dashboard", null); return true;
                case Keys.Control | Keys.D2: Navigate("devices", null); return true;
                case Keys.Control | Keys.D3: Navigate("compare", null); return true;
                case Keys.Control | Keys.D4: Navigate("posture", null); return true;
                case Keys.Control | Keys.D5: Navigate("software", null); return true;
                case Keys.Control | Keys.D6: Navigate("policy", null); return true;
                case Keys.Control | Keys.Oemcomma: Navigate("settings", null); return true;
                case Keys.Alt | Keys.Left: if (current != null && current.Key == "device") Navigate("devices", null); return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnDragEnter(DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop)) { e.Effect = DragDropEffects.Copy; overlay.ShowDrop(); LayoutOverlays(); }
            base.OnDragEnter(e);
        }

        protected override void OnDragLeave(EventArgs e)
        {
            overlay.HideOverlay();
            base.OnDragLeave(e);
        }

        protected override void OnDragDrop(DragEventArgs e)
        {
            overlay.HideOverlay();
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files != null && files.Length > 0) BeginInvoke(new Action(() => AddFiles(files)));
            base.OnDragDrop(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (watcher != null) watcher.Dispose();
            if (tray != null) { tray.Visible = false; tray.Dispose(); }
            watchDebounce.Dispose();
            Theme.Changed -= OnThemeChanged;
            base.OnFormClosed(e);
        }
    }
}
