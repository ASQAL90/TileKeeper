using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace TileKeeper.Class
{
    /// <summary>
    /// Draws the tiling grid for one screen and raises a per-cell click so the settings
    /// form can assign programs to specific segments. The rendering calls the same
    /// <see cref="WindowMonitor.ResolveLayout"/> used by the runtime position keeper,
    /// so the preview always matches the deployed layout (including auto-fill).
    ///
    /// Each cell is rendered as a coloured, numbered tile instead of a program name:
    /// cell N uses <see cref="CellColors"/>[N-1] and shows "N". The matching screen
    /// checklist paints the same colour/number badge on the row of the program that
    /// occupies that cell, so the two controls read as one map regardless of how small
    /// the tiles get.
    ///
    /// The layout, the cell→program map and every GDI object are computed once per
    /// <see cref="SetModel"/>/control-font rather than per paint or per mouse-move:
    /// this control repaints on hover, so recomputing the layout there dominated the
    /// settings form's CPU use.
    /// </summary>
    public class GridMapPanel : Panel
    {
        /// <summary>
        /// Shared palette keyed by cell index (cell 1 → [0]). Capped at 9 entries to match
        /// the 3×3 = 9 cell maximum. Also consumed by the screen checklists so a row's
        /// badge colour is identical to its cell.
        /// </summary>
        public static readonly Color[] CellColors =
        {
            Color.FromArgb(0x00, 0x57, 0xB8), // 1 blue
            Color.FromArgb(0xE6, 0x8A, 0x00), // 2 amber
            Color.FromArgb(0xC6, 0x28, 0x28), // 3 red
            Color.FromArgb(0x2E, 0x7D, 0x32), // 4 green
            Color.FromArgb(0x7B, 0x1F, 0xA2), // 5 purple
            Color.FromArgb(0x00, 0x69, 0x5C), // 6 teal
            Color.FromArgb(0x6D, 0x4C, 0x41), // 7 brown
            Color.FromArgb(0x30, 0x3F, 0x9F), // 8 indigo
            Color.FromArgb(0x00, 0x83, 0x8F), // 9 cyan
        };

        // ponytail: 27 GDI objects for the process lifetime — no per-paint allocation, never disposed.
        private static readonly SolidBrush[] FilledBrushes;
        private static readonly SolidBrush[] EmptyBrushes;
        private static readonly Pen[]         CellPens;
        private static readonly Color[]       EmptyTextColors;

        static GridMapPanel()
        {
            int n = CellColors.Length;
            FilledBrushes   = new SolidBrush[n];
            EmptyBrushes    = new SolidBrush[n];
            CellPens        = new Pen[n];
            EmptyTextColors = new Color[n];
            for (int i = 0; i < n; i++)
            {
                FilledBrushes[i]   = new SolidBrush(CellColors[i]);
                EmptyBrushes[i]    = new SolidBrush(LightTint(CellColors[i]));
                CellPens[i]        = new Pen(CellColors[i]);
                EmptyTextColors[i] = DarkTint(CellColors[i]);
            }
        }

        /// <summary>
        /// The cached solid fill for cell <paramref name="cellNo"/> (1-based) — the exact brush
        /// the grid tile is painted with, so the screen checklists can tint their rows to match
        /// without allocating a new brush per paint.
        /// </summary>
        public static SolidBrush CellBrush(int cellNo)
        {
            int i = (cellNo - 1) % FilledBrushes.Length;
            if (i < 0) i += FilledBrushes.Length;
            return FilledBrushes[i];
        }

        private List<string> _names = new List<string>();
        private int[]        _slots = new int[0];
        private int          _rows;
        private int          _cols;

        // Derived from (_names, _slots, _rows, _cols) — rebuilt only in SetModel, never per paint.
        private WindowMonitor.CellRect[] _layout = new WindowMonitor.CellRect[0];
        private string[]                 _cellProgram = new string[0];

        private Font _labelFont;

        private string _tooltipCell = null;
        private readonly ToolTip _tooltip = new ToolTip();

        /// <summary>Raised when a grid cell is clicked; argument is the zero-based cell index.</summary>
        public event Action<int> CellClicked;

        public GridMapPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw, true);
            BackColor = SystemColors.Control;
        }

        /// <summary>
        /// Reloads the data model. <paramref name="names"/> is the ordered program list for
        /// this screen, <paramref name="slots"/> the parallel slot array (-1 = auto-fill),
        /// and <paramref name="rows"/>/<paramref name="cols"/> the grid shape (0 = auto).
        /// </summary>
        public void SetModel(List<string> names, int[] slots, int rows, int cols)
        {
            _names = names ?? new List<string>();
            _slots = slots ?? new int[0];
            _rows  = rows;
            _cols  = cols;
            _tooltipCell = null;
            RebuildLayout();
            Invalidate();
        }

        /// <summary>Recomputes the tile rects and the cell→program map from the current model.</summary>
        private void RebuildLayout()
        {
            _cellProgram = new string[0];

            if (_names.Count == 0)
            {
                _layout = new WindowMonitor.CellRect[0];
                return;
            }

            WindowMonitor.ResolveLayout(_names, _rows, _cols, _slots, out _layout, out int[] cells);

            _cellProgram = new string[_layout.Length];
            for (int i = 0; i < cells.Length && i < _names.Count; i++)
            {
                int cell = cells[i];
                // First program wins a cell, matching the old FindProgram scan order.
                if (cell >= 0 && cell < _cellProgram.Length && _cellProgram[cell] == null)
                    _cellProgram[cell] = _names[i];
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            // No programs selected for this screen: nothing is tiled, so show a single
            // "0" placeholder instead of the "1" that ComputeLayout's Math.Max(1, n)
            // would otherwise produce.
            if (_names.Count == 0)
            {
                DrawEmptyState(e.Graphics);
                return;
            }

            if (_layout.Length == 0) return;

            Font labelFont = LabelFont;
            const int pad = 6;

            for (int i = 0; i < _layout.Length; i++)
            {
                var tile = _layout[i];

                var rect = new Rectangle(
                    (int)(tile.X * Width) + pad / 2,
                    (int)(tile.Y * Height) + pad / 2,
                    (int)(tile.W * Width) - pad,
                    (int)(tile.H * Height) - pad);

                int palette = i % CellColors.Length;
                bool occupied = _cellProgram[i] != null;

                e.Graphics.FillRectangle(occupied ? FilledBrushes[palette] : EmptyBrushes[palette], rect);

                TextRenderer.DrawText(e.Graphics, (i + 1).ToString(), labelFont, rect,
                    occupied ? Color.White : EmptyTextColors[palette],
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

                e.Graphics.DrawRectangle(CellPens[palette], rect);
            }
        }

        private Font LabelFont
        {
            get
            {
                // Lazily built once per control font; TextRenderer needs a concrete Font instance.
                if (_labelFont == null)
                    _labelFont = new Font(Font.FontFamily, 8f, FontStyle.Bold);
                return _labelFont;
            }
        }

        /// <summary>Single grey "0" tile shown when the screen has no selected programs.</summary>
        private void DrawEmptyState(Graphics g)
        {
            const int pad = 6;
            var rect = new Rectangle(pad / 2, pad / 2, Width - pad, Height - pad);
            if (rect.Width <= 0 || rect.Height <= 0)
                return;

            using (var brush = new SolidBrush(SystemColors.Control))
                g.FillRectangle(brush, rect);
            using (var pen = new Pen(SystemColors.ControlDark))
                g.DrawRectangle(pen, rect);
            TextRenderer.DrawText(g, "0", LabelFont, rect, SystemColors.GrayText,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);

            int cell = HitCell(e.X, e.Y);
            if (cell >= 0)
                CellClicked?.Invoke(cell);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            int cell = HitCell(e.X, e.Y);
            if (cell < 0)
            {
                if (_tooltipCell != null)
                {
                    _tooltipCell = null;
                    _tooltip.SetToolTip(this, string.Empty);
                }
                return;
            }

            string program = _cellProgram[cell];
            string label = "Cell " + (cell + 1) + ": " + (program ?? "(empty)");
            if (label == _tooltipCell)
                return;

            _tooltipCell = label;
            _tooltip.SetToolTip(this, label);
        }

        /// <summary>Zero-based cell under the given point, or -1 if none.</summary>
        private int HitCell(int x, int y)
        {
            if (_layout.Length == 0 || Width <= 0 || Height <= 0)
                return -1; // no programs → the placeholder "0" tile is not clickable

            double fx = x / (double)Width;
            double fy = y / (double)Height;

            for (int i = 0; i < _layout.Length; i++)
            {
                var tile = _layout[i];
                if (fx >= tile.X && fx < tile.X + tile.W && fy >= tile.Y && fy < tile.Y + tile.H)
                    return i;
            }
            return -1;
        }

        /// <summary>Blend a cell colour toward white for the empty-slot (not-yet-occupied) tint.</summary>
        private static Color LightTint(Color c)
        {
            const double keep = 0.35;
            return Color.FromArgb(
                (int)(c.R + (255 - c.R) * (1 - keep)),
                (int)(c.G + (255 - c.G) * (1 - keep)),
                (int)(c.B + (255 - c.B) * (1 - keep)));
        }

        private static Color DarkTint(Color c)
        {
            const double keep = 0.6;
            return Color.FromArgb((int)(c.R * keep), (int)(c.G * keep), (int)(c.B * keep));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _labelFont != null)
            {
                _labelFont.Dispose();
                _labelFont = null;
                _tooltip.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
