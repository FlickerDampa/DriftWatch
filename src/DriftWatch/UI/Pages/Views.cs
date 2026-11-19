using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using DriftWatch.Core;

namespace DriftWatch.UI.Pages
{
    /// <summary>Label/value pairs in one or two columns.</summary>
    public sealed class FactsView : Surface
    {
        private readonly List<KeyValuePair<string, string>> facts = new List<KeyValuePair<string, string>>();
        public int Columns = 2;

        public void SetFacts(IEnumerable<KeyValuePair<string, string>> list)
        {
            facts.Clear();
            facts.AddRange(list.Where(f => !string.IsNullOrEmpty(f.Value)));
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            int cols = Width < Dpi.S(420) ? 1 : Columns;
            int colW = (Width - Dpi.S(24) * (cols - 1)) / cols;
            int rowH = Dpi.S(46);
            for (int i = 0; i < facts.Count; i++)
            {
                int col = i % cols, row = i / cols;
                int x = col * (colW + Dpi.S(24)), y = row * rowH;
                if (y + rowH > Height + Dpi.S(4)) break;
                Draw.Text(g, facts[i].Key.ToUpperInvariant(), Fonts.Tiny, T.Muted, new Rectangle(x, y + Dpi.S(4), colW, Dpi.S(16)));
                Draw.Text(g, facts[i].Value, Fonts.Body, T.Ink, new Rectangle(x, y + Dpi.S(20), colW, Dpi.S(20)));
                using (var pen = new Pen(T.LineSoft, 1f)) g.DrawLine(pen, x, y + rowH - Dpi.S(3), x + colW, y + rowH - Dpi.S(3));
            }
        }
    }

    /// <summary>Explains one security check: what the audit shows and what to do.</summary>
    public sealed class CheckDetailView : Surface
    {
        private PostureCheck check;
        private string device = "";
        private int totalWeight = 100;

        public string EmptyText = "Select a check to see the details.";

        public void SetCheck(PostureCheck c, PostureResult r, string deviceName)
        {
            check = c;
            device = deviceName ?? "";
            totalWeight = r == null ? 100 : Math.Max(1, r.Checks.Where(x => x.Status != CheckStatus.Unknown).Sum(x => x.Weight));
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            if (check == null) { Draw.Text(g, EmptyText, Fonts.Body, T.Muted, ClientRectangle, Draw.Center); return; }
            int y = 0, w = Width;
            var c = T.For(check.Status);
            Draw.Tag(g, Cells.StatusWord(check.Status), c, T, 0, y + Dpi.S(11));
            if (device.Length > 0)
            {
                int tw = Draw.TagWidth(Cells.StatusWord(check.Status));
                Draw.Text(g, device, Fonts.Small, T.Muted, new Rectangle(tw + Dpi.S(10), y, w - tw - Dpi.S(10), Dpi.S(22)));
            }
            y += Dpi.S(34);
            int th = Draw.MeasureHeight(check.Title, Fonts.H2, w);
            Draw.Text(g, check.Title, Fonts.H2, T.Ink, new Rectangle(0, y, w, th + 2), Draw.Wrap);
            y += th + Dpi.S(4);
            string weight = check.Status == CheckStatus.Unknown ? "Not counted in the score" : "Worth " + Math.Round(check.Weight * 100.0 / totalWeight) + " of 100 points";
            Draw.Text(g, check.Group + " · " + weight, Fonts.Small, T.Muted, new Rectangle(0, y, w, Dpi.S(18)));
            y += Dpi.S(32);
            y = Section(g, "WHAT THE AUDIT SHOWS", check.Detail, y, w, T.Soft);
            if (!string.IsNullOrEmpty(check.Advice))
            {
                y += Dpi.S(8);
                int ah = Draw.MeasureHeight(check.Advice, Fonts.Body, w - Dpi.S(46)) + Dpi.S(56);
                var box = new Rectangle(0, y, w - 1, ah);
                Draw.Smooth(g);
                Draw.Fill(g, box, Dpi.S(10), T.Tint(c, T.Dark ? 0.1 : 0.07));
                Draw.Stroke(g, box, Dpi.S(10), T.Tint(c, 0.3), 1f);
                Icons.Draw(g, "sparkle", new RectangleF(Dpi.S(14), y + Dpi.S(14), Dpi.S(16), Dpi.S(16)), c, 1.8f);
                Draw.Text(g, "WHAT TO DO", Fonts.Tiny, c, new Rectangle(Dpi.S(38), y + Dpi.S(14), w, Dpi.S(16)));
                Draw.Text(g, check.Advice, Fonts.Body, T.Ink, new Rectangle(Dpi.S(38), y + Dpi.S(36), w - Dpi.S(52), ah - Dpi.S(40)), Draw.Wrap);
            }
        }

        private int Section(Graphics g, string label, string text, int y, int w, Color ink)
        {
            Draw.Text(g, label, Fonts.Tiny, T.Muted, new Rectangle(0, y, w, Dpi.S(16)));
            y += Dpi.S(20);
            int h = Draw.MeasureHeight(text, Fonts.Body, w);
            Draw.Text(g, text, Fonts.Body, ink, new Rectangle(0, y, w, h + 2), Draw.Wrap);
            return y + h + Dpi.S(12);
        }
    }

    /// <summary>Detail view for one change: severity, before and after, and why it matters.</summary>
    public sealed class ChangeDetailView : Surface
    {
        private Change change;
        public readonly Btn AckButton, CopyButton;
        public int ContentBottom { get; private set; }

        public ChangeDetailView()
        {
            AckButton = new Btn("Mark as expected", "check", ButtonStyle.Secondary) { Compact = true, Height = Dpi.S(32) };
            AckButton.FitWidth();
            CopyButton = new Btn("Copy", "copy", ButtonStyle.Ghost) { Compact = true, Height = Dpi.S(32) };
            CopyButton.FitWidth();
            Controls.Add(AckButton);
            Controls.Add(CopyButton);
            SetChange(null);
        }

        public Change Change { get { return change; } }

        public void SetChange(Change c)
        {
            change = c;
            AckButton.Visible = CopyButton.Visible = c != null;
            if (c != null)
            {
                AckButton.Text = c.Acknowledged ? "Undo acknowledge" : "Mark as expected";
                AckButton.IconName = c.Acknowledged ? "refresh" : "check";
                AckButton.FitWidth();
            }
            PerformLayout();
            Invalidate();
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            AckButton.Location = new Point(0, Height - AckButton.Height);
            CopyButton.Location = new Point(AckButton.Right + Dpi.S(8), Height - CopyButton.Height);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            if (change == null)
            {
                int s = Dpi.S(26);
                Icons.Draw(g, "eye", new RectangleF((Width - s) / 2f, Height / 2f - Dpi.S(36), s, s), T.Faint, 1.6f);
                Draw.Text(g, "Select a change to see what it means.", Fonts.Body, T.Muted, new Rectangle(0, Height / 2 - Dpi.S(4), Width, Dpi.S(22)), Draw.Center);
                return;
            }
            int w = Width, y = 0;
            var c = T.For(change.Severity);
            int x = Draw.Tag(g, change.Severity.ToString(), c, T, 0, Dpi.S(11)) + Dpi.S(6);
            x += Draw.Tag(g, change.Category, T.Info, T, x, Dpi.S(11)) + Dpi.S(6);
            x += Draw.Tag(g, change.KindText, change.Kind == ChangeKind.Added ? T.Ok : change.Kind == ChangeKind.Removed ? T.Bad : T.Accent, T, x, Dpi.S(11)) + Dpi.S(6);
            if (change.Acknowledged) Draw.Tag(g, "Acknowledged", T.Faint, T, x, Dpi.S(11));
            y += Dpi.S(36);
            int th = Draw.MeasureHeight(change.Title, Fonts.H2, w);
            Draw.Text(g, change.Title, Fonts.H2, T.Ink, new Rectangle(0, y, w, th + 2), Draw.Wrap);
            y += th + Dpi.S(4);
            string where = change.SectionTitle + (string.IsNullOrEmpty(change.Table) ? "" : " › " + change.Table);
            Draw.Text(g, where, Fonts.Small, T.Muted, new Rectangle(0, y, w, Dpi.S(18)));
            y += Dpi.S(30);

            if (change.Kind == ChangeKind.Changed)
            {
                y = ValueBox(g, "BEFORE", change.Before, T.BeforeBg, T.BeforeInk, y, w, "minus-circle");
                y = ValueBox(g, "AFTER", change.After, T.AfterBg, T.AfterInk, y + Dpi.S(8), w, "check-circle");
            }
            else if (change.Row != null && change.Row.Count > 0)
            {
                Draw.Text(g, change.Kind == ChangeKind.Added ? "NEW ENTRY" : "REMOVED ENTRY", Fonts.Tiny, T.Muted, new Rectangle(0, y, w, Dpi.S(16)));
                y += Dpi.S(22);
                foreach (var kv in change.Row.Where(k => !TextUtil.IsBlank(k.Value) && k.Key != "No.").Take(7))
                {
                    Draw.Text(g, kv.Key, Fonts.Small, T.Muted, new Rectangle(0, y, Dpi.S(130), Dpi.S(20)));
                    Draw.Text(g, kv.Value, Fonts.Mono, T.Ink, new Rectangle(Dpi.S(136), y, w - Dpi.S(136), Dpi.S(20)));
                    y += Dpi.S(22);
                }
                y += Dpi.S(4);
            }
            if (!string.IsNullOrEmpty(change.PolicyNote))
            {
                y += Dpi.S(6);
                Icons.Draw(g, "policy", new RectangleF(0, y + Dpi.S(1), Dpi.S(15), Dpi.S(15)), T.Accent2, 2f);
                Draw.Text(g, change.PolicyNote, Fonts.SmallSemi, T.Accent2, new Rectangle(Dpi.S(22), y, w - Dpi.S(22), Dpi.S(18)));
                y += Dpi.S(26);
            }
            if (!string.IsNullOrEmpty(change.Why))
            {
                y += Dpi.S(8);
                Draw.Text(g, "WHY IT MATTERS", Fonts.Tiny, T.Muted, new Rectangle(0, y, w, Dpi.S(16)));
                y += Dpi.S(22);
                int wh = Draw.MeasureHeight(change.Why, Fonts.Body, w);
                int maxH = Height - AckButton.Height - Dpi.S(16) - y;
                Draw.Text(g, change.Why, Fonts.Body, T.Soft, new Rectangle(0, y, w, Math.Min(wh + 2, Math.Max(0, maxH))), Draw.Wrap | TextFormatFlags.EndEllipsis);
                y += wh;
            }
            ContentBottom = y;
        }

        private int ValueBox(Graphics g, string label, string value, Color bg, Color ink, int y, int w, string icon)
        {
            string v = Change.Display(value);
            int h = Math.Max(Dpi.S(40), Draw.MeasureHeight(v, Fonts.Mono, w - Dpi.S(96)) + Dpi.S(22));
            h = Math.Min(h, Dpi.S(110));
            var r = new Rectangle(0, y, w - 1, h);
            Draw.Smooth(g);
            Draw.Fill(g, r, Dpi.S(8), bg);
            Draw.Text(g, label, Fonts.Tiny, ink, new Rectangle(Dpi.S(12), y, Dpi.S(70), Dpi.S(40)));
            Draw.Text(g, v, Fonts.Mono, T.Ink, new Rectangle(Dpi.S(82), y + Dpi.S(11), w - Dpi.S(96), h - Dpi.S(16)), Draw.Wrap | TextFormatFlags.EndEllipsis);
            return y + h;
        }
    }
}
