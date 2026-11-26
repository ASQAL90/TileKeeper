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
        private static readonly SolidBrush    UnassignedBrush =
            new SolidBrush(Color.FromArgb(0xE8, 0xE8, 0xE8));
        private static readonly Pen           UnassignedPen =
            new Pen(SystemColors.ControlDark);
        private static readonly Color         UnassignedText = Color.DimGray;

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
        private List<string> _displayNames = new List<string>();
        private int[]        _slots = new int[0];
        private int          _rows;
        private int          _cols;
        private int          _reservedCells;
        private int[]        _emptyCells = new int[0];

        // Derived from (_names, _slots, _rows, _cols) — rebuilt only in SetModel, never per paint.
        private WindowMonitor.CellRect[] _layout = new WindowMonitor.CellRect[0];
        private string[]                 _cellProgram = new string[0];
        private string[]                 _cellDisplay = new string[0];

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
        /// <paramref name="rows"/>/<paramref name="cols"/> the grid shape (0 = auto), and
        /// <paramref name="reservedCells"/> the highest pinned cell + 1 (pins of unchecked
        /// rows included) so an auto grid keeps its shape when a row is unchecked.
        /// <paramref name="emptyCells"/> are cells cleared by the user: they remain in the
        /// shape but are never auto-filled by another program.
        /// </summary>
        public void SetModel(List<string> names, int[] slots, int rows, int cols, int reservedCells = 0,
                             int[] emptyCells = null, List<string> displayNames = null)
        {
            var newNames = names ?? new List<string>();
            var newSlots = slots ?? new int[0];
            var newEmpty = emptyCells ?? new int[0];
            var newDisplay = displayNames ?? new List<string>();

            // RefreshAllGrids runs on every tray show and checkbox click. If the model is
            // identical, keep the cached layout/GDI state and skip the repaint entirely.
            if (_rows == rows && _cols == cols && _reservedCells == reservedCells
                && SameNames(newNames) && SameSlots(newSlots) && SameInts(_emptyCells, newEmpty)
                && SameDisplayNames(newDisplay))
                return;

            _names = newNames;
            _displayNames = newDisplay;
            _slots = newSlots;
            _rows  = rows;
            _cols  = cols;
            _reservedCells = reservedCells;
            _emptyCells = newEmpty;
            _tooltipCell = null;
            RebuildLayout();
            Invalidate();
        }

        private static bool SameInts(int[] a, int[] b)
        {
            if (a.Length != b.Length)
                return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i])
                    return false;
            return true;
        }

        private bool SameNames(List<string> names)
        {
            if (_names.Count != names.Count)
                return false;
            for (int i = 0; i < names.Count; i++)
                if (!string.Equals(_names[i], names[i], StringComparison.Ordinal))
                    return false;
            return true;
        }

        private bool SameDisplayNames(List<string> names)
        {
            if (_displayNames.Count != names.Count)
                return false;
            for (int i = 0; i < names.Count; i++)
                if (!string.Equals(_displayNames[i], names[i], StringComparison.Ordinal))
                    return false;
            return true;
        }

        private bool SameSlots(int[] slots)
        {
            if (_slots.Length != slots.Length)
                return false;
            for (int i = 0; i < slots.Length; i++)
                if (_slots[i] != slots[i])
                    return false;
            return true;
        }

        /// <summary>Recomputes the tile rects and the cell→program map from the current model.</summary>
        private void RebuildLayout()
        {
            _cellProgram = new string[0];
            _cellDisplay = new string[0];

            if (_names.Count == 0)
            {
                // An explicit rows×cols shape (or the reserved cells of a previously sized
                // auto grid) still defines the grid even before any program is mapped. Show
                // every cell as gray/clickable so a row from this screen's list can be assigned
                // straight into one of them.
                if (_rows > 0 && _cols > 0)
                {
                    _layout = new WindowMonitor.CellRect[_rows * _cols];
                    double w = 1.0 / _cols;
                    double h = 1.0 / _rows;
                    for (int r = 0; r < _rows; r++)
                        for (int c = 0; c < _cols; c++)
                            _layout[r * _cols + c] =
                                new WindowMonitor.CellRect(c * w, r * h, w, h);
                    _cellProgram = new string[_layout.Length];
                    _cellDisplay = new string[_layout.Length];
                }
                else if (_reservedCells > 0)
                {
                    // Same auto-shape table the populated path uses, so clearing the last row
                    // leaves exactly the same matrix the user saw before.
                    _layout = WindowMonitor.ComputeLayout(_reservedCells, 0, 0);
                    _cellProgram = new string[_layout.Length];
                    _cellDisplay = new string[_layout.Length];
                }
                else
                {
                    _layout = new WindowMonitor.CellRect[0];
                }
                return;
            }

            WindowMonitor.ResolveLayout(_names, _rows, _cols, _slots, out _layout, out int[] cells,
                                        _reservedCells, _emptyCells);

            _cellProgram = new string[_layout.Length];
            _cellDisplay = new string[_layout.Length];
            for (int i = 0; i < cells.Length && i < _names.Count; i++)
            {
                int cell = cells[i];
                // A disabled Arrange row is sent as "" so its cell stays reserved but empty
                // (same placeholder the runtime resolver produces). Only real programs claim
                // a filled tile.
                if (string.IsNullOrWhiteSpace(_names[i]))
                    continue;
                // First program wins a cell, matching the old FindProgram scan order.
                if (cell >= 0 && cell < _cellProgram.Length && _cellProgram[cell] == null)
                {
                    _cellProgram[cell] = _names[i];
                    _cellDisplay[cell] = i < _displayNames.Count && !string.IsNullOrWhiteSpace(_displayNames[i])
                        ? _displayNames[i]
                        : _names[i];
                }
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            // No layout at all (no programs and no explicit grid): show a single "0"
            // placeholder instead of the "1" that ComputeLayout's Math.Max(1, n) would
            // otherwise produce. With an explicit grid, _layout holds the gray empty cells
            // and the normal loop below paints them.
            if (_layout.Length == 0)
            {
                DrawEmptyState(e.Graphics);
                return;
            }

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
                // In an explicit grid every unassigned cell is neutral gray, whether the
                // screen has zero programs or some cells are still free. Auto layouts keep
                // the old light-tint placeholder for a disabled Arrange row.
                bool fixedGrid = _rows > 0 && _cols > 0 && !occupied;

                e.Graphics.FillRectangle(occupied ? FilledBrushes[palette]
                                            : fixedGrid ? UnassignedBrush : EmptyBrushes[palette], rect);

                TextRenderer.DrawText(e.Graphics, (i + 1).ToString(), labelFont, rect,
                    occupied ? Color.White : fixedGrid ? UnassignedText : EmptyTextColors[palette],
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

                e.Graphics.DrawRectangle(fixedGrid ? UnassignedPen : CellPens[palette], rect);
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

            string program = cell < _cellDisplay.Length ? _cellDisplay[cell] : null;
            if (string.IsNullOrWhiteSpace(program) && cell < _cellProgram.Length)
                program = _cellProgram[cell];
            string label = "Cell " + (cell + 1) + ": " + (string.IsNullOrWhiteSpace(program) ? "(empty)" : program);
            if (label == _tooltipCell)
                return;

            _tooltipCell = label;
            _tooltip.SetToolTip(this, label);
        }

        /// <summary>Zero-based cell under the given point, or -1 if none.</summary>
        private int HitCell(int x, int y)
        {
            if (_layout.Length == 0 || Width <= 0 || Height <= 0)
                return -1; // only the auto-mode "0" placeholder is not clickable

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
