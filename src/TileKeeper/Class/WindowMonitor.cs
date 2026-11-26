using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace TileKeeper.Class
{
    /// <summary>
    /// One monitor's tiling plan: the ordered program list, the grid shape, and the
    /// optional per-program cell slot. A slot value of -1 means "auto-fill in list
    /// order" (first free cell). Slots are parallel to <see cref="Programs"/>.
    /// </summary>
    public class ScreenAssignment
    {
        public List<string> Programs;     // Process names (without extension), list order drives auto-fill
        public List<string> Args;         // Parallel to Programs; "" = no args filter (historic name-only match)
        public List<bool> MatchLargest;   // Parallel to Programs; true = pick the largest visible top-level
                                           // window of the process instead of trusting MainWindowHandle
                                           // (guards against a floating/secondary window being moved instead
                                           // of the app's real main window). Default false = historic behavior.
        public bool AutoFit;              // True → tile; false → every window full-screen
        public bool Borderless;           // True → strip title bar/frame/border from every positioned window
        public int Rows;                  // 0 = auto rows
        public int Cols;                  // 0 = auto cols
        public int[] Slots;               // Parallel to Programs; -1 or any value ≥ grid size = auto-fill
        /// <summary>Highest pinned cell + 1. Reserves cells of unchecked rows in an auto grid.</summary>
        public int ReservedCells;
        /// <summary>
        /// Cells the user deliberately cleared. They stay in the layout but are never
        /// auto-filled, so their neighbours keep their numbers after Save/reload too.
        /// </summary>
        public int[] EmptyCells;

        public ScreenAssignment(List<string> programs, bool autoFit, int rows, int cols, int[] slots,
            List<string> args = null, List<bool> matchLargest = null, bool borderless = true,
            int reservedCells = 0, int[] emptyCells = null)
        {
            Programs      = programs ?? new List<string>();
            Args          = args ?? new List<string>();
            MatchLargest  = matchLargest ?? new List<bool>();
            AutoFit       = autoFit;
            Borderless    = borderless;
            Rows          = rows;
            Cols          = cols;
            Slots         = slots;
            ReservedCells = reservedCells;
            EmptyCells    = emptyCells;
        }
    }

    /// <summary>
    /// Periodically checks whether configured programs are displayed on the correct
    /// monitor and in the correct screen section, and repositions them if not.
    ///
    /// Layout is a set of fractional tiles (no empty cells — partial rows stretch).
    /// Auto allows 0 rows/cols and picks a column-priority shape per program count.
    /// With explicit slots, each program can be pinned to a specific tile instead of
    /// flowing in list order; unassigned programs auto-fill the first free tile in
    /// list order.
    /// </summary>
    public class WindowMonitor
    {
        // ── ShowWindow command constants ──────────────────────────────────────
        private const int SW_SHOW    = 5; // Show window at its current size and position
        private const int SW_RESTORE = 9; // Restore from minimized/maximized to normal state

        // ── GetWindowLong / SetWindowLong index ───────────────────────────────
        private const int GWL_STYLE   = -16; // Retrieves/sets the window style flags
        private const int GWL_EXSTYLE = -20; // Retrieves/sets the extended window style flags

        // ── Window style bit flags ────────────────────────────────────────────
        private const uint WS_BORDER      = 0x00800000; // Thin border
        private const uint WS_CAPTION     = 0x00C00000; // Title bar (includes WS_BORDER)
        private const uint WS_THICKFRAME  = 0x00040000; // Resizable border
        private const uint WS_MAXIMIZEBOX = 0x00010000; // Maximize button in title bar

        // ── Extended window style bit flags ──────────────────────────────────
        private const uint WS_EX_CLIENTEDGE = 0x00000200; // Etched 3-D edge visible on all sides

        // ── DWM (dwmapi) non-client rendering policy ──────────────────────────
        private const int DWMWA_NCRENDERING_POLICY = 2;  // Whether DWM renders the window frame
        private const int DWMNCRP_DISABLED         = 1;  // No DWM frame painting (no drop shadow)
        private const int DWMNCRP_ENABLED          = 2;  // Normal DWM frame painting

        // ── SetWindowPos flags ────────────────────────────────────────────────
        private const uint SWP_NOSIZE       = 0x0001;
        private const uint SWP_NOMOVE       = 0x0002;
        private const uint SWP_NOZORDER     = 0x0004; // Do not change the Z-order
        private const uint SWP_NOACTIVATE   = 0x0010;
        private const uint SWP_FRAMECHANGED = 0x0020; // Force a non-client recalculation (title bar, borders)
        private const uint SWP_SHOWWINDOW   = 0x0040; // Show the window as part of the move

        // ── RedrawWindow flags ─────────────────────────────────────────────────
        private const uint RDW_INVALIDATE    = 0x0001; // Invalidate the window
        private const uint RDW_ERASE         = 0x0004; // Erase the background
        private const uint RDW_ALLCHILDREN   = 0x0080; // Include child windows
        private const uint RDW_UPDATENOW     = 0x0100; // Redraw synchronously
        private const uint RDW_FRAME         = 0x0400; // Invalidate the frame (non-client) area

        private readonly IWin32Api _api;

        /// <summary>
        /// Handles whose chrome this instance stripped, mapped to the exact style bits removed.
        /// Restoring re-adds precisely those bits (never invents a frame a window never had).
        /// Single background thread (the position keeper) owns all access.
        /// </summary>
        private readonly Dictionary<IntPtr, uint> _stripped = new Dictionary<IntPtr, uint>();

        /// <summary>Production constructor — uses real Win32 calls.</summary>
        public WindowMonitor() : this(new Win32ApiImpl()) { }

        /// <summary>
        /// Testable constructor — accepts a mock <see cref="IWin32Api"/> so unit tests
        /// can verify call sequences without touching actual windows.
        /// </summary>
        public WindowMonitor(IWin32Api api)
        {
            _api = api;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Public entry point — called on the background position-keeper thread
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Scans all running processes, finds those matching the configured program lists,
        /// and moves any window that is not already in its designated screen cell.
        /// Silently swallows exceptions so the background thread never crashes the app.
        /// </summary>
        /// <param name="screen1">Tiling plan for the primary screen.</param>
        /// <param name="screen2">Tiling plan for the secondary screen.</param>
        public void ManageProgramScreenPosition(ScreenAssignment screen1, ScreenAssignment screen2)
            => ManageProgramScreenPosition(new[] { screen1, screen2 });

        /// <summary>
        /// Scans all running processes, finds those matching the configured program lists,
        /// and moves any window that is not already in its designated screen cell.
        /// The number of screens actually managed is capped at the number of detected monitors.
        /// Silently swallows exceptions so the background thread never crashes the app.
        /// </summary>
        /// <param name="assignments">Tiling plan per screen, in monitor order.</param>
        public void ManageProgramScreenPosition(ScreenAssignment[] assignments)
        {
            try
            {
                Screen[] screens = Screen.AllScreens;

                // A screen section is active only when its physical monitor is actually
                // detected. assignments[s] binds to the s-th detected monitor; any section
                // past the detected count is skipped silently (nothing leaks into leftover
                // cells, no windows touched, no scan log spam).
                bool[] detected = new bool[assignments.Length];
                for (int d = 0; d < detected.Length; d++)
                    detected[d] = d < screens.Length;

                // Build a de-duplicated, case-insensitive set for fast process-name matching.
                // (Was: a lowercased string array, which allocated a copy of every configured
                // name and of every running process name on each tick.)
                int n = Math.Min(assignments.Length, screens.Length);
                var allPrograms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var a in assignments)
                {
                    if (a.Programs == null) continue;
                    foreach (var p in a.Programs)
                        if (!string.IsNullOrWhiteSpace(p))
                            allPrograms.Add(p);
                }

                if (allPrograms.Count == 0)
                    return; // nothing configured to track this tick — skip the full process enumeration

                // Only enumerate processes we actually care about
                var processes = Process.GetProcesses()
                                       .Where(p => allPrograms.Contains(p.ProcessName))
                                       .ToArray();

                // Resolve the layout and the per-program cell map for each screen once
                var layout = new CellRect[n][];
                var cells = new int[n][];
                for (int s = 0; s < n; s++)
                    if (detected[s])
                        ResolveLayout(assignments[s].Programs, assignments[s].Rows, assignments[s].Cols,
                                      assignments[s].Slots, out layout[s], out cells[s],
                                      assignments[s].ReservedCells, assignments[s].EmptyCells);

                // One EnumWindows pass only when any configured program opts into MatchLargest
                // (the common case — nothing opts in — needs no top-level enumeration at all;
                // MainWindowHandle is O(1)). Previously this enumerated the top-level window
                // list once per MatchLargest process on every tick.
                bool anyMatchLargest = false;
                for (int s = 0; s < n && !anyMatchLargest; s++)
                    if (assignments[s].MatchLargest != null)
                        anyMatchLargest = assignments[s].MatchLargest.Any(v => v);
                Dictionary<int, List<IntPtr>> windowsByPid = anyMatchLargest ? BuildWindowsByPid() : null;

                // Forget stripped handles whose windows no longer exist so the map never leaks
                if (_stripped.Count > 0)
                {
                    var dead = _stripped.Keys.Where(h => !_api.IsWindow(h)).ToList();
                    foreach (IntPtr h in dead)
                        _stripped.Remove(h);
                }

                // Collect handles that need to be moved; resolved in one pass after scanning
                var screenHandles = new List<(IntPtr Handle, int Cell)>[n];
                // Border mode: every matched window (not just out-of-place ones) is re-asserted to
                // its exact tile every tick, because some apps re-size themselves after we fix them
                // and stay centered — a center-only check would never flag that shortfall again.
                var matchedOnScreen = new List<(IntPtr Handle, int Cell)>[n];
                for (int s = 0; s < n; s++)
                {
                    screenHandles[s] = new List<(IntPtr Handle, int Cell)>();
                    matchedOnScreen[s] = new List<(IntPtr Handle, int Cell)>();
                }
                bool[] styleChangedOnScreen = new bool[n];

                foreach (var process in processes)
                {
                    try
                    {
                        // A program can opt into "match largest visible window" (MatchLargest)
                        // to avoid a floating/secondary top-level window of the same process
                        // being mistaken for the app's real main window by MainWindowHandle.
                        bool matchLargest = false;
                        for (int ts = 0; ts < n && !matchLargest; ts++)
                        {
                            int tidx = IndexOf(assignments[ts].Programs, process.ProcessName);
                            if (tidx >= 0 && assignments[ts].MatchLargest != null && tidx < assignments[ts].MatchLargest.Count)
                                matchLargest = assignments[ts].MatchLargest[tidx];
                        }
                        IntPtr hWnd = matchLargest ? ResolveLargestVisibleWindow(process.Id, windowsByPid) : process.MainWindowHandle;

                        // Skip processes with no visible main window (e.g. background services)
                        if (hWnd == IntPtr.Zero || (!_api.IsWindowVisible(hWnd) && !_api.IsIconic(hWnd)))
                            continue;

                        _api.GetWindowRect(hWnd, out RECT rect);

                        // Assign to the first screen whose configured list contains the process
                        string cmdLineCache = null;
                        bool cmdLineFetched = false;
                        for (int s = 0; s < n; s++)
                        {
                            if (!detected[s]) continue;

                            // A screen's Programs list can contain the same exe more than once
                            // (two shortcuts, different args) — try every occurrence on this
                            // screen before giving up on it and moving to the next screen.
                            List<int> idxs = AllIndexesOf(assignments[s].Programs, process.ProcessName);
                            if (idxs.Count == 0) continue;

                            int matchedIdx = -1;
                            bool matchedArgsMatch = false;
                            foreach (int idx in idxs)
                            {
                                // A process only counts as this program when the captured command
                                // line matches the configured args. Empty args short-circuit to the
                                // historic name-only path; a null WMI capture (unavailable) falls
                                // back to name-only too — never a hard block.
                                string baseArgs = assignments[s].Args != null && idx < assignments[s].Args.Count
                                    ? assignments[s].Args[idx]
                                    : "";
                                bool argsMatch = true;
                                if (!string.IsNullOrEmpty(baseArgs))
                                {
                                    if (!cmdLineFetched)
                                    {
                                        cmdLineCache = _api.GetProcessCommandLine(process.Id);
                                        cmdLineFetched = true;
                                    }
                                    argsMatch = string.IsNullOrWhiteSpace(cmdLineCache)
                                        ? true // WMI unavailable — fall back to name-only match
                                        : CommandLineMatchesArgs(cmdLineCache, baseArgs);
                                }

                                matchedIdx = idx;
                                matchedArgsMatch = argsMatch;
                                if (argsMatch)
                                    break;
                            }

                            if (!matchedArgsMatch)
                                continue; // try the next screen; if none qualifies the process is left alone

                            // Window belongs on this screen — check if it is already there
                            int cell = cells[s][matchedIdx];

                            // Borderless switched off for this screen: give every tracked window
                            // a title bar with a thin border but NO WS_THICKFRAME (whose invisible
                            // resize border would leave a wide seam between adjacent tiles).
                            // Applied here — before the in-place short-circuit below — so an
                            // "uncheck" restyles windows already sitting in their correct cell on
                            // the next tick, no app restart needed.
                            bool styleChanged = false;
                            if (!assignments[s].Borderless)
                                styleChanged = EnsureThinFrame(hWnd);
                            // Borderless switched on: strip chrome from windows already sitting
                            // in their correct cell too (the move path below only sees windows
                            // that need repositioning, so without this an in-place window would
                            // keep its frame). Idempotent via the _stripped map.
                            else if (!_stripped.ContainsKey(hWnd))
                                styleChanged = StripFrame(hWnd);

                            // A frame change makes apps resize their own outer rect (they keep
                            // the client area when WS_CAPTION flips), so the window can drift
                            // short of its tile. Push every style-changed window through the move
                            // batch to re-assert the exact tile rect once (in addition to any
                            // genuinely out-of-place windows). Later ticks see the exact rect and
                            // skip, so this is one-time per toggle.
                            if (styleChanged)
                                styleChangedOnScreen[s] = true;

                            if (styleChanged
                                || !IsWindowInCorrectSection(rect, screens[s].WorkingArea, layout[s], cell, assignments[s].AutoFit))
                                screenHandles[s].Add((hWnd, cell));

                            // Border mode keeps a full list of every matched window so its exact
                            // tile can be re-asserted on every tick (settle, below) even when the
                            // window's center still looks right after the app shrank itself.
                            if (!assignments[s].Borderless)
                                matchedOnScreen[s].Add((hWnd, cell));
                            break;
                        }
                    }
                    catch
                    {
                        // Skip individual processes that fail (e.g. exited mid-enumeration)
                        continue;
                    }
                }

                // Move all out-of-place windows in one batch. On a tick where a Borderless
                // toggle changed chrome, keep the relocation quiet (no SetForegroundWindow) so
                // the apps don't steal focus from PT while the tile re-asserts itself.
                for (int s = 0; s < n; s++)
                {
                    if (!detected[s]) continue;

                    PositionWindowsOnScreen(screenHandles[s], screens[s].WorkingArea, assignments[s].AutoFit, layout[s], assignments[s].Borderless, !styleChangedOnScreen[s]);

                    // Border mode: some apps re-size themselves after the move to fit their own
                    // content (staying centered), leaving a desktop gap around them. Re-assert the
                    // exact tile rect for every matched window on every tick — without
                    // SWP_FRAMECHANGED (avoids the grow-fight) and without focus steal. The first
                    // GetWindowRect is cheap and bails the moment every window is flush. On a tick
                    // right after a Borderless toggle the apps race to keep their old client size,
                    // so that screen's settle gets the aggressive pass (more attempts, FRAMECHANGED
                    // on oversized windows) to win the race and end up flush, not past the tile.
                    if (!assignments[s].Borderless)
                        SettleWindowTiles(matchedOnScreen[s], screens[s].WorkingArea, assignments[s].AutoFit, layout[s], styleChangedOnScreen[s]);
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"ManageProgramScreenPosition error: {ex}");
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Layout helpers
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>A rectangular tile in a screen layout; fractions of the working area (0..1).</summary>
        internal struct CellRect
        {
            public double X, Y, W, H;

            public CellRect(double x, double y, double w, double h)
            {
                X = x; Y = y; W = w; H = h;
            }
        }

        /// <summary>Builds a single row of <paramref name="count"/> equal cells at height fraction <paramref name="h"/>.</summary>
        private static CellRect[] MakeRow(double y, double h, int count)
        {
            if (count <= 0) return new CellRect[0];
            var row = new CellRect[count];
            double w = 1.0 / count;
            for (int j = 0; j < count; j++)
                row[j] = new CellRect(j * w, y, w, h);
            return row;
        }

        /// <summary>
        /// Splits <paramref name="n"/> programs into a column-priority tile layout:
        /// rows filled top-down with <paramref name="c"/> columns each, and the final
        /// partial row's cells stretched to share the full width so nothing is empty.
        /// Heights use only the rows actually needed.
        /// </summary>
        private static CellRect[] SplitCells(int n, int r, int c)
        {
            c = Math.Max(1, c);
            int usedRows = Math.Min(r, (int)Math.Ceiling(n / (double)c));
            var cells = new List<CellRect>(n);
            double ch = 1.0 / usedRows;
            for (int i = 0; i < usedRows; i++)
            {
                int m = Math.Min(c, n - i * c);
                if (m <= 0) break;
                cells.AddRange(MakeRow(i * ch, ch, m));
            }
            return cells.ToArray();
        }

        /// <summary>
        /// Computes the tile layout for <paramref name="n"/> programs.
        /// Explicit <paramref name="rows"/>/<paramref name="cols"/> (when given) fill the
        /// grid top-down and stretch the last partial row; 0,0 selects the auto shapes:
        /// <list type="bullet">
        /// <item>1 → full cell · 2 → 1×2 halves · 3 → wide top, 2 below</item>
        /// <item>4 → 2×2 · 5 → 3 top / 2 bottom · 6 → 2×3</item>
        /// <item>7 → 4/3 · 8 → 4/4 · 9+ → nearest-square with stretched last row</item>
        /// </list>
        /// </summary>
        internal static CellRect[] ComputeLayout(int n, int rows, int cols)
        {
            n = Math.Max(1, n);

            if (rows == 0 && cols == 0)
            {
                switch (n)
                {
                    case 1: return MakeRow(0, 1, 1);
                    case 2: return MakeRow(0, 1, 2);
                    case 3: return new[]
                    {
                        new CellRect(0.0, 0.0, 1.0, 0.5), // wide top
                        new CellRect(0.0, 0.5, 0.5, 0.5),
                        new CellRect(0.5, 0.5, 0.5, 0.5)
                    };
                    case 4: return SplitCells(n, 2, 2);
                    case 5: return SplitCells(n, 2, 3);
                    case 6: return SplitCells(n, 2, 3);
                    case 7: return SplitCells(n, 2, 4);
                    case 8: return SplitCells(n, 2, 4);
                    default:
                        int c = (int)Math.Ceiling(Math.Sqrt(n));
                        return SplitCells(n, c, c);
                }
            }

            if (rows > 0 && cols > 0)
                return SplitCells(n, Math.Min(rows, n), Math.Min(cols, n));

            if (cols > 0)
            {
                int c = Math.Min(cols, n);
                return SplitCells(n, (int)Math.Ceiling(n / (double)c), c);   // rows inferred
            }

            if (rows > 0)
            {
                int r = Math.Min(rows, n);
                return SplitCells(n, r, (int)Math.Ceiling(n / (double)r));   // cols inferred
            }

            return SplitCells(n, n, 1);
        }

        /// <summary>
        /// Resolves the final tile index for every program on a screen.
        ///
        /// Programs with a valid explicit slot (0..layout size-1) are pinned to that tile;
        /// if two programs claim the same tile, the first keeps it and the later one is
        /// auto-filled. All remaining (slot -1/out-of-range) programs auto-fill the first
        /// free tile in list order. If the layout is too small for the program count the
        /// leftovers wrap with modulo so nothing crashes. With an explicit rows×cols grid,
        /// the layout is padded to the full cell count so unassigned cells stay empty instead
        /// of letting a mapped program stretch into them.
        /// </summary>
        internal static void ResolveLayout(List<string> programs, int rows, int cols, int[] slots,
                                           out CellRect[] layout, out int[] cells, int reservedCells = 0,
                                           ICollection<int> emptyCells = null)
        {
            int n = programs != null ? programs.Count : 0;

            // An explicit grid reserves every cell, not just the ones with a program. Without
            // this, 2x2 with one mapped app collapsed to one full-screen tile and stretched the
            // app into the three unassigned areas. Padding with empty entries keeps each pinned
            // app in its own cell and leaves unassigned cells genuinely empty (gray in the
            // preview, untouched at runtime). An auto grid (0×0) reserves the highest pinned
            // cell too, so unchecking a row cannot collapse the shape into a smaller one.
            int fixedCells = rows > 0 && cols > 0 ? rows * cols : Math.Max(0, reservedCells);
            int count = Math.Max(n, fixedCells);

            layout = ComputeLayout(count, rows, cols);
            int total = layout.Length;
            cells = new int[count];
            // Padding entries (an explicit grid has more cells than mapped programs) must start
            // unassigned. Default 0 made them look like a program already pinned to cell 1, so
            // the auto-fill pass skipped them and the real gap never got filled.
            for (int i = 0; i < count; i++)
                cells[i] = -1;
            if (count == 0) return;

            var occupied = new bool[total];
            // A cell cleared by "Clear this cell (leave empty)" must stay empty even after a
            // Save/reload: mark it occupied up front so the auto-fill pass cannot hand it to a
            // different program. Explicit smaller grids simply ignore out-of-range indexes.
            var blocked = new bool[total];
            if (emptyCells != null)
                foreach (int cell in emptyCells)
                    if (cell >= 0 && cell < total)
                    {
                        blocked[cell] = true;
                        occupied[cell] = true;
                    }

            for (int i = 0; i < n; i++)
            {
                int slot = (slots != null && i < slots.Length) ? slots[i] : -1;
                if (total > 0 && slot >= 0 && slot < total && !occupied[slot])
                {
                    cells[i] = slot;
                    occupied[slot] = true;
                }
                else
                {
                    cells[i] = -1;
                }
            }

            int next = 0;
            for (int i = 0; i < count; i++)
            {
                if (cells[i] >= 0) continue;

                while (next < total && occupied[next]) next++;
                if (next >= total)
                {
                    // More programs than free cells: wrap (old modulo behaviour), but never
                    // wrap into a cell the user deliberately left empty.
                    int wrapped = total > 0 ? i % total : 0;
                    for (int step = 0; step < total && blocked[wrapped]; step++)
                        wrapped = (wrapped + 1) % total;
                    cells[i] = wrapped;
                    occupied[wrapped] = true;
                    continue;
                }

                cells[i] = next;
                occupied[next] = true;
            }
        }

        /// <summary>Converts a fractional <paramref name="cell"/> into pixel coordinates inside <paramref name="area"/>.</summary>
        private static Rectangle CellRectToRectangle(CellRect cell, Rectangle area)
        {
            int left   = area.Left + (int)Math.Round(cell.X * area.Width);
            int top    = area.Top + (int)Math.Round(cell.Y * area.Height);
            int width  = (int)Math.Round(cell.W * area.Width);
            int height = (int)Math.Round(cell.H * area.Height);
            return new Rectangle(left, top, width, height);
        }

        /// <summary>
        /// Determines whether a window's current on-screen center matches the expected
        /// center of tile <paramref name="cell"/> in the layout.
        ///
        /// The check is center-point based: the midpoint of the window's bounding rect
        /// is compared to the midpoint of its target tile. This avoids false positives
        /// when the window has a non-standard size but is still correctly placed.
        /// </summary>
        internal static bool IsWindowInCorrectSection(RECT rect, Rectangle workingArea, CellRect[] layout, int cell, bool autoFit)
        {
            int cx = (rect.Left + rect.Right)  / 2;
            int cy = (rect.Top  + rect.Bottom) / 2;

            // autoFit=false (or an empty layout) means every program fills the whole screen
            if (!autoFit || layout == null || layout.Length == 0)
            {
                int areaCx = workingArea.Left + workingArea.Width  / 2;
                int areaCy = workingArea.Top  + workingArea.Height / 2;
                return cx == areaCx && cy == areaCy;
            }

            if (cell < 0) cell = 0;
            if (cell >= layout.Length) cell = cell % layout.Length;

            Rectangle target = CellRectToRectangle(layout[cell], workingArea);
            return cx == target.Left + target.Width / 2 && cy == target.Top + target.Height / 2;
        }

        /// <summary>
        /// True when <paramref name="cmdLine"/> (null-safe) equals — or ends with — the configured
        /// <paramref name="args"/> (case-insensitive, trimmed). "Ends-with" is what distinguishes
        /// same-exe-different-args programs: each instance is only matched by its own arg tail.
        /// Null capture (WMI unavailable) returns false here; the caller treats that as fallback.
        /// </summary>
        internal static bool CommandLineMatchesArgs(string cmdLine, string args)
        {
            if (cmdLine == null) return false;
            string c = cmdLine.Trim();
            return string.Equals(c, args, StringComparison.OrdinalIgnoreCase)
                || c.EndsWith(args, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Picks the largest visible top-level window of a process, regardless of class. Used when
        /// a program entry opts into <see cref="ScreenAssignment.MatchLargest"/> to avoid a
        /// floating/secondary top-level window being mistaken for the app's real main window
        /// (<see cref="Process.MainWindowHandle"/> can return either one for multi-window apps).
        /// </summary>
        internal IntPtr ResolveLargestVisibleWindow(int processId)
            => PickLargestVisible(_api.GetProcessWindowHandles(processId));

        /// <summary>
        /// Bucket variant that avoids a per-pid EnumWindows pass: <paramref name="windowsByPid"/>
        /// is built once per monitor tick by <see cref="ManageProgramScreenPosition"/>. Falls back
        /// to the per-pid enumeration when the caller has no index or the pid has no bucket entry.
        /// </summary>
        internal IntPtr ResolveLargestVisibleWindow(int processId, Dictionary<int, List<IntPtr>> windowsByPid)
        {
            if (windowsByPid == null || !windowsByPid.TryGetValue(processId, out List<IntPtr> handles))
                return ResolveLargestVisibleWindow(processId);
            return PickLargestVisible(handles);
        }

        private IntPtr PickLargestVisible(List<IntPtr> handles)
        {
            IntPtr best = IntPtr.Zero;
            long bestArea = 0;
            foreach (IntPtr h in handles)
            {
                if (!_api.IsWindowVisible(h) && !_api.IsIconic(h))
                    continue;
                _api.GetWindowRect(h, out RECT r);
                long area = (long)(r.Right - r.Left) * (r.Bottom - r.Top);
                if (area > bestArea)
                {
                    bestArea = area;
                    best = h;
                }
            }
            return best;
        }

        /// <summary>Buckets one <see cref="IWin32Api.EnumerateTopLevelWindows"/> pass by owning pid.</summary>
        private Dictionary<int, List<IntPtr>> BuildWindowsByPid()
        {
            var map = new Dictionary<int, List<IntPtr>>();
            foreach (var (handle, pid) in _api.EnumerateTopLevelWindows())
            {
                if (!map.TryGetValue(pid, out List<IntPtr> list))
                    map[pid] = list = new List<IntPtr>();
                list.Add(handle);
            }
            return map;
        }

        /// <summary>
        /// Iterates the list of out-of-place window handles and calls <see cref="MoveWindowToArea"/>
        /// with the correct size and offset for each handle's assigned tile.
        /// </summary>
        /// <param name="handles">Windows to reposition, paired with their resolved tile index.</param>
        /// <param name="workingArea">Target monitor's working area.</param>
        /// <param name="autoFit">When false, all windows are stretched to fill the full screen.</param>
        /// <param name="layout">The tile layout for this screen (drives tile sizes).</param>
        /// <param name="borderless">When true, strip chrome from positioned windows (see <see cref="MoveWindowToArea"/>).</param>
        /// <param name="bringToFront">When false, the repositioned windows are not raised (used on tint toggle ticks).</param>
        internal void PositionWindowsOnScreen(List<(IntPtr Handle, int Cell)> handles, Rectangle workingArea, bool autoFit, CellRect[] layout, bool borderless, bool bringToFront = true)
        {
            if (handles.Count == 0)
                return;

            if (!autoFit || layout == null || layout.Length <= 1)
            {
                foreach (var (handle, _) in handles)
                    MoveWindowToArea(handle, workingArea, workingArea.Width, workingArea.Height, 0, 0, borderless, bringToFront);
                return;
            }

            foreach (var (handle, cell) in handles)
            {
                int pos = cell;
                if (pos < 0) pos = 0;
                if (pos >= layout.Length) pos = pos % layout.Length;

                Rectangle target = CellRectToRectangle(layout[pos], workingArea);
                MoveWindowToArea(handle, workingArea, target.Width, target.Height, target.Left - workingArea.Left, target.Top - workingArea.Top, borderless, bringToFront);
            }
        }

        /// <summary>
        /// Re-asserts every <paramref name="handles"/> window to its exact tile rect after a
        /// Borderless style change. Apps whose frame style just flipped often resize their own
        /// window afterward — to keep the old client size — which makes it protrude past the
        /// tile bottom. A plain SetWindowPos (no SWP_FRAMECHANGED) lands after that adjustment
        /// and sticks. Up to three attempts before giving up (rather than fighting forever).
        /// </summary>
        private void SettleWindowTiles(List<(IntPtr Handle, int Cell)> handles, Rectangle workingArea, bool autoFit, CellRect[] layout, bool aggressive)
        {
            if (handles.Count == 0)
                return;

            bool fullScreen = !autoFit || layout == null || layout.Length <= 1;
            int attempts = aggressive ? 5 : 3;
            int pauseMs = aggressive ? 200 : 150;

            foreach (var (handle, cell) in handles)
            {
                int pos = cell;
                if (pos < 0) pos = 0;
                if (pos >= layout.Length) pos = pos % layout.Length;

                Rectangle target = fullScreen
                    ? new Rectangle(workingArea.Left, workingArea.Top, workingArea.Width, workingArea.Height)
                    : CellRectToRectangle(layout[pos], workingArea);

                for (int attempt = 0; attempt < attempts; attempt++)
                {
                    _api.GetWindowRect(handle, out RECT r);
                    if (r.Right - r.Left == target.Width && r.Bottom - r.Top == target.Height)
                    {
                        if (attempt == 0)
                            break; // already flush with the tile
                        if (aggressive && attempt != attempts - 1)
                            continue; // leave time at the end so the app's last write is ours
                        break;
                    }

                    // A window protrudes past its tile (the keep-old-client re-grow) because it
                    // still computes against the pre-caption client size. SWP_FRAMECHANGED re-runs
                    // the non-client calculation, so the app adopts the new (smaller) client and
                    // stops re-growing. For a window that is only too small, skip it — the plain
                    // set below already lands and FRAMECHANGED would feed the earlier grow-fight.
                    uint flags = SWP_NOZORDER | SWP_NOACTIVATE;
                    if (aggressive && r.Bottom - r.Top > target.Height)
                        flags |= SWP_FRAMECHANGED;

                    _api.SetWindowPos(handle, IntPtr.Zero, target.Left, target.Top, target.Width, target.Height, flags);
                    Thread.Sleep(pauseMs);
                }

                if (aggressive)
                {
                    // Resize handshake: an externally-applied caption (SetWindowLong) shrinks the
                    // real client, but WPF apps hold their old client size in their own layout and
                    // keep laying content out for it — so the bottom rows render past the client
                    // and get clipped. A real WM_SIZE change (not just SWP_FRAMECHANGED on an
                    // unchanged size) makes WPF re-run Measure/Arrange against the new client and
                    // stretch the content to fit. Nudge +1px with FRAMECHANGED, then back to the
                    // exact tile, quietly, so the app's final size notification is the true tile.
                    _api.SetWindowPos(handle, IntPtr.Zero, target.Left, target.Top, target.Width + 1, target.Height + 1,
                        SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
                    Thread.Sleep(150);
                    _api.SetWindowPos(handle, IntPtr.Zero, target.Left, target.Top, target.Width, target.Height,
                        SWP_NOZORDER | SWP_NOACTIVATE);

                    _api.GetWindowRect(handle, out RECT r);
                    if (r.Right - r.Left != target.Width || r.Bottom - r.Top != target.Height)
                        Logger.Log($"border settle: window 0x{handle.ToInt64():X} still {r.Right - r.Left}x{r.Bottom - r.Top}, tile {target.Width}x{target.Height}");
                }
            }
        }

        /// <summary>
        /// Moves a single window to the specified position and size within a monitor's
        /// working area, optionally bringing it to the foreground.
        ///
        /// When <paramref name="borderless"/> is true, the title bar, frame and border
        /// styles are stripped from EVERY positioned window (regardless of whether it has a
        /// maximize button) to achieve a uniform borderless tile look. When false, any
        /// previously-stripped window tracked for this handle is re-styled with a title bar
        /// and thin border but no WS_THICKFRAME (keeps adjacent tiles close together).
        ///
        /// <para>
        /// <b>Windows 11 note:</b> Calling SW_MAXIMIZE before SetWindowPos causes the DWM
        /// to anchor the window to the current monitor. The subsequent SetWindowPos call to
        /// move it to a different screen is then silently ignored. Using SW_RESTORE instead
        /// puts the window in a normal (non-maximized) state first, so SetWindowPos can
        /// freely place it on any monitor. This was the root cause of windows failing to
        /// appear on screen 2 in Windows 11.
        /// </para>
        /// </summary>
        internal void MoveWindowToArea(IntPtr hWnd, Rectangle workingArea, int width, int height, int x = 0, int y = 0, bool borderless = true, bool bringToFront = true)
        {
            bool stripped = false;
            uint style = _api.GetWindowLong(hWnd, GWL_STYLE);

            if (borderless)
            {
                // Uniform borderless look — remove chrome from every positioned window
                uint mask = style & (WS_CAPTION | WS_THICKFRAME | WS_BORDER);
                if (mask != 0 && _api.SetWindowLong(hWnd, GWL_STYLE, style & ~mask))
                {
                    _stripped[hWnd] = mask;
                    stripped = true;
                }

                // SW_RESTORE (not SW_MAXIMIZE) — see summary above for the Windows 11 reason.
                // Also un-minimizes iconic windows, so no separate IsIconic branch is needed.
                _api.ShowWindow(hWnd, SW_RESTORE);
            }
            else if (_stripped.TryGetValue(hWnd, out _))
            {
                // Borderless was switched off — title bar only, no thick resize frame
                EnsureThinFrame(hWnd);
            }
            else if (_api.IsIconic(hWnd))
            {
                // Minimized window without tracked chrome: SW_SHOW does NOT un-minimize it, so
                // SetWindowPos below would be a silent no-op at (-32000,-32000). SW_RESTORE
                // brings it back to normal state first.
                _api.ShowWindow(hWnd, SW_RESTORE);
            }
            else
            {
                // Window has no tracked chrome and isn't minimized — just make it visible
                _api.ShowWindow(hWnd, SW_SHOW);
            }

            // Move and resize the window. workingArea.Left/Top anchor to the correct monitor.
            uint flags = SWP_NOZORDER;
            if (stripped)
                flags |= SWP_FRAMECHANGED;   // force the removed frame to redraw immediately

            bool swpOk = _api.SetWindowPos(
                hWnd,
                IntPtr.Zero,                        // hWndInsertAfter — ignored due to SWP_NOZORDER
                workingArea.Left + x,               // absolute screen X
                workingArea.Top  + y,               // absolute screen Y
                width,
                height,
                flags);                              // no SWP_SHOWWINDOW — it re-shows a window the app deliberately hides (5s taskbar blink)

            // SetWindowPos is refused for some window classes (lastError=0xB7). MoveWindow can still
            // place the same rect, so fall back to it.
            if (!swpOk)
                _api.MoveWindow(hWnd, workingArea.Left + x, workingArea.Top + y, width, height, true);

            if (bringToFront)
                _api.SetForegroundWindow(hWnd);
        }

        /// <summary>
        /// Strips the title bar/frame/border from <paramref name="hWnd"/> and records the exact
        /// removed bits so <see cref="EnsureThinFrame"/> can re-attach a caption later. Does not
        /// move or resize the window (unlike the strip branch in <see cref="MoveWindowToArea"/>),
        /// which lets the scan loop restyle windows already in their correct cell. Idempotent per
        /// handle via the <see cref="_stripped"/> map. Returns true when the style actually changed.
        /// </summary>
        private bool StripFrame(IntPtr hWnd)
        {
            uint style = _api.GetWindowLong(hWnd, GWL_STYLE);
            uint mask  = style & (WS_CAPTION | WS_THICKFRAME | WS_BORDER);
            bool changed = mask != 0;
            if (changed && _api.SetWindowLong(hWnd, GWL_STYLE, style & ~mask))
            {
                // Back to borderless — hand DWM its normal frame painting back (restores the
                // adornment we suppressed in border mode; borderless chrome stays stripped so
                // the filled tile remains gapless).
                int ncrp = DWMNCRP_ENABLED;
                _api.DwmSetWindowAttribute(hWnd, DWMWA_NCRENDERING_POLICY, ref ncrp, sizeof(int));

                _stripped[hWnd] = mask;
                _api.SetWindowPos(hWnd, IntPtr.Zero, 0, 0, 0, 0,
                    SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
                _api.RedrawWindow(hWnd, RDW_FRAME | RDW_INVALIDATE | RDW_ERASE | RDW_ALLCHILDREN | RDW_UPDATENOW);
            }
            return changed;
        }

        /// <summary>
        /// Gives <paramref name="hWnd"/> a title bar with a thin border but no resizable frame:
        /// adds <see cref="WS_CAPTION"/> when missing (it already includes WS_BORDER) and clears
        /// <see cref="WS_THICKFRAME"/> when present. Dropping the thick frame removes the invisible
        /// resize border Windows paints around resizable windows, which is what leaves a visible
        /// seam between adjacent tiled windows. Forces a non-client recalculation so the frame
        /// redraws, and forgets any recorded strip mask for the handle. Idempotent. Returns true
        /// when the style actually changed.
        /// </summary>
        private bool EnsureThinFrame(IntPtr hWnd)
        {
            uint style = _api.GetWindowLong(hWnd, GWL_STYLE);
            uint newStyle = style;
            if ((style & WS_CAPTION) == 0)
                newStyle |= WS_CAPTION;
            if ((style & WS_THICKFRAME) != 0)
                newStyle &= ~WS_THICKFRAME;
            bool changed = newStyle != style;
            // Border mode — tell DWM to skip its own frame painting (the drop shadow that
            // would otherwise bleed across into the neighboring tile) while the caption stays.
            // No WS_THICKFRAME, so no fat invisible resize-border seam between tiles either.
            int ncrp = DWMNCRP_DISABLED;
            _api.DwmSetWindowAttribute(hWnd, DWMWA_NCRENDERING_POLICY, ref ncrp, sizeof(int));

            if (changed && _api.SetWindowLong(hWnd, GWL_STYLE, newStyle))
            {
                _api.SetWindowPos(hWnd, IntPtr.Zero, 0, 0, 0, 0,
                    SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
                _api.RedrawWindow(hWnd, RDW_FRAME | RDW_INVALIDATE | RDW_ERASE | RDW_ALLCHILDREN | RDW_UPDATENOW);
            }
            _stripped.Remove(hWnd);
            return changed;
        }

        /// <summary>Case-insensitive index of <paramref name="processName"/> in <paramref name="list"/>, or -1.</summary>
        private static int IndexOf(List<string> list, string processName)
        {
            for (int i = 0; i < list.Count; i++)
                if (string.Equals(list[i], processName, StringComparison.OrdinalIgnoreCase))
                    return i;
            return -1;
        }

        /// <summary>
        /// Case-insensitive indexes of every occurrence of <paramref name="processName"/> in
        /// <paramref name="list"/>, in list order. Two Programs-tab entries for the same exe with
        /// different args produce two entries here; the caller tries each in turn.
        /// </summary>
        private static List<int> AllIndexesOf(List<string> list, string processName)
        {
            var result = new List<int>();
            for (int i = 0; i < list.Count; i++)
                if (string.Equals(list[i], processName, StringComparison.OrdinalIgnoreCase))
                    result.Add(i);
            return result;
        }
    }
}