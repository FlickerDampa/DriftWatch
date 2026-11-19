using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DriftWatch.UI
{
    /// <summary>Drawing helpers shared by every custom control.</summary>
    public static class Draw
    {
        public const TextFormatFlags Left = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
        public const TextFormatFlags Center = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
        public const TextFormatFlags Right = TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
        public const TextFormatFlags Wrap = TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.TextBoxControl;

        public static void Smooth(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        }

        public static GraphicsPath Round(RectangleF r, float radius)
        {
            var p = new GraphicsPath();
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            if (d <= 1) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void Fill(Graphics g, RectangleF r, float radius, Color c)
        {
            using (var p = Round(r, radius))
            using (var b = new SolidBrush(c)) g.FillPath(b, p);
        }

        public static void Stroke(Graphics g, RectangleF r, float radius, Color c, float width)
        {
            using (var p = Round(new RectangleF(r.X + width / 2, r.Y + width / 2, r.Width - width, r.Height - width), radius))
            using (var pen = new Pen(c, width)) g.DrawPath(pen, p);
        }

        public static void Gradient(Graphics g, RectangleF r, float radius, Color a, Color b, float angle)
        {
            if (r.Width < 1 || r.Height < 1) return;
            using (var p = Round(r, radius))
            using (var br = new LinearGradientBrush(new RectangleF(r.X - 1, r.Y - 1, r.Width + 2, r.Height + 2), a, b, angle)) g.FillPath(br, p);
        }

        public static void GradientStroke(Graphics g, RectangleF r, float radius, Color a, Color b, float width)
        {
            if (r.Width < 2 || r.Height < 2) return;
            using (var p = Round(new RectangleF(r.X + width / 2, r.Y + width / 2, r.Width - width, r.Height - width), radius))
            using (var br = new LinearGradientBrush(new RectangleF(r.X - 1, r.Y - 1, r.Width + 2, r.Height + 2), a, b, 0f))
            using (var pen = new Pen(br, width)) g.DrawPath(pen, p);
        }

        /// <summary>A soft drop shadow under a rounded rectangle.</summary>
        public static void Shadow(Graphics g, RectangleF r, float radius, Theme t)
        {
            int layers = 5;
            for (int i = layers; i >= 1; i--)
            {
                float grow = Dpi.S(i * 1.6f);
                var rr = new RectangleF(r.X - grow / 2, r.Y - grow / 3 + Dpi.S(2f), r.Width + grow, r.Height + grow);
                Fill(g, rr, radius + grow, Color.FromArgb(t.Dark ? 9 : 5, 0, 0, 0));
            }
        }

        public static void Text(Graphics g, string s, Font f, Color c, Rectangle r, TextFormatFlags flags)
        {
            if (string.IsNullOrEmpty(s) || r.Width <= 0 || r.Height <= 0) return;
            TextRenderer.DrawText(g, s, f, r, c, flags);
        }

        public static void Text(Graphics g, string s, Font f, Color c, Rectangle r)
        {
            Text(g, s, f, c, r, Left);
        }

        public static Size Measure(string s, Font f)
        {
            if (string.IsNullOrEmpty(s)) return new Size(0, f.Height);
            return TextRenderer.MeasureText(s, f, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        }

        public static int MeasureHeight(string s, Font f, int width)
        {
            if (string.IsNullOrEmpty(s) || width <= 0) return 0;
            return TextRenderer.MeasureText(s, f, new Size(width, int.MaxValue), Wrap).Height;
        }

        /// <summary>A rounded label, such as a severity badge. Returns its width.</summary>
        public static int Pill(Graphics g, string text, Font f, Color fill, Color ink, int x, int centreY, int height)
        {
            var sz = Measure(text, f);
            int w = sz.Width + Dpi.S(16);
            var r = new Rectangle(x, centreY - height / 2, w, height);
            Smooth(g);
            Fill(g, r, height / 2f, fill);
            Text(g, text, f, ink, r, Center);
            return w;
        }

        /// <summary>A pill with a coloured dot, tinted from the colour.</summary>
        public static int Tag(Graphics g, string text, Color c, Theme t, int x, int centreY)
        {
            var f = Fonts.SmallSemi;
            var sz = Measure(text, f);
            int h = Dpi.S(22), dot = Dpi.S(6);
            int w = sz.Width + Dpi.S(26);
            var r = new Rectangle(x, centreY - h / 2, w, h);
            Smooth(g);
            Fill(g, r, h / 2f, t.Tint(c, t.Dark ? 0.16 : 0.12));
            using (var b = new SolidBrush(c)) g.FillEllipse(b, x + Dpi.S(9), centreY - dot / 2, dot, dot);
            Text(g, text, f, t.Dark ? Theme.Mix(c, Color.White, 0.25) : Theme.Mix(c, Color.Black, 0.2), new Rectangle(x + Dpi.S(19), r.Y, sz.Width + Dpi.S(4), h), Left);
            return w;
        }

        public static int TagWidth(string text)
        {
            return Measure(text, Fonts.SmallSemi).Width + Dpi.S(26);
        }

        /// <summary>A progress ring. <paramref name="value"/> runs from 0 to 1.</summary>
        public static void Ring(Graphics g, RectangleF r, float thickness, double value, Color track, Color a, Color b)
        {
            Smooth(g);
            var rr = new RectangleF(r.X + thickness / 2, r.Y + thickness / 2, r.Width - thickness, r.Height - thickness);
            if (rr.Width < 2) return;
            using (var pen = new Pen(track, thickness)) g.DrawEllipse(pen, rr);
            float sweep = (float)(360 * Math.Max(0, Math.Min(1, value)));
            if (sweep < 0.5f) return;
            using (var br = new LinearGradientBrush(new RectangleF(r.X - 1, r.Y - 1, r.Width + 2, r.Height + 2), a, b, 45f))
            using (var pen = new Pen(br, thickness))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                g.DrawArc(pen, rr, -90, sweep);
            }
        }

        public static void Sparkline(Graphics g, Rectangle r, IList<double> values, double min, double max, Color c)
        {
            if (values == null || values.Count == 0 || r.Width < 4) return;
            Smooth(g);
            var pts = new PointF[values.Count];
            for (int i = 0; i < values.Count; i++)
            {
                float x = values.Count == 1 ? r.X + r.Width / 2f : r.X + i * (r.Width - 1f) / (values.Count - 1);
                double v = max > min ? (values[i] - min) / (max - min) : 0.5;
                pts[i] = new PointF(x, (float)(r.Bottom - 2 - v * (r.Height - 4)));
            }
            if (pts.Length > 1)
            {
                using (var path = new GraphicsPath())
                {
                    path.AddLines(pts);
                    path.AddLine(pts[pts.Length - 1].X, r.Bottom, pts[0].X, r.Bottom);
                    path.CloseFigure();
                    using (var br = new LinearGradientBrush(new Rectangle(r.X, r.Y - 1, r.Width, r.Height + 2), Theme.Alpha(c, 38), Theme.Alpha(c, 0), 90f)) g.FillPath(br, path);
                }
                using (var pen = new Pen(c, Dpi.S(1.6f)) { LineJoin = LineJoin.Round }) g.DrawLines(pen, pts);
            }
            var last = pts[pts.Length - 1];
            float d = Dpi.S(5f);
            using (var b = new SolidBrush(c)) g.FillEllipse(b, last.X - d / 2, last.Y - d / 2, d, d);
        }

        /// <summary>A horizontal bar split by severity counts.</summary>
        public static void SeverityBar(Graphics g, Rectangle r, int[] counts, Theme t)
        {
            int total = 0;
            foreach (var n in counts) total += n;
            Smooth(g);
            Fill(g, r, r.Height / 2f, t.LineSoft);
            if (total == 0) return;
            using (var clip = Round(r, r.Height / 2f))
            {
                var old = g.Clip;
                g.SetClip(clip, CombineMode.Intersect);
                float x = r.X;
                for (int i = 0; i < counts.Length; i++)
                {
                    if (counts[i] == 0) continue;
                    float w = r.Width * counts[i] / (float)total;
                    using (var b = new SolidBrush(t.For((DriftWatch.Core.Severity)i))) g.FillRectangle(b, x, r.Y, w + 0.5f, r.Height);
                    x += w;
                }
                g.Clip = old;
            }
        }

        /// <summary>The DriftWatch mark: a drift line across a dark tile with an amber point.</summary>
        public static void Logo(Graphics g, RectangleF r, Theme t)
        {
            Smooth(g);
            float rad = r.Width * 0.26f;
            Gradient(g, r, rad, Theme.Hex("#0B1A33"), Theme.Hex("#142A52"), 45f);
            using (var p = Round(r, rad))
            using (var pen = new Pen(Theme.Alpha(t.Accent, 90), Math.Max(1f, r.Width / 28f))) g.DrawPath(pen, p);
            float w = r.Width, h = r.Height;
            var pts = new[]
            {
                new PointF(r.X + w * 0.16f, r.Y + h * 0.60f), new PointF(r.X + w * 0.33f, r.Y + h * 0.42f),
                new PointF(r.X + w * 0.50f, r.Y + h * 0.60f), new PointF(r.X + w * 0.67f, r.Y + h * 0.40f)
            };
            using (var br = new LinearGradientBrush(r, Theme.Hex("#22D3EE"), Theme.Hex("#8B7CF6"), 0f))
            using (var pen = new Pen(br, w * 0.1f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                g.DrawCurve(pen, pts, 0.5f);
            float d = w * 0.17f;
            using (var b = new SolidBrush(Theme.Hex("#FBBF24"))) g.FillEllipse(b, r.X + w * 0.80f - d / 2, r.Y + h * 0.33f - d / 2, d, d);
        }
    }
}
