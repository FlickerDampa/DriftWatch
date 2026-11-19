using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace DriftWatch.UI
{
    /// <summary>Base for every custom-drawn control: double-buffered, themed, and aware of the colour behind it.</summary>
    public class Surface : Panel
    {
        public Surface()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            DoubleBuffered = true;
            Theme.Changed += HandleTheme;
        }

        protected Theme T { get { return Theme.Current; } }

        /// <summary>The colour this control's children sit on.</summary>
        public virtual Color InnerColor { get { return Behind; } }

        /// <summary>The colour behind this control (its parent's inner colour).</summary>
        protected Color Behind
        {
            get
            {
                var s = Parent as Surface;
                return s != null ? s.InnerColor : T.Bg;
            }
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(Behind);
        }

        private void HandleTheme(object sender, EventArgs e)
        {
            OnThemeChanged();
            Invalidate();
        }

        protected virtual void OnThemeChanged() { }

        protected override void Dispose(bool disposing)
        {
            if (disposing) Theme.Changed -= HandleTheme;
            base.Dispose(disposing);
        }
    }

    /// <summary>A rounded panel with an optional header (title, subtitle and controls on the right).</summary>
    public class Card : Surface
    {
        private string title = "", subtitle = "";
        private readonly List<Control> headerControls = new List<Control>();

        private Padding content = Dpi.P(18, 16, 18, 16);

        public Card()
        {
            Padding = content;
        }

        /// <summary>Space around the content. Top is measured from below the header when there is a title.</summary>
        public Padding ContentPadding
        {
            get { return content; }
            set { content = value; UpdatePaddingIfNeeded(); PerformLayout(); }
        }

        public string Title { get { return title; } set { title = value ?? ""; UpdatePadding(); Invalidate(); } }
        public string Subtitle { get { return subtitle; } set { subtitle = value ?? ""; Invalidate(); } }
        public string Icon { get; set; }
        /// <summary>Optional accent shown as a glowing line along the top edge.</summary>
        public Color? Accent { get; set; }
        public bool Flat { get; set; }

        public override Color InnerColor { get { return T.Card; } }

        public int HeaderHeight { get { return title.Length > 0 ? Dpi.S(subtitle.Length > 0 ? 62 : 52) : 0; } }

        public void AddHeaderControl(Control c)
        {
            headerControls.Add(c);
            Controls.Add(c);
            PerformLayout();
        }

        private void UpdatePadding()
        {
            UpdatePaddingIfNeeded();
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            UpdatePaddingIfNeeded();
            base.OnLayout(e);
            int x = Width - Dpi.S(16);
            int headerMid = title.Length > 0 ? Dpi.S(subtitle.Length > 0 ? 30 : 26) : Dpi.S(26);
            for (int i = headerControls.Count - 1; i >= 0; i--)
            {
                var c = headerControls[i];
                if (!c.Visible) continue;
                x -= c.Width;
                c.Location = new Point(x, headerMid - c.Height / 2);
                x -= Dpi.S(8);
            }
        }

        private void UpdatePaddingIfNeeded()
        {
            int top = title.Length > 0 ? HeaderHeight + (content.Top == Dpi.S(16) ? 0 : content.Top) : content.Top;
            var want = new Padding(content.Left, top, content.Right, content.Bottom);
            if (Padding != want) Padding = want;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Draw.Smooth(g);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            float rad = Dpi.S(12f);
            Draw.Fill(g, r, rad, T.Card);
            if (!Flat) Draw.Stroke(g, r, rad, T.Line, 1f);
            if (Accent.HasValue)
            {
                var a = Accent.Value;
                using (var br = new LinearGradientBrush(new RectangleF(0, 0, Width, 4), Theme.Alpha(a, 0), a, 0f))
                {
                    var blend = new ColorBlend(3) { Colors = new[] { Theme.Alpha(a, 0), a, Theme.Alpha(a, 0) }, Positions = new[] { 0f, 0.5f, 1f } };
                    br.InterpolationColors = blend;
                    using (var pen = new Pen(br, Dpi.S(1.5f))) g.DrawLine(pen, rad, 1, Width - rad, 1);
                }
            }
            if (title.Length > 0)
            {
                int x = Dpi.S(18);
                if (!string.IsNullOrEmpty(Icon))
                {
                    int s = Dpi.S(18);
                    Icons.Draw(g, Icon, new RectangleF(x, Dpi.S(subtitle.Length > 0 ? 21 : 17), s, s), T.Accent);
                    x += s + Dpi.S(10);
                }
                Draw.Text(g, title, Fonts.H3, T.Ink, new Rectangle(x, Dpi.S(subtitle.Length > 0 ? 14 : 14), Width - x - Dpi.S(16), Dpi.S(24)));
                if (subtitle.Length > 0)
                    Draw.Text(g, subtitle, Fonts.Small, T.Muted, new Rectangle(x, Dpi.S(37), Width - x - Dpi.S(16) - HeaderControlsWidth(), Dpi.S(18)));
            }
        }

        private int HeaderControlsWidth()
        {
            int w = 0;
            foreach (var c in headerControls) if (c.Visible) w += c.Width + Dpi.S(8);
            return w;
        }
    }

    public enum ButtonStyle { Primary, Secondary, Ghost, Danger }

    /// <summary>A rounded button with an optional icon, keyboard support and a focus ring.</summary>
    public class Btn : Surface
    {
        private bool hover, down;
        private string icon;

        public Btn(string text, string icon, ButtonStyle style)
        {
            Text = text;
            this.icon = icon;
            Style = style;
            SetStyle(ControlStyles.Selectable, true);
            TabStop = true;
            Cursor = Cursors.Hand;
            Height = Dpi.S(36);
            Width = PreferredWidth;
        }

        public ButtonStyle Style { get; set; }
        public string IconName { get { return icon; } set { icon = value; Invalidate(); } }
        public bool Compact { get; set; }
        public string Tip { get; set; }

        public int PreferredWidth
        {
            get
            {
                int w = Dpi.S(Compact ? 20 : 28) + Draw.Measure(Text, Fonts.Semi).Width;
                if (!string.IsNullOrEmpty(icon)) w += Dpi.S(string.IsNullOrEmpty(Text) ? 16 : 24);
                if (string.IsNullOrEmpty(Text)) w = Height;
                return w;
            }
        }

        public void FitWidth() { Width = PreferredWidth; }

        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { down = true; Focus(); Invalidate(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        protected override bool IsInputKey(Keys keyData)
        {
            return keyData == Keys.Enter || keyData == Keys.Space || base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) { OnClick(EventArgs.Empty); e.Handled = true; }
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Draw.Smooth(g);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            float rad = Dpi.S(9f);
            Color ink;
            switch (Style)
            {
                case ButtonStyle.Primary:
                    Color a = Theme.Hex("#0891B2"), b = Theme.Hex("#6D4AE8");
                    if (hover) { a = Theme.Mix(a, Color.White, 0.12); b = Theme.Mix(b, Color.White, 0.12); }
                    if (down) { a = Theme.Mix(a, Color.Black, 0.12); b = Theme.Mix(b, Color.Black, 0.12); }
                    if (!Enabled) { a = T.Line; b = T.Line; }
                    Draw.Gradient(g, r, rad, a, b, 0f);
                    if (Enabled && T.Dark)
                        using (var pen = new Pen(Color.FromArgb(40, 255, 255, 255), 1f)) g.DrawLine(pen, rad, 1.5f, Width - rad, 1.5f);
                    ink = Enabled ? Color.White : T.Muted;
                    break;
                case ButtonStyle.Danger:
                    Draw.Fill(g, r, rad, hover ? T.Tint(T.Bad, 0.22) : T.Tint(T.Bad, 0.12));
                    Draw.Stroke(g, r, rad, T.Tint(T.Bad, 0.45), 1f);
                    ink = T.Dark ? Theme.Mix(T.Bad, Color.White, 0.3) : T.Bad;
                    break;
                case ButtonStyle.Ghost:
                    if (hover || down) Draw.Fill(g, r, rad, down ? T.Line : T.CardHover);
                    ink = Enabled ? (hover ? T.Ink : T.Soft) : T.Faint;
                    break;
                default:
                    Draw.Fill(g, r, rad, down ? T.Line : hover ? T.CardHover : (Behind == T.Card ? T.Field : T.Card));
                    Draw.Stroke(g, r, rad, hover ? Theme.Mix(T.Line, T.Accent, 0.35) : T.Line, 1f);
                    ink = Enabled ? T.Ink : T.Faint;
                    break;
            }
            if (Focused && ShowFocusCues)
                Draw.Stroke(g, new RectangleF(-0.5f, -0.5f, Width, Height), rad + 1, Theme.Alpha(T.Accent, 200), Dpi.S(2f));

            int iconSize = Dpi.S(Compact ? 15 : 16);
            int textW = Draw.Measure(Text, Fonts.Semi).Width;
            bool hasIcon = !string.IsNullOrEmpty(icon), hasText = !string.IsNullOrEmpty(Text);
            int content = (hasIcon ? iconSize : 0) + (hasIcon && hasText ? Dpi.S(8) : 0) + (hasText ? textW : 0);
            int x = (Width - content) / 2;
            if (hasIcon)
            {
                Icons.Draw(g, icon, new RectangleF(x, (Height - iconSize) / 2f, iconSize, iconSize), ink, 2f);
                x += iconSize + Dpi.S(8);
            }
            if (hasText) Draw.Text(g, Text, Fonts.Semi, ink, new Rectangle(x, 0, textW + Dpi.S(2), Height), Draw.Left);
        }
    }

    public enum Tone { Ink, Soft, Muted, Faint, Accent }

    /// <summary>A themed text label.</summary>
    public class Lbl : Surface
    {
        private Color? custom;

        public Lbl(string text, Font font, Tone tone)
        {
            Text = text;
            Font = font;
            Tone = tone;
            Height = font.Height + Dpi.S(4);
            SetStyle(ControlStyles.Selectable, false);
        }

        public Tone Tone { get; set; }
        public bool WrapText { get; set; }
        public TextFormatFlags Align = Draw.Left;
        public Color? CustomColor { get { return custom; } set { custom = value; Invalidate(); } }

        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }

        public Color Ink
        {
            get
            {
                if (custom.HasValue) return custom.Value;
                switch (Tone)
                {
                    case Tone.Soft: return T.Soft;
                    case Tone.Muted: return T.Muted;
                    case Tone.Faint: return T.Faint;
                    case Tone.Accent: return T.Accent;
                    default: return T.Ink;
                }
            }
        }

        public int HeightFor(int width)
        {
            return WrapText ? Draw.MeasureHeight(Text, Font, width) + Dpi.S(2) : Font.Height + Dpi.S(4);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Draw.Text(e.Graphics, Text, Font, Ink, ClientRectangle, WrapText ? Draw.Wrap : Align);
        }
    }

    /// <summary>A search field with an icon, a placeholder and a clear button.</summary>
    public class SearchBox : Surface
    {
        private readonly TextBox box;
        private bool hover;

        public SearchBox(string placeholder)
        {
            Placeholder = placeholder;
            Cursor = Cursors.IBeam;
            box = new TextBox { BorderStyle = BorderStyle.None, Font = Fonts.Body, Visible = false };
            box.TextChanged += delegate { Invalidate(); var h = TextChanged2; if (h != null) h(this, EventArgs.Empty); };
            box.LostFocus += delegate { if (box.Text.Length == 0) box.Visible = false; Invalidate(); };
            box.GotFocus += delegate { Invalidate(); };
            box.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Escape) { box.Text = ""; e.Handled = true; e.SuppressKeyPress = true; }
                if (e.KeyCode == Keys.Down || e.KeyCode == Keys.Enter) { var h = Submitted; if (h != null) h(this, e); }
            };
            Controls.Add(box);
            Height = Dpi.S(36);
            OnThemeChanged();
        }

        public string Placeholder { get; set; }
        public string Hint { get; set; }
        /// <summary>When set, clicking opens something else (the command palette) instead of typing.</summary>
        public bool Launcher { get; set; }
        public event EventHandler TextChanged2;
        public event KeyEventHandler Submitted;
        public event EventHandler Launched;

        public string Value
        {
            get { return box.Text; }
            set { box.Text = value ?? ""; box.Visible = box.Text.Length > 0 || box.Focused; Invalidate(); }
        }

        public TextBox Input { get { return box; } }

        public void FocusInput()
        {
            if (Launcher) { var h = Launched; if (h != null) h(this, EventArgs.Empty); return; }
            box.Visible = true;
            box.Focus();
            box.SelectAll();
        }

        protected override void OnThemeChanged()
        {
            box.BackColor = Behind == T.Card ? T.Field : T.Card;
            box.ForeColor = T.Ink;
        }

        protected override void OnParentChanged(EventArgs e) { base.OnParentChanged(e); OnThemeChanged(); }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (box.Text.Length > 0 && e.X > Width - Dpi.S(32)) { box.Text = ""; box.Visible = false; Invalidate(); return; }
            FocusInput();
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            int left = Dpi.S(36), right = Dpi.S(34);
            box.SetBounds(left, (Height - box.PreferredHeight) / 2 + 1, Math.Max(10, Width - left - right), box.PreferredHeight);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Draw.Smooth(g);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            float rad = Dpi.S(9f);
            Color fill = Behind == T.Card ? T.Field : T.Card;
            if (box.BackColor != fill) box.BackColor = fill;
            Draw.Fill(g, r, rad, fill);
            bool focus = box.Focused;
            Draw.Stroke(g, r, rad, focus ? T.Accent : hover ? Theme.Mix(T.Line, T.Accent, 0.3) : T.Line, focus ? Dpi.S(1.5f) : 1f);
            int s = Dpi.S(16);
            Icons.Draw(g, "search", new RectangleF(Dpi.S(12), (Height - s) / 2f, s, s), focus ? T.Accent : T.Muted, 2f);
            if (box.Text.Length == 0 && !box.Visible)
                Draw.Text(g, Placeholder, Fonts.Body, T.Faint, new Rectangle(Dpi.S(36), 0, Width - Dpi.S(36) - Dpi.S(70), Height));
            if (box.Text.Length > 0)
            {
                int xs = Dpi.S(12);
                Icons.Draw(g, "x", new RectangleF(Width - Dpi.S(23), (Height - xs) / 2f, xs, xs), T.Muted, 2.2f);
            }
            else if (!string.IsNullOrEmpty(Hint) && !box.Visible)
            {
                var hs = Draw.Measure(Hint, Fonts.Tiny);
                var hr = new Rectangle(Width - hs.Width - Dpi.S(22), (Height - Dpi.S(20)) / 2, hs.Width + Dpi.S(12), Dpi.S(20));
                Draw.Fill(g, hr, Dpi.S(5), T.Dark ? T.LineSoft : T.Bg);
                Draw.Stroke(g, hr, Dpi.S(5), T.Line, 1f);
                Draw.Text(g, Hint, Fonts.Tiny, T.Muted, hr, Draw.Center);
            }
        }
    }

    public sealed class ChipItem
    {
        public ChipItem(string key, string text) { Key = key; Text = text; Count = -1; }
        public string Key;
        public string Text;
        public int Count;
        public Color? Dot;
    }

    /// <summary>A row of filter chips. One is selected at a time.</summary>
    public class ChipBar : Surface
    {
        private readonly List<ChipItem> items = new List<ChipItem>();
        private readonly List<Rectangle> rects = new List<Rectangle>();
        private int selected, hover = -1, offset;

        public ChipBar()
        {
            Height = Dpi.S(32);
            Cursor = Cursors.Hand;
        }

        public event EventHandler SelectionChanged;
        public List<ChipItem> Items { get { return items; } }

        public string SelectedKey
        {
            get { return selected >= 0 && selected < items.Count ? items[selected].Key : null; }
            set
            {
                int i = items.FindIndex(x => x.Key == value);
                if (i < 0) i = 0;
                if (i != selected) { selected = i; Invalidate(); Raise(); }
            }
        }

        public void SetItems(IEnumerable<ChipItem> list)
        {
            string key = SelectedKey;
            items.Clear();
            items.AddRange(list);
            int i = items.FindIndex(x => x.Key == key);
            selected = i < 0 ? 0 : i;
            Measure();
            Invalidate();
        }

        public void SetCount(string key, int count)
        {
            var it = items.FirstOrDefault(x => x.Key == key);
            if (it != null) { it.Count = count; Measure(); Invalidate(); }
        }

        public int PreferredWidth { get { Measure(); return rects.Count == 0 ? 0 : rects[rects.Count - 1].Right + 1; } }

        private void Raise()
        {
            var h = SelectionChanged;
            if (h != null) h(this, EventArgs.Empty);
        }

        private int ContentWidth { get { return rects.Count == 0 ? 0 : rects[rects.Count - 1].Right + offset; } }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            Measure();
            int max = Math.Max(0, ContentWidth - Width);
            offset = Math.Max(0, Math.Min(max, offset - e.Delta / 2));
            Measure();
            Invalidate();
            base.OnMouseWheel(e);
        }

        private void Measure()
        {
            rects.Clear();
            int x = -offset, h = Dpi.S(30);
            foreach (var it in items)
            {
                int w = Draw.Measure(it.Text, Fonts.SmallSemi).Width + Dpi.S(24);
                if (it.Count >= 0) w += Draw.Measure(it.Count.ToString(CultureInfo.InvariantCulture), Fonts.Tiny).Width + Dpi.S(16);
                if (it.Dot.HasValue) w += Dpi.S(12);
                rects.Add(new Rectangle(x, (Height - h) / 2, w, h));
                x += w + Dpi.S(6);
            }
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); offset = 0; Measure(); }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = rects.FindIndex(r => r.Contains(e.Location));
            if (h != hover) { hover = h; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            int i = rects.FindIndex(r => r.Contains(e.Location));
            if (i >= 0)
            {
                // Bring a partly hidden chip fully into view.
                if (rects[i].Right > Width) offset += rects[i].Right - Width + Dpi.S(24);
                else if (rects[i].X < 0) offset = Math.Max(0, offset + rects[i].X - Dpi.S(24));
                Measure();
                Invalidate();
            }
            if (i >= 0 && i != selected) { selected = i; Invalidate(); Raise(); }
            base.OnMouseDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            if (rects.Count != items.Count) Measure();
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                var r = rects[i];
                bool sel = i == selected;
                Draw.Smooth(g);
                Color fill = sel ? T.Tint(T.Accent, T.Dark ? 0.16 : 0.1) : i == hover ? T.CardHover : (Behind == T.Card ? T.Field : T.Card);
                Draw.Fill(g, r, r.Height / 2f, fill);
                Draw.Stroke(g, r, r.Height / 2f, sel ? Theme.Alpha(T.Accent, 160) : T.Line, 1f);
                int x = r.X + Dpi.S(12);
                if (it.Dot.HasValue)
                {
                    int d = Dpi.S(7);
                    using (var b = new SolidBrush(it.Dot.Value)) g.FillEllipse(b, x, r.Y + (r.Height - d) / 2, d, d);
                    x += Dpi.S(12);
                }
                var tw = Draw.Measure(it.Text, Fonts.SmallSemi).Width;
                Draw.Text(g, it.Text, Fonts.SmallSemi, sel ? (T.Dark ? T.Accent : Theme.Mix(T.Accent, Color.Black, 0.2)) : T.Soft, new Rectangle(x, r.Y, tw + 2, r.Height), Draw.Left);
                if (it.Count >= 0)
                {
                    string n = it.Count.ToString(CultureInfo.InvariantCulture);
                    var nw = Draw.Measure(n, Fonts.Tiny).Width + Dpi.S(10);
                    var nr = new Rectangle(x + tw + Dpi.S(6), r.Y + (r.Height - Dpi.S(18)) / 2, nw, Dpi.S(18));
                    Draw.Fill(g, nr, nr.Height / 2f, sel ? Theme.Alpha(T.Accent, T.Dark ? 60 : 40) : T.LineSoft);
                    Draw.Text(g, n, Fonts.Tiny, sel ? T.Ink : T.Muted, nr, Draw.Center);
                }
            }
            // Fade the edges when chips run past them, as a hint to scroll.
            int fw = Dpi.S(28);
            if (ContentWidth > Width && rects.Count > 0 && rects[rects.Count - 1].Right > Width)
                using (var br = new LinearGradientBrush(new Rectangle(Width - fw - 1, 0, fw + 2, Height), Theme.Alpha(Behind, 0), Behind, 0f)) g.FillRectangle(br, Width - fw, 0, fw, Height);
            if (offset > 0)
                using (var br = new LinearGradientBrush(new Rectangle(-1, 0, fw + 2, Height), Behind, Theme.Alpha(Behind, 0), 0f)) g.FillRectangle(br, 0, 0, fw, Height);
        }
    }

    /// <summary>Underlined tabs.</summary>
    public class Tabs : Surface
    {
        private readonly List<string> items = new List<string>();
        private readonly List<Rectangle> rects = new List<Rectangle>();
        private int selected, hover = -1;

        public Tabs(params string[] tabs)
        {
            items.AddRange(tabs);
            Height = Dpi.S(40);
            Cursor = Cursors.Hand;
        }

        public event EventHandler SelectionChanged;
        public int SelectedIndex
        {
            get { return selected; }
            set { if (value != selected && value >= 0 && value < items.Count) { selected = value; Invalidate(); var h = SelectionChanged; if (h != null) h(this, EventArgs.Empty); } }
        }
        public string SelectedText { get { return items[selected]; } }

        private void Measure()
        {
            rects.Clear();
            int x = 0;
            foreach (var t in items)
            {
                int w = Draw.Measure(t, Fonts.Semi).Width + Dpi.S(24);
                rects.Add(new Rectangle(x, 0, w, Height));
                x += w + Dpi.S(4);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            Measure();
            int h = rects.FindIndex(r => r.Contains(e.Location));
            if (h != hover) { hover = h; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Measure();
            int i = rects.FindIndex(r => r.Contains(e.Location));
            if (i >= 0) SelectedIndex = i;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Measure();
            using (var pen = new Pen(T.Line, 1f)) g.DrawLine(pen, 0, Height - 1, Width, Height - 1);
            for (int i = 0; i < items.Count; i++)
            {
                var r = rects[i];
                bool sel = i == selected;
                Draw.Text(g, items[i], Fonts.Semi, sel ? T.Ink : i == hover ? T.Soft : T.Muted, new Rectangle(r.X, 0, r.Width, Height - Dpi.S(2)), Draw.Center);
                if (sel)
                {
                    var bar = new RectangleF(r.X + Dpi.S(8), Height - Dpi.S(3), r.Width - Dpi.S(16), Dpi.S(3));
                    Draw.Smooth(g);
                    Draw.Gradient(g, bar, Dpi.S(1.5f), T.Accent, T.Accent2, 0f);
                }
            }
        }
    }

    /// <summary>An on/off switch with a label and an optional description.</summary>
    public class Toggle : Surface
    {
        private bool on, hover;

        public Toggle(string text, string description)
        {
            Text = text;
            Description = description ?? "";
            Height = Dpi.S(string.IsNullOrEmpty(description) ? 32 : 50);
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.Selectable, true);
            TabStop = true;
        }

        public string Description { get; set; }
        public event EventHandler CheckedChanged;
        public bool Checked
        {
            get { return on; }
            set { if (on != value) { on = value; Invalidate(); var h = CheckedChanged; if (h != null) h(this, EventArgs.Empty); } }
        }

        protected override void OnClick(EventArgs e) { Checked = !Checked; Focus(); base.OnClick(e); }
        protected override bool IsInputKey(Keys k) { return k == Keys.Space || base.IsInputKey(k); }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Space) Checked = !Checked; base.OnKeyDown(e); }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Draw.Smooth(g);
            int w = Dpi.S(38), h = Dpi.S(22);
            var track = new RectangleF(Width - w - 1, Dpi.S(Description.Length > 0 ? 6 : 5), w, h);
            if (on) Draw.Gradient(g, track, h / 2f, Theme.Hex("#0891B2"), Theme.Hex("#6D4AE8"), 0f);
            else { Draw.Fill(g, track, h / 2f, hover ? T.Line : T.LineSoft); Draw.Stroke(g, track, h / 2f, T.Line, 1f); }
            if (Focused && ShowFocusCues) Draw.Stroke(g, RectangleF.Inflate(track, 2, 2), h / 2f + 2, T.Accent, Dpi.S(1.5f));
            float d = h - Dpi.S(6);
            float kx = on ? track.Right - d - Dpi.S(3) : track.X + Dpi.S(3);
            using (var b = new SolidBrush(on ? Color.White : T.Muted)) g.FillEllipse(b, kx, track.Y + Dpi.S(3), d, d);
            Draw.Text(g, Text, Fonts.Semi, T.Ink, new Rectangle(0, Dpi.S(Description.Length > 0 ? 4 : 0), Width - w - Dpi.S(16), Dpi.S(Description.Length > 0 ? 22 : 32)));
            if (Description.Length > 0)
                Draw.Text(g, Description, Fonts.Small, T.Muted, new Rectangle(0, Dpi.S(26), Width - w - Dpi.S(16), Dpi.S(20)));
        }
    }

    /// <summary>A bordered text input. Numeric mode accepts digits only.</summary>
    public class TextField : Surface
    {
        private readonly TextBox box;

        public TextField(bool multiline, bool numeric)
        {
            Numeric = numeric;
            box = new TextBox { BorderStyle = BorderStyle.None, Font = multiline ? Fonts.Mono : Fonts.Body, Multiline = multiline, AcceptsReturn = multiline, ScrollBars = multiline && Native.IsWindows ? ScrollBars.Vertical : ScrollBars.None, WordWrap = false };
            if (numeric) box.KeyPress += (s, e) => { if (!char.IsDigit(e.KeyChar) && !char.IsControl(e.KeyChar)) e.Handled = true; };
            box.GotFocus += delegate { Invalidate(); };
            box.LostFocus += delegate { Invalidate(); };
            box.TextChanged += delegate { var h = ValueChanged; if (h != null) h(this, EventArgs.Empty); };
            box.HandleCreated += delegate { Native.DarkScrollbars(box, T); };
            Controls.Add(box);
            Height = multiline ? Dpi.S(120) : Dpi.S(36);
            Cursor = Cursors.IBeam;
            OnThemeChanged();
        }

        public bool Numeric { get; private set; }
        public event EventHandler ValueChanged;
        public TextBox Input { get { return box; } }
        public string Value { get { return box.Text; } set { box.Text = value ?? ""; } }

        public int Number(int fallback)
        {
            int n;
            return int.TryParse(box.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out n) ? n : fallback;
        }

        public List<string> Lines()
        {
            return box.Text.Replace("\r", "").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        protected override void OnThemeChanged()
        {
            box.BackColor = T.Field;
            box.ForeColor = T.Ink;
            Native.DarkScrollbars(box, T);
        }

        protected override void OnMouseDown(MouseEventArgs e) { box.Focus(); base.OnMouseDown(e); }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            int pad = Dpi.S(12);
            if (box.Multiline) box.SetBounds(pad, Dpi.S(10), Width - pad * 2 + Dpi.S(6), Height - Dpi.S(20));
            else box.SetBounds(pad, (Height - box.PreferredHeight) / 2 + 1, Width - pad * 2, box.PreferredHeight);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Draw.Smooth(g);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Draw.Fill(g, r, Dpi.S(8f), T.Field);
            Draw.Stroke(g, r, Dpi.S(8f), box.Focused ? T.Accent : T.Line, box.Focused ? Dpi.S(1.5f) : 1f);
        }
    }

    /// <summary>A headline number on its own card: icon, label, value and a note.</summary>
    public class StatTile : Surface
    {
        private bool hover;

        public StatTile(string icon, string label)
        {
            IconName = icon;
            Label = label;
            Value = "–";
            Note = "";
            Height = Dpi.S(132);
        }

        public string IconName { get; set; }
        public string Label { get; set; }
        public string Value { get; set; }
        public string Suffix { get; set; }
        public string Note { get; set; }
        public Color? Color { get; set; }
        public Color? NoteColor { get; set; }
        public IList<double> Spark { get; set; }
        /// <summary>When set (0-100), a small ring is drawn on the right.</summary>
        public int? Ring { get; set; }
        public bool Clickable { get { return Cursor == Cursors.Hand; } set { Cursor = value ? Cursors.Hand : Cursors.Default; } }

        protected override void OnMouseEnter(EventArgs e) { hover = Clickable; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Draw.Smooth(g);
            var c = Color ?? T.Accent;
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            float rad = Dpi.S(12f);
            Draw.Fill(g, r, rad, hover ? T.CardHover : T.Card);
            Draw.Stroke(g, r, rad, hover ? Theme.Mix(T.Line, c, 0.4) : T.Line, 1f);
            using (var path = new GraphicsPath())
            {
                float gs = Dpi.S(140f);
                path.AddEllipse(Width - gs * 0.6f, -gs * 0.55f, gs, gs);
                using (var br = new PathGradientBrush(path) { CenterColor = Theme.Alpha(c, T.Dark ? 34 : 22), SurroundColors = new[] { Theme.Alpha(c, 0) } })
                using (var clip = Draw.Round(r, rad))
                {
                    var old = g.Clip;
                    g.SetClip(clip, CombineMode.Intersect);
                    g.FillPath(br, path);
                    g.Clip = old;
                }
            }
            int pad = Dpi.S(18);
            int box = Dpi.S(34);
            var ib = new Rectangle(pad, pad, box, box);
            Draw.Fill(g, ib, Dpi.S(9), T.Tint(c, T.Dark ? 0.18 : 0.12));
            Icons.Draw(g, IconName, new RectangleF(ib.X + Dpi.S(8), ib.Y + Dpi.S(8), box - Dpi.S(16), box - Dpi.S(16)), c, 2f);
            Draw.Text(g, Label.ToUpperInvariant(), Fonts.Tiny, T.Muted, new Rectangle(pad + box + Dpi.S(12), pad, Width - pad * 2 - box, box));

            int vy = pad + box + Dpi.S(6);
            var vs = Draw.Measure(Value, Fonts.Number);
            Draw.Text(g, Value, Fonts.Number, T.Ink, new Rectangle(pad - Dpi.S(1), vy, vs.Width + Dpi.S(4), vs.Height));
            if (!string.IsNullOrEmpty(Suffix))
                Draw.Text(g, Suffix, Fonts.Body, T.Muted, new Rectangle(pad + vs.Width + Dpi.S(4), vy + vs.Height - Fonts.Body.Height - Dpi.S(4), Dpi.S(80), Fonts.Body.Height + 2));
            if (!string.IsNullOrEmpty(Note))
                Draw.Text(g, Note, Fonts.Small, NoteColor ?? T.Muted, new Rectangle(pad, Height - pad - Fonts.Small.Height + Dpi.S(2), Width - pad * 2, Fonts.Small.Height + 2));

            if (Ring.HasValue)
            {
                int rs = Dpi.S(46);
                var rr = new RectangleF(Width - pad - rs, vy + Dpi.S(2), rs, rs);
                Draw.Ring(g, rr, Dpi.S(5f), Ring.Value / 100.0, T.LineSoft, T.ForScore(Ring.Value), Theme.Mix(T.ForScore(Ring.Value), T.Accent2, 0.5));
                Draw.Text(g, PostureGrade(Ring.Value), Fonts.SmallSemi, T.Ink, Rectangle.Round(rr), Draw.Center);
            }
            else if (Spark != null && Spark.Count > 1)
            {
                var sr = new Rectangle(Width - pad - Dpi.S(90), vy + Dpi.S(8), Dpi.S(90), Dpi.S(30));
                double min = Spark.Min(), max = Spark.Max();
                if (max - min < 1) { min -= 1; max += 1; }
                Draw.Sparkline(g, sr, Spark, min, max, c);
            }
        }

        private static string PostureGrade(int score) { return DriftWatch.Core.PostureResult.GradeFor(score); }
    }

    /// <summary>A large score ring with an animated sweep.</summary>
    public class Gauge : Surface
    {
        private double shown, target;
        private readonly Timer timer = new Timer { Interval = 16 };

        public Gauge()
        {
            Size = Dpi.S(170, 170);
            Caption = "Security score";
            timer.Tick += delegate
            {
                shown += (target - shown) * 0.18;
                if (Math.Abs(target - shown) < 0.4) { shown = target; timer.Stop(); }
                Invalidate();
            };
        }

        public string Caption { get; set; }
        public int? Score { get; private set; }
        public float Thickness = 12f;

        public void SetScore(int? score, bool animate)
        {
            Score = score;
            target = score ?? 0;
            if (!animate || Environment.GetEnvironmentVariable("DRIFTWATCH_NO_ANIMATION") == "1") { shown = target; Invalidate(); return; }
            shown = 0;
            timer.Start();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) timer.Dispose();
            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            int size = Math.Min(Width, Height);
            var r = new RectangleF((Width - size) / 2f + Dpi.S(4), (Height - size) / 2f + Dpi.S(4), size - Dpi.S(8), size - Dpi.S(8));
            Color c = Score.HasValue ? T.ForScore(Score.Value) : T.Faint;
            Draw.Smooth(g);
            using (var pen = new Pen(T.LineSoft, Dpi.S(1f)))
            {
                float cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2, outer = r.Width / 2 + Dpi.S(1f), inner = outer - Dpi.S(3f);
                for (int i = 0; i < 40; i++)
                {
                    double a = i * Math.PI * 2 / 40;
                    g.DrawLine(pen, cx + (float)Math.Cos(a) * inner, cy + (float)Math.Sin(a) * inner, cx + (float)Math.Cos(a) * outer, cy + (float)Math.Sin(a) * outer);
                }
            }
            var ring = RectangleF.Inflate(r, -Dpi.S(8f), -Dpi.S(8f));
            Draw.Ring(g, ring, Dpi.S(Thickness), shown / 100.0, T.LineSoft, c, Theme.Mix(c, T.Accent2, 0.55));
            string value = Score.HasValue ? ((int)Math.Round(shown)).ToString(CultureInfo.InvariantCulture) : "–";
            var big = size > Dpi.S(150) ? Fonts.Big : Fonts.Number;
            int vh = big.Height;
            int top = (int)(ring.Y + ring.Height / 2 - vh / 2 - Dpi.S(8));
            Draw.Text(g, value, big, T.Ink, new Rectangle((int)ring.X, top, (int)ring.Width, vh), Draw.Center);
            string sub = Score.HasValue ? "Grade " + DriftWatch.Core.PostureResult.GradeFor(Score.Value) + (size > Dpi.S(160) ? " · " + DriftWatch.Core.PostureResult.Describe(Score.Value) : "") : Caption;
            Draw.Text(g, sub, Fonts.SmallSemi, Score.HasValue ? c : T.Muted, new Rectangle((int)ring.X, top + vh, (int)ring.Width, Dpi.S(18)), Draw.Center);
        }
    }

    public sealed class ChartPoint
    {
        public ChartPoint(DateTime x, double y, object tag) { X = x; Y = y; Tag = tag; }
        public DateTime X;
        public double Y;
        public object Tag;
    }

    public sealed class ChartSeries
    {
        public ChartSeries(string name, Color color, bool emphasis)
        {
            Name = name;
            Color = color;
            Emphasis = emphasis;
            Points = new List<ChartPoint>();
        }

        public string Name;
        public Color Color;
        public bool Emphasis;
        public List<ChartPoint> Points;
    }

    /// <summary>A time-based line chart (0 to 100) with an optional threshold and a hover read-out.</summary>
    public class LineChart : Surface
    {
        private readonly List<ChartSeries> series = new List<ChartSeries>();
        private ChartPoint hoverPoint;
        private ChartSeries hoverSeries;

        public LineChart()
        {
            YMin = 0;
            YMax = 100;
            EmptyText = "Not enough audits to draw a trend yet.";
        }

        public double YMin, YMax;
        public double? Threshold;
        public string ThresholdLabel = "";
        public string EmptyText;
        public string ValueSuffix = "";
        public event EventHandler<ChartPointEventArgs> PointClicked;

        public void SetSeries(IEnumerable<ChartSeries> list)
        {
            series.Clear();
            series.AddRange(list);
            hoverPoint = null;
            Invalidate();
        }

        private Rectangle Plot
        {
            get { return new Rectangle(Dpi.S(34), Dpi.S(10), Width - Dpi.S(34) - Dpi.S(12), Height - Dpi.S(10) - Dpi.S(28)); }
        }

        private void Range(out DateTime min, out DateTime max)
        {
            var all = series.SelectMany(s => s.Points).ToList();
            min = all.Count == 0 ? DateTime.Today.AddDays(-30) : all.Min(p => p.X);
            max = all.Count == 0 ? DateTime.Today : all.Max(p => p.X);
            if ((max - min).TotalDays < 1) { min = min.AddDays(-3); max = max.AddDays(3); }
            double pad = (max - min).TotalDays * 0.04;
            min = min.AddDays(-pad);
            max = max.AddDays(pad);
        }

        private PointF Map(ChartPoint p, Rectangle plot, DateTime min, DateTime max)
        {
            double fx = (p.X - min).TotalSeconds / Math.Max(1, (max - min).TotalSeconds);
            double fy = (p.Y - YMin) / Math.Max(0.0001, YMax - YMin);
            return new PointF((float)(plot.X + fx * plot.Width), (float)(plot.Bottom - fy * plot.Height));
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            DateTime min, max;
            Range(out min, out max);
            var plot = Plot;
            ChartPoint best = null;
            ChartSeries bestS = null;
            double bestD = Dpi.S(28);
            foreach (var s in series)
            {
                foreach (var p in s.Points)
                {
                    var pt = Map(p, plot, min, max);
                    double d = Math.Sqrt(Math.Pow(pt.X - e.X, 2) + Math.Pow((pt.Y - e.Y) * (s.Emphasis ? 0.4 : 1), 2));
                    if (d < bestD || (Math.Abs(d - bestD) < 0.5 && s.Emphasis)) { bestD = d; best = p; bestS = s; }
                }
            }
            if (best != hoverPoint) { hoverPoint = best; hoverSeries = bestS; Cursor = best != null && best.Tag != null ? Cursors.Hand : Cursors.Default; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e) { hoverPoint = null; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            var h = PointClicked;
            if (hoverPoint != null && h != null) h(this, new ChartPointEventArgs(hoverPoint, hoverSeries));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var plot = Plot;
            if (plot.Width < 20 || plot.Height < 20) return;
            for (int i = 0; i <= 4; i++)
            {
                double v = YMin + (YMax - YMin) * i / 4;
                int y = (int)(plot.Bottom - plot.Height * i / 4.0);
                using (var pen = new Pen(i == 0 ? T.Line : T.LineSoft, 1f) { DashStyle = i == 0 ? DashStyle.Solid : DashStyle.Dot }) g.DrawLine(pen, plot.X, y, plot.Right, y);
                Draw.Text(g, ((int)v).ToString(CultureInfo.InvariantCulture), Fonts.MonoSmall, T.Faint, new Rectangle(0, y - Dpi.S(8), plot.X - Dpi.S(8), Dpi.S(16)), Draw.Right);
            }
            var pts = series.SelectMany(s => s.Points).ToList();
            if (pts.Count < 1)
            {
                Draw.Text(g, EmptyText, Fonts.Body, T.Muted, plot, Draw.Center);
                return;
            }
            DateTime min, max;
            Range(out min, out max);
            int labels = Math.Max(2, Math.Min(6, plot.Width / Dpi.S(90)));
            for (int i = 0; i < labels; i++)
            {
                var d = min.AddSeconds((max - min).TotalSeconds * (i + 0.5) / labels);
                int x = (int)(plot.X + plot.Width * (i + 0.5) / labels);
                Draw.Text(g, d.ToString("d MMM", CultureInfo.InvariantCulture), Fonts.Small, T.Faint, new Rectangle(x - Dpi.S(40), plot.Bottom + Dpi.S(8), Dpi.S(80), Dpi.S(16)), Draw.Center);
            }
            if (Threshold.HasValue)
            {
                float ty = Map(new ChartPoint(min, Threshold.Value, null), plot, min, max).Y;
                using (var pen = new Pen(Theme.Alpha(T.Warn, 150), Dpi.S(1f)) { DashStyle = DashStyle.Dash }) g.DrawLine(pen, plot.X, ty, plot.Right, ty);
                if (ThresholdLabel.Length > 0)
                {
                    var tw = Draw.Measure(ThresholdLabel, Fonts.Tiny).Width + Dpi.S(10);
                    var tr = new Rectangle(plot.Right - tw, (int)ty - Dpi.S(18), tw, Dpi.S(16));
                    Draw.Text(g, ThresholdLabel, Fonts.Tiny, T.Warn, tr, Draw.Right);
                }
            }
            Draw.Smooth(g);
            foreach (var s in series.OrderBy(x => x.Emphasis ? 1 : 0))
            {
                var ordered = s.Points.OrderBy(p => p.X).ToList();
                if (ordered.Count == 0) continue;
                var mapped = ordered.Select(p => Map(p, plot, min, max)).ToArray();
                if (s.Emphasis && mapped.Length > 1)
                {
                    using (var path = new GraphicsPath())
                    {
                        path.AddLines(mapped);
                        path.AddLine(mapped[mapped.Length - 1].X, plot.Bottom, mapped[0].X, plot.Bottom);
                        path.CloseFigure();
                        using (var br = new LinearGradientBrush(new Rectangle(plot.X, plot.Y - 1, plot.Width, plot.Height + 2), Theme.Alpha(s.Color, T.Dark ? 70 : 50), Theme.Alpha(s.Color, 0), 90f)) g.FillPath(br, path);
                    }
                }
                if (mapped.Length > 1)
                {
                    Color lc = s.Emphasis ? s.Color : Theme.Alpha(s.Color, 120);
                    using (var pen = new Pen(lc, Dpi.S(s.Emphasis ? 2.4f : 1.4f)) { LineJoin = LineJoin.Round }) g.DrawLines(pen, mapped);
                }
                float dot = Dpi.S(s.Emphasis ? 8f : 5f);
                foreach (var m in mapped)
                {
                    using (var b = new SolidBrush(T.Card)) g.FillEllipse(b, m.X - dot / 2, m.Y - dot / 2, dot, dot);
                    using (var pen = new Pen(s.Emphasis ? s.Color : Theme.Alpha(s.Color, 150), Dpi.S(1.8f))) g.DrawEllipse(pen, m.X - dot / 2, m.Y - dot / 2, dot, dot);
                }
            }
            if (hoverPoint != null)
            {
                var m = Map(hoverPoint, plot, min, max);
                using (var pen = new Pen(Theme.Alpha(T.Accent, 90), 1f) { DashStyle = DashStyle.Dash }) g.DrawLine(pen, m.X, plot.Y, m.X, plot.Bottom);
                float dot = Dpi.S(10f);
                using (var b = new SolidBrush(hoverSeries != null ? hoverSeries.Color : T.Accent)) g.FillEllipse(b, m.X - dot / 2, m.Y - dot / 2, dot, dot);
                string l1 = (hoverSeries != null ? hoverSeries.Name : "") + "  " + Math.Round(hoverPoint.Y).ToString(CultureInfo.InvariantCulture) + ValueSuffix;
                string l2 = hoverPoint.X.ToString("ddd d MMM yyyy, HH:mm", CultureInfo.InvariantCulture);
                int tw = Math.Max(Draw.Measure(l1, Fonts.SmallSemi).Width, Draw.Measure(l2, Fonts.Small).Width) + Dpi.S(20);
                int th = Dpi.S(44);
                int tx = (int)Math.Min(Math.Max(plot.X, m.X - tw / 2), Width - tw - 2);
                int ty = (int)(m.Y - th - Dpi.S(12));
                if (ty < 0) ty = (int)m.Y + Dpi.S(12);
                var tr = new Rectangle(tx, ty, tw, th);
                Draw.Shadow(g, tr, Dpi.S(8), T);
                Draw.Fill(g, tr, Dpi.S(8), T.Dark ? T.CardHover : T.Card);
                Draw.Stroke(g, tr, Dpi.S(8), T.Line, 1f);
                Draw.Text(g, l1, Fonts.SmallSemi, T.Ink, new Rectangle(tx + Dpi.S(10), ty + Dpi.S(6), tw, Dpi.S(16)));
                Draw.Text(g, l2, Fonts.Small, T.Muted, new Rectangle(tx + Dpi.S(10), ty + Dpi.S(23), tw, Dpi.S(16)));
            }
        }
    }

    public sealed class ChartPointEventArgs : EventArgs
    {
        public ChartPointEventArgs(ChartPoint p, ChartSeries s) { Point = p; Series = s; }
        public ChartPoint Point { get; private set; }
        public ChartSeries Series { get; private set; }
    }

    public sealed class Segment
    {
        public Segment(string label, double value, Color color) { Label = label; Value = value; Color = color; }
        public string Label;
        public double Value;
        public Color Color;
    }

    /// <summary>A donut chart with a centre total and a legend.</summary>
    public class Donut : Surface
    {
        private readonly List<Segment> segments = new List<Segment>();

        public string CenterText = "";
        public string CenterCaption = "";
        public event EventHandler<SegmentEventArgs> SegmentClicked;
        private int hover = -1;
        private readonly List<Rectangle> legendRects = new List<Rectangle>();

        public void SetSegments(IEnumerable<Segment> list)
        {
            segments.Clear();
            segments.AddRange(list);
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = legendRects.FindIndex(r => r.Contains(e.Location));
            if (h != hover) { hover = h; Cursor = h >= 0 ? Cursors.Hand : Cursors.Default; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            var h = SegmentClicked;
            if (hover >= 0 && hover < segments.Count && h != null) h(this, new SegmentEventArgs(segments[hover]));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Draw.Smooth(g);
            int size = Math.Min(Height - Dpi.S(8), Math.Min(Width / 2, Dpi.S(190)));
            var r = new RectangleF(Dpi.S(4), (Height - size) / 2f, size, size);
            float thick = Math.Max(Dpi.S(12f), size * 0.13f);
            var rr = RectangleF.Inflate(r, -thick / 2, -thick / 2);
            double total = segments.Sum(s => s.Value);
            using (var pen = new Pen(T.LineSoft, thick)) g.DrawEllipse(pen, rr);
            if (total > 0)
            {
                float start = -90;
                float gap = segments.Count(s => s.Value > 0) > 1 ? 2.5f : 0;
                for (int i = 0; i < segments.Count; i++)
                {
                    var s = segments[i];
                    if (s.Value <= 0) continue;
                    float sweep = (float)(360 * s.Value / total);
                    using (var pen = new Pen(i == hover ? Theme.Mix(s.Color, Color.White, 0.2) : s.Color, i == hover ? thick + Dpi.S(3f) : thick))
                        g.DrawArc(pen, rr, start + gap / 2, Math.Max(0.5f, sweep - gap));
                    start += sweep;
                }
            }
            Draw.Text(g, CenterText, Fonts.Number, T.Ink, new Rectangle((int)r.X, (int)(r.Y + r.Height / 2 - Fonts.Number.Height + Dpi.S(4)), (int)r.Width, Fonts.Number.Height), Draw.Center);
            Draw.Text(g, CenterCaption, Fonts.Small, T.Muted, new Rectangle((int)r.X, (int)(r.Y + r.Height / 2 + Dpi.S(4)), (int)r.Width, Dpi.S(18)), Draw.Center);

            legendRects.Clear();
            int lx = (int)r.Right + Dpi.S(24), rowH = Dpi.S(30);
            int ly = (int)(Height / 2 - segments.Count * rowH / 2);
            for (int i = 0; i < segments.Count; i++)
            {
                var s = segments[i];
                var row = new Rectangle(lx - Dpi.S(8), ly + i * rowH, Width - lx, rowH);
                legendRects.Add(row);
                if (i == hover) Draw.Fill(g, row, Dpi.S(6), T.CardHover);
                int d = Dpi.S(9);
                Draw.Fill(g, new RectangleF(lx, row.Y + (rowH - d) / 2f, d, d), Dpi.S(2.5f), s.Color);
                Draw.Text(g, s.Label, Fonts.Body, T.Soft, new Rectangle(lx + Dpi.S(18), row.Y, row.Width - Dpi.S(90), rowH));
                string pct = total > 0 ? Math.Round(100 * s.Value / total).ToString(CultureInfo.InvariantCulture) + "%" : "0%";
                Draw.Text(g, ((int)s.Value).ToString(CultureInfo.InvariantCulture), Fonts.Semi, T.Ink, new Rectangle(row.Right - Dpi.S(80), row.Y, Dpi.S(36), rowH), Draw.Right);
                Draw.Text(g, pct, Fonts.Small, T.Muted, new Rectangle(row.Right - Dpi.S(42), row.Y, Dpi.S(36), rowH), Draw.Right);
            }
        }
    }

    public sealed class SegmentEventArgs : EventArgs
    {
        public SegmentEventArgs(Segment s) { Segment = s; }
        public Segment Segment { get; private set; }
    }

    public sealed class BarItem
    {
        public BarItem(string label, double fraction, string value, Color color, object tag) { Label = label; Fraction = fraction; Value = value; Color = color; Tag = tag; }
        public string Label;
        public double Fraction;
        public string Value;
        public Color Color;
        public object Tag;
    }

    /// <summary>Labelled horizontal bars, for compliance rates and top lists.</summary>
    public class BarList : Surface
    {
        private readonly List<BarItem> items = new List<BarItem>();
        private int hover = -1;

        public string EmptyText = "Nothing to show yet.";
        public event EventHandler<BarEventArgs> ItemClicked;

        public void SetItems(IEnumerable<BarItem> list)
        {
            items.Clear();
            items.AddRange(list);
            Invalidate();
        }

        private int RowHeight { get { return Dpi.S(40); } }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = e.Y / RowHeight;
            if (h >= items.Count) h = -1;
            if (h != hover) { hover = h; Cursor = h >= 0 && ItemClicked != null ? Cursors.Hand : Cursors.Default; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            var h = ItemClicked;
            if (hover >= 0 && h != null) h(this, new BarEventArgs(items[hover]));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            if (items.Count == 0) { Draw.Text(g, EmptyText, Fonts.Body, T.Muted, ClientRectangle, Draw.Center); return; }
            int rh = RowHeight;
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                int y = i * rh;
                if (y + rh > Height + 2) break;
                if (i == hover) Draw.Fill(g, new Rectangle(-Dpi.S(6), y, Width + Dpi.S(12), rh), Dpi.S(6), T.CardHover);
                Draw.Text(g, it.Label, Fonts.Body, T.Soft, new Rectangle(0, y + Dpi.S(3), Width - Dpi.S(70), Dpi.S(18)));
                Draw.Text(g, it.Value, Fonts.SmallSemi, T.Ink, new Rectangle(Width - Dpi.S(90), y + Dpi.S(3), Dpi.S(90), Dpi.S(18)), Draw.Right);
                var bar = new Rectangle(0, y + Dpi.S(25), Width, Dpi.S(6));
                Draw.Smooth(g);
                Draw.Fill(g, bar, Dpi.S(3), T.LineSoft);
                int w = (int)(bar.Width * Math.Max(0, Math.Min(1, it.Fraction)));
                if (w > 0) Draw.Gradient(g, new Rectangle(bar.X, bar.Y, Math.Max(w, bar.Height), bar.Height), Dpi.S(3), Theme.Mix(it.Color, T.Card, 0.25), it.Color, 0f);
            }
        }
    }

    public sealed class BarEventArgs : EventArgs
    {
        public BarEventArgs(BarItem item) { Item = item; }
        public BarItem Item { get; private set; }
    }

    public enum BannerKind { Info, Warn, Bad, Ok }

    /// <summary>A tinted notice with an icon.</summary>
    public class Banner : Surface
    {
        public Banner()
        {
            Height = Dpi.S(44);
            Kind = BannerKind.Info;
        }

        public BannerKind Kind { get; set; }
        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }

        public Color Color
        {
            get { return Kind == BannerKind.Warn ? T.Warn : Kind == BannerKind.Bad ? T.Bad : Kind == BannerKind.Ok ? T.Ok : T.Accent; }
        }

        public int HeightFor(int width)
        {
            return Math.Max(Dpi.S(44), Draw.MeasureHeight(Text, Fonts.Body, width - Dpi.S(56)) + Dpi.S(24));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Draw.Smooth(g);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Draw.Fill(g, r, Dpi.S(10f), T.Tint(Color, T.Dark ? 0.12 : 0.08));
            Draw.Stroke(g, r, Dpi.S(10f), T.Tint(Color, T.Dark ? 0.35 : 0.3), 1f);
            int s = Dpi.S(18);
            string icon = Kind == BannerKind.Ok ? "check-circle" : Kind == BannerKind.Info ? "info" : "alert";
            Icons.Draw(g, icon, new RectangleF(Dpi.S(14), Dpi.S(13), s, s), Color, 2f);
            Draw.Text(g, Text, Fonts.Body, T.Ink, new Rectangle(Dpi.S(42), Dpi.S(12), Width - Dpi.S(56), Height - Dpi.S(16)), Draw.Wrap);
        }
    }

    /// <summary>A centred message with an icon, used when there is nothing to show yet.</summary>
    public class EmptyState : Surface
    {
        private readonly List<Control> actions = new List<Control>();

        public EmptyState(string icon, string title, string text)
        {
            IconName = icon;
            Title = title;
            Text = text;
        }

        public string IconName { get; set; }
        public string Title { get; set; }

        public void AddAction(Control c)
        {
            actions.Add(c);
            Controls.Add(c);
            PerformLayout();
        }

        private int ContentTop
        {
            get
            {
                int h = Dpi.S(64) + Dpi.S(20) + Fonts.H2.Height + Dpi.S(8) + Draw.MeasureHeight(Text, Fonts.Body, TextWidth) + (actions.Any(a => a.Visible) ? Dpi.S(24) + Dpi.S(36) : 0);
                return Math.Max(Dpi.S(10), (Height - h) / 2);
            }
        }

        private int TextWidth { get { return Math.Min(Width - Dpi.S(40), Dpi.S(440)); } }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            var shown = actions.Where(a => a.Visible).ToList();
            int total = shown.Sum(a => a.Width) + Dpi.S(10) * Math.Max(0, shown.Count - 1);
            int x = (Width - total) / 2;
            int y = ContentTop + Dpi.S(64) + Dpi.S(20) + Fonts.H2.Height + Dpi.S(8) + Draw.MeasureHeight(Text, Fonts.Body, TextWidth) + Dpi.S(24);
            foreach (var a in shown)
            {
                a.Location = new Point(x, y);
                x += a.Width + Dpi.S(10);
            }
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); PerformLayout(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Draw.Smooth(g);
            int top = ContentTop;
            int s = Dpi.S(64);
            var r = new RectangleF((Width - s) / 2f, top, s, s);
            using (var path = new GraphicsPath())
            {
                var glow = RectangleF.Inflate(r, s * 0.6f, s * 0.6f);
                path.AddEllipse(glow);
                using (var br = new PathGradientBrush(path) { CenterColor = Theme.Alpha(T.Accent, T.Dark ? 40 : 26), SurroundColors = new[] { Theme.Alpha(T.Accent, 0) } }) g.FillPath(br, path);
            }
            Draw.Fill(g, r, Dpi.S(18), T.Tint(T.Accent, T.Dark ? 0.14 : 0.1));
            Draw.Stroke(g, r, Dpi.S(18), Theme.Alpha(T.Accent, 90), 1f);
            int i = Dpi.S(28);
            Icons.Draw(g, IconName, new RectangleF(r.X + (s - i) / 2f, r.Y + (s - i) / 2f, i, i), T.Accent, 1.8f);
            int y = top + s + Dpi.S(20);
            Draw.Text(g, Title, Fonts.H2, T.Ink, new Rectangle(0, y, Width, Fonts.H2.Height), Draw.Center);
            y += Fonts.H2.Height + Dpi.S(8);
            int tw = TextWidth;
            Draw.Text(g, Text, Fonts.Body, T.Muted, new Rectangle((Width - tw) / 2, y, tw, Height - y), Draw.Wrap | TextFormatFlags.HorizontalCenter);
        }
    }

    /// <summary>Menu colours for context menus and drop-downs.</summary>
    public sealed class MenuRenderer : ToolStripProfessionalRenderer
    {
        public MenuRenderer() : base(new MenuColors()) { RoundedEdges = false; }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Theme.Current.Ink : Theme.Current.Faint;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = Theme.Current.Muted;
            base.OnRenderArrow(e);
        }

        public static ContextMenuStrip Create()
        {
            var m = new ContextMenuStrip { Renderer = new MenuRenderer(), ShowImageMargin = false, Font = Fonts.Body, Padding = new Padding(Dpi.S(4)) };
            m.BackColor = Theme.Current.Card;
            return m;
        }

        public static ToolStripMenuItem Item(string text, EventHandler click)
        {
            var it = new ToolStripMenuItem(text) { Padding = new Padding(Dpi.S(4), Dpi.S(6), Dpi.S(4), Dpi.S(6)) };
            if (click != null) it.Click += click;
            return it;
        }

        private sealed class MenuColors : ProfessionalColorTable
        {
            private static Theme T { get { return Theme.Current; } }
            public override Color ToolStripDropDownBackground { get { return T.Card; } }
            public override Color MenuBorder { get { return T.Line; } }
            public override Color MenuItemBorder { get { return T.CardHover; } }
            public override Color MenuItemSelected { get { return T.CardHover; } }
            public override Color MenuItemSelectedGradientBegin { get { return T.CardHover; } }
            public override Color MenuItemSelectedGradientEnd { get { return T.CardHover; } }
            public override Color ImageMarginGradientBegin { get { return T.Card; } }
            public override Color ImageMarginGradientMiddle { get { return T.Card; } }
            public override Color ImageMarginGradientEnd { get { return T.Card; } }
            public override Color SeparatorDark { get { return T.Line; } }
            public override Color SeparatorLight { get { return T.Line; } }
        }
    }
}
