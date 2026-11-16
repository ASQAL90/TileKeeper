using System;
using System.Drawing;
using System.Windows.Forms;

namespace TileKeeper
{
    /// <summary>
    /// CheckedListBox that fills each row with its mapped grid-cell colour before the base
    /// <c>OnDrawItem</c> paints the checkbox and text on top. The stock control already
    /// owner-draws internally (its CreateParams adds LBS.OWNERDRAWFIXED and OnDrawItem draws
    /// the checkbox/selection/text) even though the public DrawMode/DrawItem face is hidden —
    /// so subclassing and overriding OnDrawItem is the supported way to colour rows.
    /// </summary>
    public class ColoredCheckedListBox : CheckedListBox
    {
        private Func<int, Color> _colorOf;

        /// <summary>
        /// Called with a row index at paint time and expected to return the fill colour
        /// (or <see cref="Color.Empty"/> to leave the row blank). Setting it invalidates.
        /// </summary>
        public Func<int, Color> ColorOf
        {
            get { return _colorOf; }
            set { _colorOf = value; Invalidate(); }
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0 || _colorOf == null || Items.Count <= 0)
            {
                base.OnDrawItem(e);
                return;
            }

            var fill = _colorOf(e.Index);
            if (fill == Color.Empty)
            {
                base.OnDrawItem(e);
                return;
            }

            // Base first: themed checkbox, gutter, focus/selection all stay native. Then
            // overpaint just the row body (past the 16px checkbox column) with the cell colour
            // and re-draw the display text on top, so a checked row shows its tile as the fill.
            base.OnDrawItem(e);

            var contentRect = e.Bounds;
            contentRect.X = e.Bounds.X + 16;
            if (contentRect.Width <= 0 || contentRect.Height <= 0)
                return;

            using (var brush = new SolidBrush(fill))
                e.Graphics.FillRectangle(brush, contentRect);

            TextRenderer.DrawText(e.Graphics, GetItemText(Items[e.Index]), e.Font, contentRect,
                Color.White,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }
}