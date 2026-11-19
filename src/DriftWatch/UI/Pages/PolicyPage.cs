using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using DriftWatch.Core;

namespace DriftWatch.UI.Pages
{
    /// <summary>Edit the organisation's rules. Every score and change updates as soon as it's saved.</summary>
    public sealed class PolicyPage : Page
    {
        private readonly ScrollHost host = new ScrollHost();
        private readonly Card softwareCard = new Card { Title = "Software rules", Subtitle = "Program names, one per line · matches anywhere in the name · * is a wildcard", Icon = "package" };
        private readonly Card accessCard = new Card { Title = "Accounts and access", Icon = "user" };
        private readonly Card thresholdCard = new Card { Title = "Thresholds", Icon = "clock" };
        private readonly Card ackCard = new Card { Title = "Acknowledged changes", Icon = "check-circle" };
        private readonly Surface actions = new Bar();
        private readonly List<Control[]> softwareRows = new List<Control[]>();
        private readonly TextField banned = new TextField(true, false), approved = new TextField(true, false), required = new TextField(true, false), admins = new TextField(true, false);
        private readonly TextField maxAdmins = new TextField(false, true), patchAge = new TextField(false, true), sigAge = new TextField(false, true), staleDays = new TextField(false, true), minScore = new TextField(false, true);
        private readonly Toggle allowRdp = new Toggle("Allow Remote Desktop", "Don't flag Remote Desktop being turned on");
        private readonly Lbl ackText = new Lbl("", Fonts.Body, Tone.Soft) { WrapText = true };
        private readonly Btn clearAck = new Btn("Clear all", "refresh", ButtonStyle.Secondary) { Compact = true, Height = Dpi.S(32) };
        private readonly Btn save = new Btn("Save policy", "check", ButtonStyle.Primary);
        private readonly Btn revert = new Btn("Discard changes", null, ButtonStyle.Secondary);
        private readonly Btn import = new Btn("Import…", "upload", ButtonStyle.Ghost);
        private readonly Btn exportBtn = new Btn("Export…", "download", ButtonStyle.Ghost);
        private readonly Btn reset = new Btn("Reset to defaults", null, ButtonStyle.Ghost);
        private readonly Lbl status = new Lbl("", Fonts.Small, Tone.Muted);
        private readonly List<KeyValuePair<Lbl, Control>> thresholdRows = new List<KeyValuePair<Lbl, Control>>();
        private readonly Lbl adminsLabel = new Lbl("Approved administrator accounts", Fonts.Semi, Tone.Ink);
        private readonly Lbl adminsHint = new Lbl("When set, any other admin fails the check, and adding an approved one is only Low.", Fonts.Small, Tone.Muted) { WrapText = true };
        private readonly Lbl maxLabel = new Lbl("Most administrators allowed", Fonts.Semi, Tone.Ink);
        private bool loading, dirty;

        public PolicyPage(IShell shell) : base(shell)
        {
            host.Dock = DockStyle.Fill;
            Controls.Add(host);
            foreach (var c in new Control[] { softwareCard, accessCard, thresholdCard, ackCard }) host.Controls.Add(c);
            actions.Dock = DockStyle.Top;
            Controls.Add(actions);

            AddSoftwareRow("Banned software", "Installing one is High and fails the software check. Example: utorrent, steam, *crack*", banned);
            AddSoftwareRow("Approved software", "Tools your IT uses on purpose, such as your remote-support app. They stop being flagged.", approved);
            AddSoftwareRow("Required software", "Must be on every PC, such as your EDR agent. Missing fails a check; removal is High.", required);

            foreach (var c in new Control[] { adminsLabel, adminsHint, admins, maxLabel, maxAdmins, allowRdp }) accessCard.Controls.Add(c);
            admins.Height = Dpi.S(84);
            maxAdmins.Width = Dpi.S(110);

            AddThreshold("Security updates older than (days)", patchAge);
            AddThreshold("Malware definitions older than (days)", sigAge);
            AddThreshold("Audit overdue after (days)", staleDays);
            AddThreshold("Minimum security score (0-100)", minScore);

            ackCard.Controls.Add(ackText);
            ackCard.Controls.Add(clearAck);
            clearAck.FitWidth();
            clearAck.Click += delegate
            {
                int n = Ws.Policy.Acknowledged.Count;
                if (n == 0) return;
                if (MessageBox.Show(Shell.Window, "Clear all " + n + " acknowledged changes? They'll count towards the verdicts again.", "DriftWatch", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
                Ws.Policy.Acknowledged.Clear();
                Shell.PolicyChanged(true);
                Shell.Toast("Cleared " + Fmt.Plural(n, "acknowledged change") + ".", BannerKind.Ok);
            };

            foreach (var b in new[] { save, revert, import, exportBtn, reset }) { b.FitWidth(); actions.Controls.Add(b); }
            actions.Controls.Add(status);
            save.Click += delegate { Save(); };
            revert.Click += delegate { LoadFields(); };
            reset.Click += delegate
            {
                if (MessageBox.Show(Shell.Window, "Reset every rule and threshold to the defaults? Acknowledged changes are kept.", "DriftWatch", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
                var p = new Policy { FilePath = Ws.Policy.FilePath };
                p.Acknowledged.AddRange(Ws.Policy.Acknowledged);
                Ws.SetPolicy(p);
                Shell.PolicyChanged(true);
                LoadFields();
                Shell.Toast("Policy reset to the defaults.", BannerKind.Ok);
            };
            import.Click += delegate { Import(); };
            exportBtn.Click += delegate { Export(); };

            foreach (var f in new[] { banned, approved, required, admins, maxAdmins, patchAge, sigAge, staleDays, minScore }) f.ValueChanged += delegate { MarkDirty(); };
            allowRdp.CheckedChanged += delegate { MarkDirty(); };
            host.Resize += delegate { LayoutForm(); };
            LoadFields();
        }

        public override string Key { get { return "policy"; } }
        public override string PageTitle { get { return "Policy"; } }
        public override string PageSubtitle { get { return "Rules for software, accounts and thresholds, used by every score"; } }

        private void AddSoftwareRow(string title, string hint, TextField field)
        {
            var l = new Lbl(title, Fonts.Semi, Tone.Ink);
            var h = new Lbl(hint, Fonts.Small, Tone.Muted) { WrapText = true };
            field.Height = Dpi.S(92);
            softwareCard.Controls.Add(l);
            softwareCard.Controls.Add(h);
            softwareCard.Controls.Add(field);
            softwareRows.Add(new Control[] { l, h, field });
        }

        private void AddThreshold(string title, TextField field)
        {
            var l = new Lbl(title, Fonts.Semi, Tone.Ink) { WrapText = true };
            thresholdCard.Controls.Add(l);
            thresholdCard.Controls.Add(field);
            thresholdRows.Add(new KeyValuePair<Lbl, Control>(l, field));
        }

        private void MarkDirty()
        {
            if (loading) return;
            dirty = true;
            UpdateStatus();
        }

        private void UpdateStatus()
        {
            status.Text = dirty ? "Unsaved changes" : "Saved to " + (Ws.Policy.FilePath ?? Policy.DefaultPath);
            status.CustomColor = dirty ? (Color?)T.Warn : null;
            save.Enabled = dirty;
            revert.Enabled = dirty;
            int n = Ws.Policy.Acknowledged.Count;
            ackText.Text = n == 0 ? "No changes are acknowledged. In Compare, use \"Mark as expected\" on a change you've checked, so it stops counting towards the verdict." : Fmt.Plural(n, "change") + " marked as expected. They stay listed in Compare, greyed out, but don't affect verdicts, exit codes or attention counts.";
            clearAck.Enabled = n > 0;
            ackText.Invalidate();
            status.Invalidate();
        }

        public override void RefreshData()
        {
            if (!dirty) LoadFields();
            else UpdateStatus();
        }

        private void LoadFields()
        {
            loading = true;
            var p = Ws.Policy;
            banned.Value = string.Join(Environment.NewLine, p.BannedSoftware);
            approved.Value = string.Join(Environment.NewLine, p.ApprovedSoftware);
            required.Value = string.Join(Environment.NewLine, p.RequiredSoftware);
            admins.Value = string.Join(Environment.NewLine, p.ApprovedAdmins);
            maxAdmins.Value = p.MaxAdmins.ToString(CultureInfo.InvariantCulture);
            patchAge.Value = p.MaxPatchAgeDays.ToString(CultureInfo.InvariantCulture);
            sigAge.Value = p.MaxSignatureAgeDays.ToString(CultureInfo.InvariantCulture);
            staleDays.Value = p.StaleAuditDays.ToString(CultureInfo.InvariantCulture);
            minScore.Value = p.MinimumScore.ToString(CultureInfo.InvariantCulture);
            allowRdp.Checked = p.AllowRemoteDesktop;
            loading = false;
            dirty = false;
            UpdateStatus();
        }

        private Policy FromFields()
        {
            var old = Ws.Policy;
            var p = new Policy { Name = old.Name, FilePath = old.FilePath };
            p.BannedSoftware.AddRange(banned.Lines());
            p.ApprovedSoftware.AddRange(approved.Lines());
            p.RequiredSoftware.AddRange(required.Lines());
            p.ApprovedAdmins.AddRange(admins.Lines());
            p.Acknowledged.AddRange(old.Acknowledged);
            p.MaxAdmins = Clamp(maxAdmins.Number(old.MaxAdmins), 1, 50);
            p.MaxPatchAgeDays = Clamp(patchAge.Number(old.MaxPatchAgeDays), 1, 3650);
            p.MaxSignatureAgeDays = Clamp(sigAge.Number(old.MaxSignatureAgeDays), 1, 365);
            p.StaleAuditDays = Clamp(staleDays.Number(old.StaleAuditDays), 1, 3650);
            p.MinimumScore = Clamp(minScore.Number(old.MinimumScore), 0, 100);
            p.AllowRemoteDesktop = allowRdp.Checked;
            return p;
        }

        private static int Clamp(int v, int lo, int hi) { return v < lo ? lo : v > hi ? hi : v; }

        private void Save()
        {
            Ws.SetPolicy(FromFields());
            dirty = false;
            Shell.PolicyChanged(true);
            LoadFields();
            Shell.Toast("Policy saved. Scores and changes have been re-checked.", BannerKind.Ok);
        }

        private void Import()
        {
            using (var dlg = new OpenFileDialog { Title = "Import a DriftWatch policy", Filter = "DriftWatch policy (*.json)|*.json|All files (*.*)|*.*" })
            {
                if (dlg.ShowDialog(Shell.Window) != DialogResult.OK) return;
                try
                {
                    var p = Policy.Load(dlg.FileName);
                    p.FilePath = Ws.Policy.FilePath ?? Policy.DefaultPath;
                    Ws.SetPolicy(p);
                    Shell.PolicyChanged(true);
                    dirty = false;
                    LoadFields();
                    Shell.Toast("Imported " + Path.GetFileName(dlg.FileName) + ".", BannerKind.Ok);
                }
                catch (Exception ex)
                {
                    if (!(ex is FormatException || ex is IOException || ex is UnauthorizedAccessException)) throw;
                    Shell.Toast("Couldn't import that file: " + ex.Message, BannerKind.Bad);
                }
            }
        }

        private void Export()
        {
            string path = Shell.SaveFileDialog("Export policy", "DriftWatch_Policy.json", "DriftWatch policy (*.json)|*.json");
            if (path == null) return;
            try
            {
                var copy = FromFields();
                copy.Save(path);
                Shell.Toast("Exported the policy. Use it on another PC with Import, or with --policy on the command line.", BannerKind.Ok);
            }
            catch (Exception ex)
            {
                if (!(ex is IOException || ex is UnauthorizedAccessException)) throw;
                Shell.Toast("Couldn't export: " + ex.Message, BannerKind.Bad);
            }
        }

        private void LayoutForm()
        {
            int pad = Pad, gap = Gap;
            int oy = host.AutoScrollPosition.Y;
            int w = host.ClientSize.Width - pad * 2;
            int left = (int)((w - gap) * 0.55);
            int right = w - left - gap;
            int y = Dpi.S(4);

            int inner = left - Dpi.S(36);
            int cy = softwareCard.HeaderHeight;
            foreach (var row in softwareRows)
            {
                var l = (Lbl)row[0];
                var h = (Lbl)row[1];
                l.Bounds = new Rectangle(Dpi.S(18), cy, inner, l.Font.Height + Dpi.S(4));
                cy += l.Height + Dpi.S(2);
                int hh = h.HeightFor(inner);
                h.Bounds = new Rectangle(Dpi.S(18), cy, inner, hh);
                cy += hh + Dpi.S(8);
                row[2].Bounds = new Rectangle(Dpi.S(18), cy, inner, row[2].Height);
                cy += row[2].Height + Dpi.S(20);
            }
            int softH = cy;

            int ri = right - Dpi.S(36);
            int ay = accessCard.HeaderHeight;
            adminsLabel.Bounds = new Rectangle(Dpi.S(18), ay, ri, adminsLabel.Font.Height + Dpi.S(4));
            ay += adminsLabel.Height + Dpi.S(2);
            int ahh = adminsHint.HeightFor(ri);
            adminsHint.Bounds = new Rectangle(Dpi.S(18), ay, ri, ahh);
            ay += ahh + Dpi.S(8);
            admins.Bounds = new Rectangle(Dpi.S(18), ay, ri, admins.Height);
            ay += admins.Height + Dpi.S(16);
            maxLabel.Bounds = new Rectangle(Dpi.S(18), ay + Dpi.S(8), ri - maxAdmins.Width - Dpi.S(12), maxLabel.Font.Height + Dpi.S(4));
            maxAdmins.Location = new Point(Dpi.S(18) + ri - maxAdmins.Width, ay);
            ay += maxAdmins.Height + Dpi.S(14);
            allowRdp.Bounds = new Rectangle(Dpi.S(18), ay, ri, allowRdp.Height);
            ay += allowRdp.Height + Dpi.S(16);
            int accessH = ay;

            int ty = thresholdCard.HeaderHeight;
            int colW = (ri - Dpi.S(16)) / 2;
            for (int i = 0; i < thresholdRows.Count; i++)
            {
                int col = i % 2;
                int x = Dpi.S(18) + col * (colW + Dpi.S(16));
                int rowY = ty + (i / 2) * Dpi.S(92);
                var l = thresholdRows[i].Key;
                l.Bounds = new Rectangle(x, rowY, colW, l.HeightFor(colW));
                thresholdRows[i].Value.Bounds = new Rectangle(x, rowY + Dpi.S(44), colW, Dpi.S(36));
            }
            int thrH = ty + ((thresholdRows.Count + 1) / 2) * Dpi.S(92) + Dpi.S(4);

            int ky = ackCard.HeaderHeight;
            int kh = ackText.HeightFor(inner);
            ackText.Bounds = new Rectangle(Dpi.S(18), ky, inner, kh);
            clearAck.Location = new Point(Dpi.S(18), ky + kh + Dpi.S(12));
            int ackH = ky + kh + Dpi.S(12) + clearAck.Height + Dpi.S(18);

            int rightH = accessH + gap + thrH;
            int leftH = Math.Max(softH, rightH - ackH - gap);
            softwareCard.Bounds = new Rectangle(pad, y + oy, left, leftH);
            ackCard.Bounds = new Rectangle(pad, y + oy + leftH + gap, left, ackH);
            accessCard.Bounds = new Rectangle(pad + left + gap, y + oy, right, accessH);
            thresholdCard.Bounds = new Rectangle(pad + left + gap, y + oy + accessH + gap, right, Math.Max(thrH, leftH + gap + ackH - accessH - gap));
            int ay2 = Math.Max(softwareCard.Bottom, ackCard.Bottom) - oy;

            actions.Height = Dpi.S(56);
            int bx = pad, by = Dpi.S(4);
            save.Location = new Point(bx, by);
            revert.Location = new Point(save.Right + Dpi.S(8), by);
            int rx = actions.Width - pad;
            foreach (var b in new[] { reset, exportBtn, import }) { rx -= b.Width; b.Location = new Point(rx, by); rx -= Dpi.S(4); }
            status.Bounds = new Rectangle(revert.Right + Dpi.S(16), by, Math.Max(0, rx - revert.Right - Dpi.S(28)), save.Height);
            status.Align = Draw.Left | TextFormatFlags.PathEllipsis;
            host.AutoScrollMinSize = new Size(0, ay2 + Dpi.S(24));
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            LayoutForm();
        }

        private sealed class Bar : Surface { }
    }
}
