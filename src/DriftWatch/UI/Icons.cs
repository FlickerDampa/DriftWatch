using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace DriftWatch.UI
{
    /// <summary>Line icons drawn on a 24-unit grid, so they stay sharp at any size and scale.</summary>
    public static class Icons
    {
        public static void Draw(Graphics g, string name, RectangleF r, Color c)
        {
            Draw(g, name, r, c, 1.8f);
        }

        public static void Draw(Graphics g, string name, RectangleF r, Color c, float strokeUnits)
        {
            if (string.IsNullOrEmpty(name) || r.Width < 4) return;
            UI.Draw.Smooth(g);
            var state = g.Save();
            float k = Math.Min(r.Width, r.Height) / 24f;
            g.TranslateTransform(r.X + (r.Width - 24 * k) / 2, r.Y + (r.Height - 24 * k) / 2);
            g.ScaleTransform(k, k);
            using (var pen = new Pen(c, strokeUnits) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            using (var brush = new SolidBrush(c))
            {
                Paint(g, name, pen, brush);
            }
            g.Restore(state);
        }

        private static void Rect(Graphics g, Pen p, float x, float y, float w, float h, float rad)
        {
            using (var path = UI.Draw.Round(new RectangleF(x, y, w, h), rad)) g.DrawPath(p, path);
        }

        private static void Lines(Graphics g, Pen p, params float[] xy)
        {
            var pts = new PointF[xy.Length / 2];
            for (int i = 0; i < pts.Length; i++) pts[i] = new PointF(xy[i * 2], xy[i * 2 + 1]);
            g.DrawLines(p, pts);
        }

        private static void Circle(Graphics g, Pen p, float cx, float cy, float rad)
        {
            g.DrawEllipse(p, cx - rad, cy - rad, rad * 2, rad * 2);
        }

        private static void Dot(Graphics g, Brush b, float cx, float cy, float rad)
        {
            g.FillEllipse(b, cx - rad, cy - rad, rad * 2, rad * 2);
        }

        private static void Paint(Graphics g, string name, Pen p, Brush b)
        {
            switch (name)
            {
                case "dashboard":
                    Rect(g, p, 3, 3, 7.5f, 9, 2); Rect(g, p, 13.5f, 3, 7.5f, 5, 2); Rect(g, p, 13.5f, 11, 7.5f, 10, 2); Rect(g, p, 3, 15, 7.5f, 6, 2);
                    break;
                case "devices":
                case "desktop":
                    Rect(g, p, 2.5f, 3.5f, 19, 13, 2); Lines(g, p, 8, 20.5f, 16, 20.5f); Lines(g, p, 12, 16.5f, 12, 20.5f);
                    break;
                case "laptop":
                    Rect(g, p, 4.5f, 5, 15, 10.5f, 1.5f); Lines(g, p, 2, 19, 22, 19);
                    break;
                case "compare":
                    Lines(g, p, 4, 8, 19, 8); Lines(g, p, 15.5f, 4.5f, 19.5f, 8, 15.5f, 11.5f);
                    Lines(g, p, 20, 16, 5, 16); Lines(g, p, 8.5f, 12.5f, 4.5f, 16, 8.5f, 19.5f);
                    break;
                case "shield":
                case "shield-check":
                    using (var path = new GraphicsPath())
                    {
                        path.AddLine(12, 2.5f, 19.5f, 5.5f);
                        path.AddLine(19.5f, 5.5f, 19.5f, 11);
                        path.AddBezier(19.5f, 11, 19.5f, 16, 16, 19.5f, 12, 21.5f);
                        path.AddBezier(12, 21.5f, 8, 19.5f, 4.5f, 16, 4.5f, 11);
                        path.AddLine(4.5f, 11, 4.5f, 5.5f);
                        path.CloseFigure();
                        g.DrawPath(p, path);
                    }
                    if (name == "shield-check") Lines(g, p, 8.8f, 12, 11, 14.2f, 15.4f, 9.8f);
                    break;
                case "package":
                    using (var path = new GraphicsPath())
                    {
                        path.AddPolygon(new[] { new PointF(12, 2.5f), new PointF(20.5f, 7), new PointF(20.5f, 17), new PointF(12, 21.5f), new PointF(3.5f, 17), new PointF(3.5f, 7) });
                        g.DrawPath(p, path);
                    }
                    Lines(g, p, 3.8f, 7.2f, 12, 11.8f, 20.2f, 7.2f); Lines(g, p, 12, 11.8f, 12, 21.2f); Lines(g, p, 7.8f, 4.8f, 16.2f, 9.5f);
                    break;
                case "policy":
                    Lines(g, p, 14, 2.5f, 6, 2.5f, 6, 2.5f); Rect(g, p, 4.5f, 2.5f, 15, 19, 2);
                    Lines(g, p, 8.5f, 8, 15.5f, 8); Lines(g, p, 8.5f, 12, 15.5f, 12); Lines(g, p, 8.5f, 16, 12, 16);
                    break;
                case "settings":
                    Lines(g, p, 4, 6, 20, 6); Lines(g, p, 4, 12, 20, 12); Lines(g, p, 4, 18, 20, 18);
                    FillRing(g, p, b, 9, 6); FillRing(g, p, b, 15.5f, 12); FillRing(g, p, b, 7.5f, 18);
                    break;
                case "folder":
                    using (var path = new GraphicsPath())
                    {
                        path.AddLine(3, 7, 3, 18);
                        path.AddArc(3, 17, 2, 2, 180, -90);
                        path.AddLine(4, 19.5f, 20, 19.5f);
                        path.AddLine(21, 18.5f, 21, 9.5f);
                        path.AddLine(20, 8.5f, 11.5f, 8.5f);
                        path.AddLine(11.5f, 8.5f, 9.5f, 5.5f);
                        path.AddLine(9.5f, 5.5f, 4.5f, 5.5f);
                        path.CloseFigure();
                        g.DrawPath(p, path);
                    }
                    break;
                case "file":
                case "file-plus":
                    using (var path = new GraphicsPath())
                    {
                        path.AddLine(14, 2.5f, 6, 2.5f);
                        path.AddLine(6, 2.5f, 6, 21.5f);
                        path.AddLine(6, 21.5f, 19, 21.5f);
                        path.AddLine(19, 21.5f, 19, 7.5f);
                        path.CloseFigure();
                        g.DrawPath(p, path);
                    }
                    Lines(g, p, 14, 2.5f, 14, 7.5f, 19, 7.5f);
                    if (name == "file-plus") { Lines(g, p, 12.5f, 11, 12.5f, 18); Lines(g, p, 9, 14.5f, 16, 14.5f); }
                    break;
                case "plus":
                    Lines(g, p, 12, 5, 12, 19); Lines(g, p, 5, 12, 19, 12);
                    break;
                case "search":
                    Circle(g, p, 10.5f, 10.5f, 6.5f); Lines(g, p, 15.5f, 15.5f, 20.5f, 20.5f);
                    break;
                case "download":
                    Lines(g, p, 12, 3.5f, 12, 15); Lines(g, p, 7, 10, 12, 15, 17, 10); Lines(g, p, 4, 20, 20, 20);
                    break;
                case "check":
                    Lines(g, p, 5, 12.5f, 10, 17.5f, 19, 7);
                    break;
                case "check-circle":
                    Circle(g, p, 12, 12, 9); Lines(g, p, 8, 12.3f, 10.8f, 15, 16, 9.5f);
                    break;
                case "x":
                    Lines(g, p, 6.5f, 6.5f, 17.5f, 17.5f); Lines(g, p, 17.5f, 6.5f, 6.5f, 17.5f);
                    break;
                case "x-circle":
                    Circle(g, p, 12, 12, 9); Lines(g, p, 9, 9, 15, 15); Lines(g, p, 15, 9, 9, 15);
                    break;
                case "alert":
                    using (var path = new GraphicsPath())
                    {
                        path.AddPolygon(new[] { new PointF(12, 3.5f), new PointF(21.5f, 20), new PointF(2.5f, 20) });
                        g.DrawPath(p, path);
                    }
                    Lines(g, p, 12, 9.5f, 12, 13.5f); Dot(g, b, 12, 16.7f, 1.1f);
                    break;
                case "info":
                    Circle(g, p, 12, 12, 9); Lines(g, p, 12, 11, 12, 16.5f); Dot(g, b, 12, 7.8f, 1.1f);
                    break;
                case "minus-circle":
                    Circle(g, p, 12, 12, 9); Lines(g, p, 8.5f, 12, 15.5f, 12);
                    break;
                case "clock":
                    Circle(g, p, 12, 12, 9); Lines(g, p, 12, 7, 12, 12, 15.5f, 14);
                    break;
                case "chevron-right":
                    Lines(g, p, 9.5f, 6, 15.5f, 12, 9.5f, 18);
                    break;
                case "chevron-left":
                    Lines(g, p, 14.5f, 6, 8.5f, 12, 14.5f, 18);
                    break;
                case "chevron-down":
                    Lines(g, p, 6, 9.5f, 12, 15.5f, 18, 9.5f);
                    break;
                case "copy":
                    Rect(g, p, 8.5f, 8.5f, 12, 12, 2); Lines(g, p, 15.5f, 8.5f, 15.5f, 5.5f); Rect(g, p, 3.5f, 3.5f, 12, 12, 2);
                    break;
                case "eye":
                    using (var path = new GraphicsPath())
                    {
                        path.AddBezier(2.5f, 12, 6, 5.5f, 18, 5.5f, 21.5f, 12);
                        path.AddBezier(21.5f, 12, 18, 18.5f, 6, 18.5f, 2.5f, 12);
                        g.DrawPath(p, path);
                    }
                    Circle(g, p, 12, 12, 3);
                    break;
                case "sun":
                    Circle(g, p, 12, 12, 4);
                    for (int i = 0; i < 8; i++)
                    {
                        double a = i * Math.PI / 4;
                        Lines(g, p, 12 + (float)Math.Cos(a) * 7, 12 + (float)Math.Sin(a) * 7, 12 + (float)Math.Cos(a) * 9.5f, 12 + (float)Math.Sin(a) * 9.5f);
                    }
                    break;
                case "moon":
                    using (var outer = new GraphicsPath())
                    using (var inner = new GraphicsPath())
                    {
                        outer.AddEllipse(3.5f, 3.5f, 17, 17);
                        inner.AddEllipse(9.5f, 0.5f, 15, 15);
                        using (var region = new Region(outer))
                        {
                            region.Exclude(inner);
                            g.FillRegion(b, region);
                        }
                    }
                    break;
                case "monitor-cog":
                case "system":
                    Rect(g, p, 2.5f, 3.5f, 19, 13, 2); Lines(g, p, 8, 20.5f, 16, 20.5f); Lines(g, p, 12, 16.5f, 12, 20.5f); Lines(g, p, 12, 3.5f, 12, 16.5f);
                    break;
                case "activity":
                    Lines(g, p, 2.5f, 12, 6.5f, 12, 9.5f, 4.5f, 14.5f, 19.5f, 17.5f, 12, 21.5f, 12);
                    break;
                case "timeline":
                    Lines(g, p, 6, 3, 6, 21); FillRing(g, p, b, 6, 6); FillRing(g, p, b, 6, 12); FillRing(g, p, b, 6, 18);
                    Lines(g, p, 11, 6, 20, 6); Lines(g, p, 11, 12, 17, 12); Lines(g, p, 11, 18, 19, 18);
                    break;
                case "external":
                    Lines(g, p, 13.5f, 3.5f, 20.5f, 3.5f, 20.5f, 10.5f); Lines(g, p, 20.5f, 3.5f, 11, 13);
                    Lines(g, p, 17.5f, 14, 17.5f, 20.5f, 3.5f, 20.5f, 3.5f, 6.5f, 10, 6.5f);
                    break;
                case "refresh":
                    g.DrawArc(p, 4, 4, 16, 16, -60, 290);
                    Lines(g, p, 15.5f, 3.2f, 19.2f, 5.4f, 17.6f, 9.4f);
                    break;
                case "filter":
                    using (var path = new GraphicsPath())
                    {
                        path.AddPolygon(new[] { new PointF(3.5f, 4.5f), new PointF(20.5f, 4.5f), new PointF(14, 12.5f), new PointF(14, 19.5f), new PointF(10, 17.5f), new PointF(10, 12.5f) });
                        g.DrawPath(p, path);
                    }
                    break;
                case "user":
                    Circle(g, p, 12, 8, 4);
                    g.DrawArc(p, 4.5f, 14, 15, 13, 200, 140);
                    break;
                case "bell":
                    using (var path = new GraphicsPath())
                    {
                        path.AddBezier(6, 16.5f, 6, 10, 6, 5, 12, 5);
                        path.AddBezier(12, 5, 18, 5, 18, 10, 18, 16.5f);
                        path.AddLine(18, 16.5f, 20, 18.5f);
                        path.AddLine(20, 18.5f, 4, 18.5f);
                        path.AddLine(4, 18.5f, 6, 16.5f);
                        g.DrawPath(p, path);
                    }
                    Lines(g, p, 10, 21.2f, 14, 21.2f); Lines(g, p, 12, 3, 12, 5);
                    break;
                case "cpu":
                    Rect(g, p, 6, 6, 12, 12, 2); Rect(g, p, 9.5f, 9.5f, 5, 5, 1);
                    foreach (float v in new[] { 9.5f, 14.5f })
                    {
                        Lines(g, p, v, 3, v, 6); Lines(g, p, v, 18, v, 21); Lines(g, p, 3, v, 6, v); Lines(g, p, 18, v, 21, v);
                    }
                    break;
                case "lock":
                    Rect(g, p, 5, 10.5f, 14, 10, 2); g.DrawArc(p, 8, 4, 8, 10, 180, 180); Lines(g, p, 8, 9, 8, 10.5f); Lines(g, p, 16, 9, 16, 10.5f);
                    break;
                case "globe":
                    Circle(g, p, 12, 12, 9); g.DrawEllipse(p, 8, 3, 8, 18); Lines(g, p, 3, 12, 21, 12);
                    break;
                case "keyboard":
                    Rect(g, p, 2.5f, 6, 19, 12, 2); Lines(g, p, 7, 14.5f, 17, 14.5f);
                    Dot(g, b, 7, 10, 0.9f); Dot(g, b, 10.3f, 10, 0.9f); Dot(g, b, 13.6f, 10, 0.9f); Dot(g, b, 17, 10, 0.9f);
                    break;
                case "sparkle":
                    using (var path = new GraphicsPath())
                    {
                        path.AddBezier(12, 3, 12.8f, 9, 15, 11.2f, 21, 12);
                        path.AddBezier(21, 12, 15, 12.8f, 12.8f, 15, 12, 21);
                        path.AddBezier(12, 21, 11.2f, 15, 9, 12.8f, 3, 12);
                        path.AddBezier(3, 12, 9, 11.2f, 11.2f, 9, 12, 3);
                        g.DrawPath(p, path);
                    }
                    break;
                case "upload":
                    Lines(g, p, 12, 15, 12, 3.5f); Lines(g, p, 7, 8.5f, 12, 3.5f, 17, 8.5f); Lines(g, p, 4, 20, 20, 20);
                    break;
                case "arrow-up":
                    Lines(g, p, 12, 19, 12, 5); Lines(g, p, 6, 11, 12, 5, 18, 11);
                    break;
                case "arrow-down":
                    Lines(g, p, 12, 5, 12, 19); Lines(g, p, 6, 13, 12, 19, 18, 13);
                    break;
                case "dot":
                    Dot(g, b, 12, 12, 4);
                    break;
                default:
                    Circle(g, p, 12, 12, 8);
                    break;
            }
        }

        private static void FillRing(Graphics g, Pen p, Brush b, float cx, float cy)
        {
            g.FillEllipse(b, cx - 2.4f, cy - 2.4f, 4.8f, 4.8f);
        }
    }
}
