using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using DriftWatch.Core;

namespace DriftWatch.UI.Pages
{
    /// <summary>Reusable table cell painters.</summary>
    public static class Cells
    {
        public static void TwoLine(CellArgs a, string title, string sub, string icon, Color? iconColor)
        {
            int x = a.Bounds.X;
            if (!string.IsNullOrEmpty(icon))
            {
                int s = Dpi.S(32);
                var ib = new Rectangle(x, a.Bounds.Y + (a.Bounds.Height - s) / 2, s, s);
                var c = iconColor ?? a.T.Accent;
                Draw.Smooth(a.G);
                Draw.Fill(a.G, ib, Dpi.S(8), a.T.Tint(c, a.T.Dark ? 0.14 : 0.1));
                Icons.Draw(a.G, icon, new RectangleF(ib.X + Dpi.S(8), ib.Y + Dpi.S(8), s - Dpi.S(16), s - Dpi.S(16)), c, 2f);
                x += s + Dpi.S(12);
            }
            int w = a.Bounds.Right - x;
            if (string.IsNullOrEmpty(sub))
            {
                Draw.Text(a.G, title, Fonts.Semi, a.T.Ink, new Rectangle(x, a.Bounds.Y, w, a.Bounds.Height));
                return;
            }
            int mid = a.Bounds.Y + a.Bounds.Height / 2;
            Draw.Text(a.G, title, Fonts.Semi, a.T.Ink, new Rectangle(x, mid - Dpi.S(19), w, Dpi.S(20)));
            Draw.Text(a.G, sub, Fonts.Small, a.T.Muted, new Rectangle(x, mid + Dpi.S(1), w, Dpi.S(18)));
        }

        public static void Score(CellArgs a, int score)
        {
            int s = Dpi.S(26);
            var r = new RectangleF(a.Bounds.X, a.Bounds.Y + (a.Bounds.Height - s) / 2f, s, s);
            var c = a.T.ForScore(score);
            Draw.Ring(a.G, r, Dpi.S(3.5f), score / 100.0, a.T.LineSoft, c, Theme.Mix(c, a.T.Accent2, 0.5));
            int x = (int)r.Right + Dpi.S(10);
            string n = score.ToString(CultureInfo.InvariantCulture);
            int nw = Draw.Measure(n, Fonts.Semi).Width;
            Draw.Text(a.G, n, Fonts.Semi, a.T.Ink, new Rectangle(x, a.Bounds.Y, nw + 2, a.Bounds.Height));
            Draw.Text(a.G, PostureResult.GradeFor(score), Fonts.SmallSemi, c, new Rectangle(x + nw + Dpi.S(6), a.Bounds.Y, Dpi.S(20), a.Bounds.Height));
        }

        public static void SeverityPill(CellArgs a, Severity s, bool dim)
        {
            var c = a.T.For(s);
            if (dim) c = Theme.Mix(c, a.T.Card, 0.55);
            Draw.Tag(a.G, s.ToString(), c, a.T, a.Bounds.X, a.Bounds.Y + a.Bounds.Height / 2);
        }

        public static void Changes(CellArgs a, Comparison c)
        {
            if (c == null)
            {
                Draw.Text(a.G, "One audit only", Fonts.Small, a.T.Faint, a.Bounds);
                return;
            }
            var h = c.Highest;
            int mid = a.Bounds.Y + a.Bounds.Height / 2;
            if (h == null)
            {
                Draw.Tag(a.G, c.Changes.Count == 0 ? "No changes" : "All acknowledged", a.T.Ok, a.T, a.Bounds.X, mid);
                return;
            }
            int w = Draw.Tag(a.G, h.Value.ToString(), a.T.For(h.Value), a.T, a.Bounds.X, mid);
            var top = c.Active.First();
            Draw.Text(a.G, top.Title, Fonts.Small, a.T.Soft, new Rectangle(a.Bounds.X + w + Dpi.S(10), a.Bounds.Y, a.Bounds.Width - w - Dpi.S(10), a.Bounds.Height));
        }

        public static void Bar(CellArgs a, Comparison c)
        {
            if (c == null) { Draw.Text(a.G, "–", Fonts.Small, a.T.Faint, a.Bounds); return; }
            var counts = Fmt.Counts(c);
            int total = counts.Sum();
            string label = total.ToString(CultureInfo.InvariantCulture);
            int lw = Dpi.S(30);
            var r = new Rectangle(a.Bounds.X, a.Bounds.Y + a.Bounds.Height / 2 - Dpi.S(3), Math.Max(Dpi.S(20), a.Bounds.Width - lw - Dpi.S(8)), Dpi.S(6));
            Draw.SeverityBar(a.G, r, counts, a.T);
            Draw.Text(a.G, label, Fonts.SmallSemi, total > 0 ? a.T.Ink : a.T.Faint, new Rectangle(r.Right + Dpi.S(8), a.Bounds.Y, lw, a.Bounds.Height));
        }

        public static void Status(CellArgs a, CheckStatus s, bool withText)
        {
            int size = Dpi.S(withText ? 18 : 22);
            var c = a.T.For(s);
            int x = withText ? a.Bounds.X : a.Bounds.X + (a.Bounds.Width - size) / 2;
            var r = new Rectangle(x, a.Bounds.Y + (a.Bounds.Height - size) / 2, size, size);
            Draw.Smooth(a.G);
            Draw.Fill(a.G, r, Dpi.S(6), a.T.Tint(c, a.T.Dark ? (s == CheckStatus.Unknown ? 0.1 : 0.22) : 0.15));
            string icon = s == CheckStatus.Pass ? "check" : s == CheckStatus.Fail ? "x" : s == CheckStatus.Warn ? "alert" : "minus-circle";
            int pad = Dpi.S(4);
            if (s == CheckStatus.Unknown) Icons.Draw(a.G, "dot", new RectangleF(r.X + pad * 1.5f, r.Y + pad * 1.5f, size - pad * 3, size - pad * 3), Theme.Mix(c, a.T.Card, 0.2), 2f);
            else Icons.Draw(a.G, icon, new RectangleF(r.X + pad, r.Y + pad, size - pad * 2, size - pad * 2), c, s == CheckStatus.Warn ? 2.2f : 2.8f);
            if (withText) Draw.Text(a.G, StatusWord(s), Fonts.SmallSemi, c, new Rectangle(r.Right + Dpi.S(8), a.Bounds.Y, a.Bounds.Width - size - Dpi.S(8), a.Bounds.Height));
        }

        public static string StatusWord(CheckStatus s)
        {
            return s == CheckStatus.Pass ? "Pass" : s == CheckStatus.Warn ? "Warning" : s == CheckStatus.Fail ? "Fail" : "Not reported";
        }

        public static void Spark(CellArgs a, IList<double> values)
        {
            if (values.Count < 2) { Draw.Text(a.G, "–", Fonts.Small, a.T.Faint, a.Bounds); return; }
            var r = new Rectangle(a.Bounds.X, a.Bounds.Y + Dpi.S(12), Math.Min(a.Bounds.Width, Dpi.S(90)), a.Bounds.Height - Dpi.S(24));
            double delta = values[values.Count - 1] - values[0];
            var c = delta < -2 ? a.T.Bad : delta > 2 ? a.T.Ok : a.T.Accent;
            Draw.Sparkline(a.G, r, values, 0, 100, c);
        }

        public static Color DevicePalette(Theme t, int i)
        {
            var list = new[] { t.Low, t.Accent2, t.High, t.Ok, t.Medium, Theme.Hex("#EC4899"), Theme.Hex("#14B8A6"), t.Info };
            return list[i % list.Length];
        }

        public static string DeviceIcon(Snapshot s)
        {
            return s != null && s.Laptop ? "laptop" : "desktop";
        }

        /// <summary>Opens a save dialog, writes the text, and reports the result.</summary>
        public static void SaveText(IShell shell, string title, string fileName, string filter, Func<string> build)
        {
            string path = shell.SaveFileDialog(title, fileName, filter);
            if (path == null) return;
            try
            {
                System.IO.File.WriteAllText(path, build(), new System.Text.UTF8Encoding(path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)));
                shell.Toast("Saved " + System.IO.Path.GetFileName(path) + ".", BannerKind.Ok);
            }
            catch (Exception ex)
            {
                if (!(ex is System.IO.IOException || ex is UnauthorizedAccessException)) throw;
                shell.Toast("Couldn't save: " + ex.Message, BannerKind.Bad);
            }
        }

        public static void Copy(IShell shell, string text, string what)
        {
            try
            {
                Clipboard.SetText(text);
                shell.Toast(what + " copied to the clipboard.", BannerKind.Ok);
            }
            catch (Exception ex)
            {
                if (!(ex is System.Runtime.InteropServices.ExternalException || ex is System.Threading.ThreadStateException)) throw;
                shell.Toast("The clipboard is busy. Try again.", BannerKind.Warn);
            }
        }

        public static string Stamp()
        {
            return DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        public static string SafeName(string s)
        {
            foreach (var c in System.IO.Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s;
        }
    }
}
