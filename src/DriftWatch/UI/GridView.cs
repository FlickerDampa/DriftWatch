using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace DriftWatch.UI
{
    public sealed class CellArgs
    {
        public Graphics G;
        public Rectangle Bounds;
        public object Item;
        public int Index;
        public bool Selected;
        public bool Hover;
        public Theme T;
    }

    public sealed class CellClickEventArgs : EventArgs
    {
        public CellClickEventArgs(object item, int column) { Item = item; Column = column; }
        public object Item { get; private set; }
        public int Column { get; private set; }
    }

    public sealed class GridColumn
    {
        public GridColumn(string header, int width)
        {
            Header = header;
            Width = width;
            Fill = 0;
        }

        public string Header;
        /// <summary>Width in design pixels. Ignored when <see cref="Fill"/> is above zero.</summary>
        public int Width;
        /// <summary>Share of the leftover width.</summary>
        public float Fill;
        public int MinWidth = 60;
        public TextFormatFlags Align = Draw.Left;
        public Func<object, string> Text;
        public Func<object, IComparable> SortKey;
        public Action<CellArgs> Paint;
        public string Tip;
        public Func<object, string> CellTip;
        public bool Mono;
        /// <summary>Space on each side of the cell, in design pixels.</summary>
        public int Pad = 10;
        public bool Hidden;
        public object Tag;
    }

    /// <summary>
    /// A fast owner-drawn table: virtualised rows, sortable headers, hover and selection, keyboard navigation,
    /// a slim scroll bar and per-cell custom drawing.
    /// </summary>
    public class GridView : Surface
    {
        private readonly List<GridColumn> columns = new List<GridColumn>();
        private List<object> items = new List<object>();
        private int selected = -1, hover = -1, scroll, sortColumn = -1, hoverHeader = -1;
        private bool sortDesc, dragging;
        private int dragStart, dragScroll;
        private readonly ToolTip tip = new ToolTip { InitialDelay = 400, ReshowDelay = 100 };
        private string lastTip;

        public GridView()
        {
            RowHeight = Dpi.S(44);
            ShowHeader = true;
            EmptyText = "Nothing to show.";
            SetStyle(ControlStyles.Selectable, true);
            TabStop = true;
        }

        public List<GridColumn> Columns { get { return columns; } }
        public int RowHeight { get; set; }
        public bool ShowHeader { get; set; }
        public string EmptyText { get; set; }
        public string EmptyTitle { get; set; }
        public Func<object, Color?> Stripe;
        public Func<object, bool> Dim;
        public bool Zebra { get; set; }

        public event EventHandler SelectionChanged;
        public event EventHandler ItemActivated;
        public event MouseEventHandler RowRightClick;
        /// <summary>Raised with the column index when a row is clicked.</summary>
        public event EventHandler<CellClickEventArgs> CellClicked;

        private int HeaderHeight { get { return ShowHeader ? Dpi.S(36) : 0; } }
        private int ContentHeight { get { return items.Count * RowHeight; } }
        private int ViewHeight { get { return Math.Max(0, Height - HeaderHeight); } }
        private int MaxScroll { get { return Math.Max(0, ContentHeight - ViewHeight); } }

        public IList<object> Items { get { return items; } }
        public int Count { get { return items.Count; } }

        public void SetItems(IEnumerable list, bool keepSelection)
        {
            object keep = keepSelection ? SelectedItem : null;
            items = new List<object>();
            foreach (var o in list) items.Add(o);
            ApplySort();
            int idx = keep != null ? items.IndexOf(keep) : -1;
            selected = idx;
            scroll = Math.Min(scroll, MaxScroll);
            if (!keepSelection) scroll = 0;
            hover = -1;
            Invalidate();
            if (!keepSelection || idx < 0) RaiseSelection();
        }

        public object SelectedItem
        {
            get { return selected >= 0 && selected < items.Count ? items[selected] : null; }
            set
            {
                int i = value == null ? -1 : items.IndexOf(value);
                if (i != selected) { selected = i; EnsureVisible(i); Invalidate(); RaiseSelection(); }
            }
        }

        public int SelectedIndex
        {
            get { return selected; }
            set
            {
                int i = Math.Max(-1, Math.Min(items.Count - 1, value));
                if (i != selected) { selected = i; EnsureVisible(i); Invalidate(); RaiseSelection(); }
            }
        }

        public void Sort(int column, bool descending)
        {
            sortColumn = column;
            sortDesc = descending;
            object keep = SelectedItem;
            ApplySort();
            selected = keep != null ? items.IndexOf(keep) : -1;
            Invalidate();
        }

        private void ApplySort()
        {
            if (sortColumn < 0 || sortColumn >= columns.Count) return;
            var col = columns[sortColumn];
            Func<object, IComparable> key = col.SortKey ?? (col.Text != null ? (Func<object, IComparable>)(o => col.Text(o)) : null);
            if (key == null) return;
            var sorted = sortDesc ? items.OrderByDescending(o => key(o) ?? "").ToList() : items.OrderBy(o => key(o) ?? "").ToList();
            items = sorted;
        }

        private void RaiseSelection()
        {
            var h = SelectionChanged;
            if (h != null) h(this, EventArgs.Empty);
        }

        public void EnsureVisible(int i)
        {
            if (i < 0 || ViewHeight < RowHeight) return;
            int top = i * RowHeight, bottom = top + RowHeight;
            if (top < scroll) scroll = top;
            else if (bottom > scroll + ViewHeight) scroll = bottom - ViewHeight;
            scroll = Math.Max(0, Math.Min(MaxScroll, scroll));
        }

        private int[] ColumnWidths()
        {
            var visible = columns.Where(c => !c.Hidden).ToList();
            int avail = Width - Dpi.S(8) - Lead;
            int fixedW = visible.Where(c => c.Fill <= 0).Sum(c => Dpi.S(c.Width));
            float fills = visible.Where(c => c.Fill > 0).Sum(c => c.Fill);
            int left = Math.Max(0, avail - fixedW);
            var result = new int[columns.Count];
            for (int i = 0; i < columns.Count; i++)
            {
                var c = columns[i];
                if (c.Hidden) { result[i] = 0; continue; }
                result[i] = c.Fill > 0 ? Math.Max(Dpi.S(c.MinWidth), (int)(left * c.Fill / Math.Max(0.001f, fills))) : Dpi.S(c.Width);
            }
            return result;
        }

        /// <summary>Extra space on the left for the coloured stripe.</summary>
        private int Lead { get { return Stripe != null ? Dpi.S(8) : 0; } }

        private int RowAt(int y)
        {
            if (y < HeaderHeight) return -1;
            int i = (y - HeaderHeight + scroll) / RowHeight;
            return i >= 0 && i < items.Count ? i : -1;
        }

        private int ColumnAt(int x)
        {
            var w = ColumnWidths();
            int cx = Dpi.S(4) + Lead;
            for (int i = 0; i < columns.Count; i++)
            {
                if (columns[i].Hidden) continue;
                if (x >= cx && x < cx + w[i]) return i;
                cx += w[i];
            }
            return -1;
        }

        private Rectangle ThumbRect
        {
            get
            {
                if (MaxScroll <= 0) return Rectangle.Empty;
                int track = ViewHeight - Dpi.S(8);
                int h = Math.Max(Dpi.S(28), (int)(track * (ViewHeight / (double)ContentHeight)));
                int y = HeaderHeight + Dpi.S(4) + (int)((track - h) * (scroll / (double)MaxScroll));
                return new Rectangle(Width - Dpi.S(8), y, Dpi.S(5), h);
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            scroll = Math.Max(0, Math.Min(MaxScroll, scroll - e.Delta / 120 * RowHeight * 2));
            hover = RowAt(e.Y);
            Invalidate();
            base.OnMouseWheel(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (dragging)
            {
                var t = ThumbRect;
                int track = ViewHeight - Dpi.S(8) - t.Height;
                if (track > 0) scroll = Math.Max(0, Math.Min(MaxScroll, dragScroll + (int)((e.Y - dragStart) * MaxScroll / (double)track)));
                Invalidate();
                return;
            }
            int h = RowAt(e.Y);
            int hh = e.Y < HeaderHeight ? ColumnAt(e.X) : -1;
            if (h != hover || hh != hoverHeader) { hover = h; hoverHeader = hh; Invalidate(); }
            string t2 = null;
            if (hh >= 0) t2 = columns[hh].Tip;
            else if (h >= 0)
            {
                int c = ColumnAt(e.X);
                if (c >= 0 && columns[c].CellTip != null) t2 = columns[c].CellTip(items[h]);
            }
            if (t2 != lastTip) { lastTip = t2; tip.SetToolTip(this, t2); }
            Cursor = hh >= 0 && (columns[hh].SortKey != null || columns[hh].Text != null) ? Cursors.Hand : Cursors.Default;
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hover = -1;
            hoverHeader = -1;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            var t = ThumbRect;
            if (!t.IsEmpty && e.X >= Width - Dpi.S(12))
            {
                if (t.Contains(new Point(t.X + 1, e.Y))) { dragging = true; dragStart = e.Y; dragScroll = scroll; }
                else { scroll = Math.Max(0, Math.Min(MaxScroll, scroll + (e.Y < t.Y ? -ViewHeight : ViewHeight))); Invalidate(); }
                return;
            }
            if (e.Y < HeaderHeight)
            {
                int c = ColumnAt(e.X);
                if (c >= 0 && (columns[c].SortKey != null || columns[c].Text != null))
                {
                    if (sortColumn == c) sortDesc = !sortDesc; else { sortColumn = c; sortDesc = false; }
                    Sort(sortColumn, sortDesc);
                }
                return;
            }
            int i = RowAt(e.Y);
            if (i >= 0) SelectedIndex = i;
            if (i >= 0 && e.Button == MouseButtons.Left)
            {
                var cc = CellClicked;
                int col = ColumnAt(e.X);
                if (cc != null && col >= 0) cc(this, new CellClickEventArgs(items[i], col));
            }
            if (e.Button == MouseButtons.Right && i >= 0)
            {
                var h = RowRightClick;
                if (h != null) h(this, e);
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            dragging = false;
            base.OnMouseUp(e);
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            if (RowAt(e.Y) >= 0 && e.Button == MouseButtons.Left)
            {
                var h = ItemActivated;
                if (h != null) h(this, EventArgs.Empty);
            }
            base.OnMouseDoubleClick(e);
        }

        protected override bool IsInputKey(Keys keyData)
        {
            switch (keyData & Keys.KeyCode)
            {
                case Keys.Up: case Keys.Down: case Keys.PageUp: case Keys.PageDown: case Keys.Home: case Keys.End: case Keys.Enter: return true;
            }
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            int page = Math.Max(1, ViewHeight / RowHeight - 1);
            switch (e.KeyCode)
            {
                case Keys.Down: SelectedIndex = Math.Min(items.Count - 1, selected + 1); e.Handled = true; break;
                case Keys.Up: SelectedIndex = Math.Max(0, selected - 1); e.Handled = true; break;
                case Keys.PageDown: SelectedIndex = Math.Min(items.Count - 1, selected + page); e.Handled = true; break;
                case Keys.PageUp: SelectedIndex = Math.Max(0, selected - page); e.Handled = true; break;
                case Keys.Home: SelectedIndex = 0; e.Handled = true; break;
                case Keys.End: SelectedIndex = items.Count - 1; e.Handled = true; break;
                case Keys.Enter:
                    var h = ItemActivated;
                    if (h != null && SelectedItem != null) h(this, EventArgs.Empty);
                    e.Handled = true;
                    break;
            }
            base.OnKeyDown(e);
        }

        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnResize(EventArgs e)
        {
            scroll = Math.Max(0, Math.Min(MaxScroll, scroll));
            base.OnResize(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var widths = ColumnWidths();
            int hh = HeaderHeight;
            if (ShowHeader)
            {
                int x = Dpi.S(4) + Lead;
                for (int i = 0; i < columns.Count; i++)
                {
                    var c = columns[i];
                    if (c.Hidden) continue;
                    var r = new Rectangle(x + Dpi.S(c.Pad), 0, widths[i] - Dpi.S(c.Pad * 2), hh);
                    string text = c.Header.ToUpperInvariant();
                    var color = i == hoverHeader ? T.Soft : T.Muted;
                    Draw.Text(g, text, Fonts.Tiny, color, r, c.Align);
                    if (i == sortColumn)
                    {
                        int tw = Draw.Measure(text, Fonts.Tiny).Width;
                        int ax = (c.Align & TextFormatFlags.Right) != 0 ? r.Right - tw - Dpi.S(14) : (c.Align & TextFormatFlags.HorizontalCenter) != 0 ? r.X + (r.Width + tw) / 2 + Dpi.S(2) : r.X + tw + Dpi.S(4);
                        Icons.Draw(g, sortDesc ? "arrow-down" : "arrow-up", new RectangleF(ax, hh / 2f - Dpi.S(5), Dpi.S(10), Dpi.S(10)), T.Accent, 2.4f);
                    }
                    x += widths[i];
                }
                using (var pen = new Pen(T.Line, 1f)) g.DrawLine(pen, 0, hh - 1, Width, hh - 1);
            }

            if (items.Count == 0)
            {
                var er = new Rectangle(Dpi.S(20), hh, Width - Dpi.S(40), ViewHeight);
                if (!string.IsNullOrEmpty(EmptyTitle))
                {
                    int s = Dpi.S(28);
                    int total = s + Dpi.S(12) + Fonts.H3.Height + Dpi.S(6) + Fonts.Body.Height * 2;
                    int y = er.Y + (er.Height - total) / 2;
                    Icons.Draw(g, "check-circle", new RectangleF((Width - s) / 2f, y, s, s), T.Faint, 1.6f);
                    y += s + Dpi.S(12);
                    Draw.Text(g, EmptyTitle, Fonts.H3, T.Soft, new Rectangle(er.X, y, er.Width, Fonts.H3.Height), Draw.Center);
                    y += Fonts.H3.Height + Dpi.S(6);
                    Draw.Text(g, EmptyText, Fonts.Body, T.Muted, new Rectangle(er.X, y, er.Width, Fonts.Body.Height * 3), Draw.Wrap | TextFormatFlags.HorizontalCenter);
                }
                else Draw.Text(g, EmptyText, Fonts.Body, T.Muted, er, Draw.Center);
                return;
            }

            var clip = new Rectangle(0, hh, Width, ViewHeight);
            g.SetClip(clip);
            int first = scroll / RowHeight;
            int last = Math.Min(items.Count - 1, (scroll + ViewHeight) / RowHeight);
            var args = new CellArgs { G = g, T = T };
            for (int row = first; row <= last; row++)
            {
                var item = items[row];
                int y = hh + row * RowHeight - scroll;
                var rr = new Rectangle(Dpi.S(2), y + Dpi.S(1), Width - Dpi.S(14), RowHeight - Dpi.S(2));
                bool sel = row == selected, hov = row == hover;
                Draw.Smooth(g);
                if (sel) Draw.Fill(g, rr, Dpi.S(8), T.Tint(T.Accent, T.Dark ? 0.13 : 0.08));
                else if (hov) Draw.Fill(g, rr, Dpi.S(8), T.CardHover);
                else if (Zebra && row % 2 == 1) Draw.Fill(g, rr, Dpi.S(8), Theme.Mix(T.Card, T.CardHover, 0.4));
                if (sel) Draw.Stroke(g, rr, Dpi.S(8), Theme.Alpha(T.Accent, Focused ? 150 : 70), 1f);
                if (Stripe != null)
                {
                    var c = Stripe(item);
                    if (c.HasValue) Draw.Fill(g, new RectangleF(rr.X + Dpi.S(4), rr.Y + Dpi.S(9), Dpi.S(3), rr.Height - Dpi.S(18)), Dpi.S(1.5f), c.Value);
                }
                if (!sel && !hov && row < last) using (var pen = new Pen(T.LineSoft, 1f)) g.DrawLine(pen, Dpi.S(12), y + RowHeight - 1, Width - Dpi.S(20), y + RowHeight - 1);
                bool dim = Dim != null && Dim(item);
                int x = Dpi.S(4) + Lead;
                for (int i = 0; i < columns.Count; i++)
                {
                    var c = columns[i];
                    if (c.Hidden) continue;
                    var cell = new Rectangle(x + Dpi.S(c.Pad), y, widths[i] - Dpi.S(c.Pad * 2), RowHeight);
                    args.Bounds = cell;
                    args.Item = item;
                    args.Index = row;
                    args.Selected = sel;
                    args.Hover = hov;
                    if (c.Paint != null) c.Paint(args);
                    else if (c.Text != null) Draw.Text(g, c.Text(item), c.Mono ? Fonts.Mono : Fonts.Body, dim ? T.Faint : (i == 0 ? T.Ink : T.Soft), cell, c.Align);
                    x += widths[i];
                }
            }
            g.ResetClip();

            var thumb = ThumbRect;
            if (!thumb.IsEmpty)
            {
                Draw.Smooth(g);
                Draw.Fill(g, thumb, thumb.Width / 2f, dragging ? T.Muted : T.Faint);
            }
        }
    }
}
