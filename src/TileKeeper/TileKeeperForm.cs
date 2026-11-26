using IWshRuntimeLibrary;
using Microsoft.Win32;
using TileKeeper.Class;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Deployment.Application;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Forms.VisualStyles;

namespace TileKeeper
{
    /// <summary>
    /// Main application form. Runs hidden in the system tray and manages concurrent
    /// background behaviours: process persistence, screen-position keeping (with an
    /// optional per-screen tiling grid and manual cell slots), scheduled RDP
    /// re-enablement, and display-resolution hold.
    ///
    /// The form itself is never shown unless the user opens it from the tray icon.
    /// Closing the window minimises it back to the tray; Exit quits immediately
    /// (this build has no exit password).
    /// </summary>
    public partial class TileKeeperForm : Form
    {
        private const int MAX_GRID_DIM = 3; // Hard cap: 3×3 = 9 cells per screen
        private const int WM_SETREDRAW = 0x000B;

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        // ── Tray icon ─────────────────────────────────────────────────────────
        private readonly NotifyIcon      trayIcon;
        private readonly ContextMenuStrip trayMenu;

        // ── Timers (in milliseconds, set from config and used by background threads) ──
        private int monitorTimer;       // How often to check if programs are still running
        private int keepScreenPosTimer; // How often to check and fix window positions

        // ── State ─────────────────────────────────────────────────────────────
        /// <summary>
        /// Designer size captured once before the first minimise. Reading Size after a tray
        /// Show/minimise cycle sampled the transient collapsed rect and permanently shrank
        /// the form to a title bar, so this value is never overwritten.
        /// </summary>
        private Size _restoreSize;
        private bool running = true; // Set to false on exit to stop all background threads cleanly
        private bool _restoringFromTray; // Suppresses OnResize's hide while OnShow restores the window

        /// <summary>
        /// Live snapshot of the Keep Apps Running rows, published by the UI thread after every
        /// edit and read by the relaunch loop. The Enable checkbox therefore takes effect on the
        /// next cycle without Save — the list on screen is what actually runs.
        /// </summary>
        private List<ProgramEntry> _livePrograms;

        /// <summary>Live snapshot of both lists combined (identity keys resolved), used by the position keeper.</summary>
        private List<ProgramEntry> _livePositionable;

        /// <summary>
        /// Persistent identity catalog. Screen assignments, slot pins and Remove address rows by
        /// <see cref="ProgramEntry.Id"/>; names and legacy keys are display/resolver concerns only.
        /// </summary>
        private readonly ProgramCatalog _catalog = new ProgramCatalog();

        /// <summary>Id → human-readable label used by screen checklists and the cell picker.</summary>
        private Dictionary<string, string> _idDisplay =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Cached monitor list. Screen.AllScreens queries the OS on every call and the position
        /// keeper reads it on every cycle; refresh only when Windows reports a display change.
        /// </summary>
        private Screen[] _screens = Screen.AllScreens ?? new Screen[0];

        /// <summary>
        /// Immutable snapshot of the Arrange tab published by the UI thread. The position loop
        /// uses it instead of re-reading app.config, so unchecking a screen's row (or its Enable
        /// box) takes effect on the next pass without Save. Null until the form has built its UI.
        /// </summary>
        private sealed class ScreenPlan
        {
            public readonly List<string>[] Lists = new List<string>[4];
            public readonly int[][] Slots = new int[4][];
            public readonly bool[] AutoFit = new bool[4];
            public readonly bool[] Borderless = new bool[4];
            public readonly int[] Rows = new int[4];
            public readonly int[] Cols = new int[4];
            /// <summary>
            /// Highest pinned cell + 1, including pins of unchecked rows. Without this an auto
            /// grid (0×0) would reflow to a smaller shape the moment a row is unchecked.
            /// </summary>
            public readonly int[] ReservedCells = new int[4];
            /// <summary>Cells cleared by the user; the resolver must never auto-fill them.</summary>
            public readonly int[][] EmptyCells = new int[4][];
        }
        private ScreenPlan _liveScreenPlan;

        /// <summary>Nesting depth for WM_SETREDRAW suppression (innermost must not re-enable early).</summary>
        private int _redrawGuard;

        /// <summary>
        /// Last-write stamp of the exe.config captured after a load/save. OnShow only rebuilds the
        /// form when the file changed underneath us (external edit); an ordinary hide/show keeps
        /// unsaved rows and toggles intact and paints in one pass.
        /// </summary>
        private DateTime _configStampUtc = DateTime.MinValue;

        /// <summary>
        /// Canonical snapshot of every form value written by Save. Compared on X/minimise so
        /// an in-memory edit (checklist, pin, text field, toggle) can warn before the form is
        /// hidden, without wiring dozens of control events or firing on programmatic refreshes.
        /// </summary>
        private string _savedConfigSnapshot = "";

        /// <summary>
        /// Manual grid-cell pins, keyed by process name (case-insensitive).
        /// Value = zero-based cell index; absence or -1 = auto-fill in list order.
        /// </summary>
        private readonly Dictionary<string, int> _screenSlot1 = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _screenSlot2 = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _screenSlot3 = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _screenSlot4 = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Cells the user deliberately emptied via "Clear this cell (leave empty)". Separate from
        /// pins: an empty cell has no program but must still exist after Save/reload, otherwise
        /// the matrix reflows (2×2 → 2×1/1×2). Keyed by screen index, zero-based cell indexes.
        /// </summary>
        private readonly HashSet<int>[] _screenReservedCells =
        {
            new HashSet<int>(), new HashSet<int>(), new HashSet<int>(), new HashSet<int>()
        };

        private readonly IWin32Api _win32Api = new Win32ApiImpl(); // reused by the watchdog for args-aware "is running" checks

        /// <summary>
        /// Watchdog baseline, keyed by "exe|args": the pid and start time of the process
        /// TileKeeper observed (or started) for a target. It keeps a target that was already
        /// running before TileKeeper started from being started again, and lets the next
        /// cycles reuse the identity instead of re-scanning every time.
        /// Guarded by <see cref="_targetsGate"/>.
        /// </summary>
        private readonly Dictionary<string, TrackedProcess> _trackedTargets =
            new Dictionary<string, TrackedProcess>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Process identity (pid + start time) adopted by the watchdog.</summary>
        private struct TrackedProcess
        {
            public int Pid;
            public long StartTicksUtc; // 0 = start time unavailable (access denied)
        }

        /// <summary>
        /// Earliest time a relaunch for a key may be attempted. A newly started app that has
        /// not produced a matching process yet used to be relaunched on the very next cycle;
        /// this window lets it finish booting. Guarded by <see cref="_targetsGate"/>.
        /// </summary>
        private readonly Dictionary<string, DateTime> _launchNotBeforeUtc =
            new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        private readonly object _targetsGate = new object();

        private bool _suppressingDedup;

        /// <summary>
        /// True while a list is being cleared/repopulated. The native ListView raises
        /// ItemChecked from inside Items.Clear()/Items.Add(); re-entering RefreshLiveSnapshot
        /// there can read half-built rows. The caller publishes one complete snapshot at end.
        /// </summary>
        private bool _suppressLiveSnapshotRefresh;

        /// <summary>
        /// The only user gesture allowed to change checkboxes in the Programs/Position
        /// ListViews is a direct click on the checkbox glyph (StateImage). Any other row
        /// interaction must not affect checkbox state.
        /// </summary>
        private ListView _checkboxClickAuthorizedList;
        private bool _allowProgramListItemCheck;
// Per-screen (index-aligned with _screenChecklists): process names grayed out there.
        // Rebuilt wholesale by RecomputeDedupGrayed; never mutated incrementally.
        private readonly HashSet<string>[] _dedupGrayed;
        private System.Windows.Forms.CheckedListBox[] _screenChecklists;
        private CheckBox[] _autoFitChecks;
        private CheckBox[] _borderlessChecks;
        private TextBox[] _gridRowsFields;
        private TextBox[] _gridColsFields;
        /// <summary>The four +/- steppers per screen, index-aligned with the grid fields.</summary>
        private Button[][] _gridSpinButtons;

        /// <summary>
        /// Per-screen map of program name → 1-based cell number, refreshed whenever the
        /// grid model is rebuilt. Drives the colour/number badge drawn on each checklist
        /// row so it matches the corresponding <see cref="Class.GridMapPanel"/> tile.
        /// Programs with no cell (grid too small) are absent.
        /// </summary>
        private readonly Dictionary<CheckedListBox, Dictionary<string, int>> _screenRowCell =
            new Dictionary<CheckedListBox, Dictionary<string, int>>();

        /// <summary>
        /// Per-screen ids that actually claim a cell (a disabled Arrange row is excluded even
        /// though it stays checked). Used by the Format fallback so a disabled row never shows
        /// a "(N)" badge for a cell the grid draws empty.
        /// </summary>
        private readonly Dictionary<CheckedListBox, HashSet<string>> _screenActiveIds =
            new Dictionary<CheckedListBox, HashSet<string>>();

        /// <summary>
        /// Per-screen list of program names in the order the user checked them (click order).
        /// Drives auto-fill cell assignment so the "(N)" badge sequences by click order rather
        /// than by list position. Slot pins (explicit cells) stay authoritative — click order
        /// only governs auto-fill (<c>slot -1</c>). Kept in sync by <see cref="ScreenItemCheck"/>.
        /// </summary>
        private readonly Dictionary<CheckedListBox, List<string>> _screenCheckOrder =
            new Dictionary<CheckedListBox, List<string>>();

        /// <summary>
        // ── Background threads ────────────────────────────────────────────────
        private Task _keepRun;       // Process persistence loop
        private Thread _keepPosition;  // Screen-position correction loop
        private Thread _displayRes;    // Display-resolution hold loop

        // Reused across position cycles instead of allocating a WindowMonitor per tick
        private readonly WindowMonitor _windowMonitor = new WindowMonitor();

        // Wakes the position-keeper loop immediately (e.g. a Borderless toggle) instead of
        // waiting out the current cycle. Set on exit too so the keeper thread stops promptly.
        private readonly AutoResetEvent _positionWake = new AutoResetEvent(false);

        // ── Update section (Display & Settings tab) ───────────────────────────
        private GroupBox grpUpdate;
        private Label lblCurrentVersion;
        private Label lblUpdateStatus;
        private Button btnCheckUpdates;
        private Button btnOpenRelease;
        private string _latestReleaseUrl = UpdateChecker.ReleasesPage;

        public TileKeeperForm()
        {
            InitializeComponent();

            // Program list columns. The native checkbox is the per-row Enable switch;
            // the first column is just its header, so the name/path live in columns 1/2.
            // Widths are kept inside the 451px list so no horizontal scrolling is needed.
            listPrograms.View = View.Details;
            listPrograms.CheckBoxes = true;
            listPrograms.Columns.Add("Enabled (relaunch)", 100);
            listPrograms.Columns.Add("Program", 150);
            listPrograms.Columns.Add("Path / Command", 199);
            listPrograms.FullRowSelect = true;
            // Checking a box does not select a row on its own (same reason as the Arrange
            // list below), so sync check -> select and refresh the gray state/live snapshot.
            listPrograms.ItemCheck += ListView_CheckboxItemCheck;
            listPrograms.ItemChecked += ListPrograms_ItemChecked;
            listPrograms.MouseDown += ListView_CheckboxMouseDown;

            // Position-only list. Its native checkbox is the per-row Enable switch for
            // positioning; Match Largest Window moved to an explicit clickable column so
            // the two switches can never be confused.
            listPositionPrograms.View = View.Details;
            listPositionPrograms.CheckBoxes = true;
            listPositionPrograms.Columns.Add("Enabled (position)", 100);
            listPositionPrograms.Columns.Add("Program", 205);
            listPositionPrograms.Columns.Add("Match largest", 144);
            listPositionPrograms.FullRowSelect = true;
            // Checking a row's box (Checked) and selecting it (Selected/blue highlight, what
            // Edit/Remove/Up/Down require) are independent WinForms ListView states — checking
            // a box never selects the row on its own. Sync check -> select so a checked row is
            // immediately actionable without an extra click.
            listPositionPrograms.ItemCheck += ListView_CheckboxItemCheck;
            listPositionPrograms.ItemChecked += ListPositionPrograms_ItemChecked;
            listPositionPrograms.MouseDown += ListView_CheckboxMouseDown;
            listPositionPrograms.MouseClick += ListPositionPrograms_MouseClick;

            // Double-buffer the form and the list/checklist controls that get rebuilt
            // wholesale on every tray show/reload — cuts the flicker from that rebuild.
            this.DoubleBuffered = true;
            EnableDoubleBuffer(tabMain);
            EnableDoubleBuffer(listPrograms);
            EnableDoubleBuffer(listPositionPrograms);
            EnableDoubleBuffer(checkedScreen1);
            EnableDoubleBuffer(checkedScreen2);
            EnableDoubleBuffer(checkedScreen3);
            EnableDoubleBuffer(checkedScreen4);

            _autoFitChecks = new[] { chkAutoFit1, chkAutoFit2, chkAutoFit3, chkAutoFit4 };
            _borderlessChecks = new[] { chkBorderless1, chkBorderless2, chkBorderless3, chkBorderless4 };
            _gridRowsFields = new[] { txtGridRows1, txtGridRows2, txtGridRows3, txtGridRows4 };
            _gridColsFields = new[] { txtGridCols1, txtGridCols2, txtGridCols3, txtGridCols4 };

            // Build the small row/col steppers next to each field. The field is narrowed to
            // one centred digit; the label moves left just enough to make room for "-".
            var groups = new[] { grpScreen1, grpScreen2, grpScreen3, grpScreen4 };
            var rowLabels = new[] { lblGridRows1, lblGridRows2, lblGridRows3, lblGridRows4 };
            var colLabels = new[] { lblGridCols1, lblGridCols2, lblGridCols3, lblGridCols4 };
            _gridSpinButtons = new Button[groups.Length][];
            for (int s = 0; s < groups.Length; s++)
            {
                rowLabels[s].Location = new Point(124, 20);
                colLabels[s].Location = new Point(124, 44);

                PlaceGridField(_gridRowsFields[s], 18);
                PlaceGridField(_gridColsFields[s], 42);
                _gridSpinButtons[s] = new[]
                {
                    AddGridSpinButton(groups[s], _gridRowsFields[s], -1, 166, 18),
                    AddGridSpinButton(groups[s], _gridRowsFields[s], +1, 202, 18),
                    AddGridSpinButton(groups[s], _gridColsFields[s], -1, 166, 42),
                    AddGridSpinButton(groups[s], _gridColsFields[s], +1, 202, 42)
                };
            }

            try
            {
                // A missing exe.config is handled earlier, in Program.Main, by recreating the
                // file and relaunching as a fresh process — the .NET configuration system can't
                // recover mid-process once something has touched a missing config file, so by
                // the time this constructor runs the file is guaranteed to already exist.
                MigrateConfigIfNeeded();
            }
            catch (Exception ex)
            {
                // Config recreation failed (e.g. no write permission to the install folder) —
                // warn and keep going with in-memory defaults rather than crashing before the
                // tray icon/UI ever exists.
                Logger.Log($"MigrateConfigIfNeeded failed: {ex.Message}");
                MessageBox.Show(
                    "Could not read or repair the configuration file. The program will start with default settings.\n\n" + ex.Message,
                    "Configuration Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            this.Load += new EventHandler(Form1_Load);
            this.Move += MainForm_Move;
            this.Icon  = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

            // Build the tray context menu (right-click on the tray icon)
            trayMenu = new ContextMenuStrip();
            trayMenu.Items.Add("Show", null, OnShow);
            trayMenu.Items.Add("About", null, OnAbout);
            trayMenu.Items.Add("Exit", null, OnExit);

            trayIcon = new NotifyIcon
            {
                Text             = "TileKeeper",
                Visible          = true,
                Icon             = Icon.ExtractAssociatedIcon(Application.ExecutablePath),
                ContextMenuStrip = trayMenu
            };
            // Right-click menu is supplied by ContextMenuStrip; a left double-click opens the
            // window (the OS does not do this for NotifyIcon on its own).
            trayIcon.DoubleClick += OnShow;

            // Capture the fully laid-out size while the form is still unshown, before any
            // minimise/restore can hand Windows a collapsed rect to remember.
            _restoreSize = this.Size;

            // Start minimised and hidden from the taskbar — app lives in the tray
            this.WindowState    = FormWindowState.Minimized;
            this.ShowInTaskbar  = false;
            this.TopMost        = true; // stay above other windows while shown; X/minimize still work
            this.FormClosing   += new FormClosingEventHandler(OnFormClosing);

            // Restrict numeric input fields to digits only
            WireNumeric(txtCycle);            
            WireNumeric(txtCycleScreen);
            // Grid fields are 1..3 integers; the small +/- buttons below add clamping and a
            // centred one-digit display on top of the digit-only filter.
            WireGridField(txtGridRows1);
            WireGridField(txtGridCols1);
            WireGridField(txtGridRows2);
            WireGridField(txtGridCols2);
            WireGridField(txtGridRows3);
            WireGridField(txtGridCols3);
            WireGridField(txtGridRows4);
            WireGridField(txtGridCols4);
            WireNumeric(txtResWidth);
            WireNumeric(txtResHeight);
            WireNumeric(txtResCycle);

            // Grid preview follows the checked set and the Rows/Cols fields live
            _screenChecklists = new[] { checkedScreen1, checkedScreen2, checkedScreen3, checkedScreen4 };
            _dedupGrayed = new HashSet<string>[_screenChecklists.Length];
            for (int s = 0; s < _screenChecklists.Length; s++)
            {
                _screenCheckOrder[_screenChecklists[s]] = new List<string>();
                // The (N) cell badge renders as *display text* via ListControl.Format (leaving
                // raw item names untouched for dedup/config consumers) AND each checked row is
                // filled with its grid-cell colour by ColoredCheckedListBox.OnDrawItem — the
                // stock control owner-draws internally (LBS.OWNERDRAWFIXED) even though the
                // public DrawMode/DrawItem face is hidden, so subclassing OnDrawItem works.
                _screenChecklists[s].FormattingEnabled = true;
                _screenChecklists[s].Format += ScreenList_Format;
                _screenChecklists[s].ItemCheck += ScreenItemCheck;

                var colored = _screenChecklists[s] as ColoredCheckedListBox;
                if (colored != null)
                    colored.ColorOf = index => RowColorFor(colored, index);
            }
            // A grid-shape edit only changes that screen's preview, so don't repaint all four.
            for (int s = 0; s < _gridRowsFields.Length; s++)
            {
                int index = s;
                _gridRowsFields[s].TextChanged += delegate
                {
                    RefreshScreenGrid(index);
                    PublishLiveScreenPlan();
                };
                _gridColsFields[s].TextChanged += delegate
                {
                    RefreshScreenGrid(index);
                    PublishLiveScreenPlan();
                };
            }

            // AutoFit/Borderless are live switches: republish the plan and wake the keeper so a
            // toggle takes effect on the next pass, with no Save and no cross-thread checkbox read.
            foreach (var check in _autoFitChecks)
                check.CheckedChanged += ChkScreenOption_CheckedChanged;

            // Accept dropped shortcuts/exes from Explorer/Desktop into both program lists.
            // AllowDrop must only be enabled on an STA thread: the moment a ListView handle is
            // created WinForms registers itself for OLE drag-drop (SetAcceptDrops), which throws
            // ThreadStateException off the UI thread. The app itself is STA, but tests build the
            // form on an MTA thread. Drag events still fire via the control's public surface.
            listPrograms.DragEnter += ProgramList_DragEnter;
            listPositionPrograms.DragEnter += ProgramList_DragEnter;
            listPrograms.DragDrop += ListPrograms_DragDrop;
            listPositionPrograms.DragDrop += ListPositionPrograms_DragDrop;
            if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
            {
                listPrograms.AllowDrop = true;
                listPositionPrograms.AllowDrop = true;
            }

            ApplyScreenSectionEnableGate();
            BuildUpdateSection();

            // Re-gate Screen3/4 (and rebuild grid previews) the instant a monitor is
            // plugged/unplugged, instead of only on the next tray show/reload.
            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        }

        /// <summary>
        /// Builds the small, self-contained update section on the Display &amp; Settings tab.
        /// It only checks GitHub Releases and opens the release page — no executable is
        /// downloaded or replaced, so the app keeps its signed-release trust model.
        /// </summary>
        private void BuildUpdateSection()
        {
            grpUpdate = new GroupBox
            {
                Text = "Application Update",
                Location = new Point(9, 339),
                Size = new Size(453, 112),
                TabIndex = 7,
                TabStop = false
            };

            lblCurrentVersion = new Label
            {
                AutoSize = true,
                Location = new Point(9, 21),
                Text = "Current version: " + Application.ProductVersion
            };

            btnCheckUpdates = new Button
            {
                Location = new Point(9, 45),
                Size = new Size(132, 25),
                Text = "Check for updates",
                UseVisualStyleBackColor = true
            };
            btnCheckUpdates.Click += BtnCheckUpdates_Click;

            btnOpenRelease = new Button
            {
                Location = new Point(149, 45),
                Size = new Size(132, 25),
                Text = "Open release page",
                UseVisualStyleBackColor = true
            };
            btnOpenRelease.Click += delegate { OpenReleasePage(_latestReleaseUrl); };

            lblUpdateStatus = new Label
            {
                Location = new Point(9, 77),
                Size = new Size(435, 28),
                Text = "Not checked yet."
            };

            grpUpdate.Controls.Add(lblCurrentVersion);
            grpUpdate.Controls.Add(btnCheckUpdates);
            grpUpdate.Controls.Add(btnOpenRelease);
            grpUpdate.Controls.Add(lblUpdateStatus);
            tabSystem.Controls.Add(grpUpdate);
        }

        /// <summary>Opens the validated GitHub release page in the user's browser.</summary>
        private static void OpenReleasePage(string url)
        {
            if (!UpdateChecker.IsAllowedReleaseUrl(url))
                url = UpdateChecker.ReleasesPage;

            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Logger.Log($"OpenReleasePage failed: {ex.Message}");
                MessageBox.Show("Could not open the release page. Open this address in your browser:\n\n" +
                                UpdateChecker.ReleasesPage,
                                "Update", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        /// <summary>Reads the latest release metadata from the public GitHub API.</summary>
        private static async Task<string> FetchLatestReleaseJsonAsync()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            using (var client = new HttpClient())
            {
                client.Timeout = TimeSpan.FromSeconds(15);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("TileKeeper/" + Application.ProductVersion);
                client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

                using (HttpResponseMessage response = await client.GetAsync(UpdateChecker.LatestReleaseApi))
                {
                    if (!response.IsSuccessStatusCode)
                        throw new InvalidOperationException($"GitHub returned HTTP {(int)response.StatusCode}.");
                    return await response.Content.ReadAsStringAsync();
                }
            }
        }

        /// <summary>Checks GitHub Releases and reports the result; never modifies local files.</summary>
        private async void BtnCheckUpdates_Click(object sender, EventArgs e)
        {
            btnCheckUpdates.Enabled = false;
            btnOpenRelease.Enabled = false;
            lblUpdateStatus.Text = "Checking for updates...";

            try
            {
                string json = await FetchLatestReleaseJsonAsync();
                if (IsDisposed)
                    return;

                if (!UpdateChecker.TryParseLatestRelease(json, out UpdateChecker.ReleaseInfo info, out string error))
                {
                    _latestReleaseUrl = UpdateChecker.ReleasesPage;
                    lblUpdateStatus.Text = error;
                    btnOpenRelease.Enabled = true;
                    return;
                }

                _latestReleaseUrl = info.ReleaseUrl;
                btnOpenRelease.Enabled = true;

                string current = Application.ProductVersion;
                if (UpdateChecker.IsNewerVersion(current, info.Version))
                {
                    lblUpdateStatus.Text = "Update available: " + info.Version;
                    DialogResult answer = MessageBox.Show(this,
                        "A newer version is available.\r\n\r\n" +
                        "Installed: " + current + "\r\n" +
                        "Latest: " + info.Version + "\r\n\r\n" +
                        "Open the release page to download it?",
                        "Update available", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                    if (answer == DialogResult.Yes)
                        OpenReleasePage(info.ReleaseUrl);
                }
                else
                {
                    lblUpdateStatus.Text = "You are using the latest version.";
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Update check failed: {ex.Message}");
                _latestReleaseUrl = UpdateChecker.ReleasesPage;
                lblUpdateStatus.Text = "Could not check for updates. Open the release page manually.";
                btnOpenRelease.Enabled = true;
            }
            finally
            {
                btnCheckUpdates.Enabled = true;
            }
        }

        private void OnDisplaySettingsChanged(object sender, EventArgs e)
        {
            RefreshScreenCache();
            if (!IsHandleCreated || IsDisposed)
                return;
            BeginInvoke(new Action(() =>
            {
                ApplyScreenSectionEnableGate();
                RefreshAllGrids();
            }));
        }

        /// <summary>Re-reads the monitor list once, not on every position-keeper cycle.</summary>
        private void RefreshScreenCache()
        {
            _screens = Screen.AllScreens ?? new Screen[0];
        }

        private Screen CachedPrimaryScreen()
        {
            foreach (var screen in _screens)
                if (screen != null && screen.Primary)
                    return screen;
            return Screen.PrimaryScreen;
        }

        /// <summary>
        /// Suspends native repaint and layout around a bulk rebuild. WM_SETREDRAW is reference
        /// counted by _redrawGuard so nested calls (RebuildScreenCandidates → RefreshAllGrids)
        /// cannot re-enable painting before the outermost operation has finished.
        /// </summary>
        private void WithoutRedraw(Control root, Action rebuild)
        {
            bool outermost = _redrawGuard++ == 0;
            bool suspended = outermost && root != null && root.IsHandleCreated;
            if (suspended)
                SendMessage(root.Handle, WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);
            if (root != null)
                root.SuspendLayout();
            try
            {
                rebuild();
            }
            finally
            {
                if (root != null)
                    root.ResumeLayout(true);
                if (suspended)
                {
                    SendMessage(root.Handle, WM_SETREDRAW, (IntPtr)1, IntPtr.Zero);
                    root.Invalidate(true);
                    root.Update();
                }
                _redrawGuard--;
            }
        }

        private void WireNumeric(TextBox box) =>
            box.KeyPress += NumberOnly_KeyPress;

        /// <summary>
        /// Turns on the protected Control.DoubleBuffered style for a control that doesn't
        /// expose it publicly (ListView, CheckedListBox) — avoids the visible flicker these
        /// controls show when cleared and repopulated wholesale (tray show/reload rebuilds).
        /// </summary>
        private static void EnableDoubleBuffer(Control control)
        {
            typeof(Control)
                .GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .SetValue(control, true, null);
        }

        /// <summary>
        /// Disables every interactive control of a screen whose monitor is not currently
        /// detected (reads the cached monitor list): checklist, AutoFit/Borderless, Rows/Cols
        /// fields and their +/- steppers, and the cell map. A disconnected screen must not be
        /// checkable, editable, or assignable — the rows and their pins are kept intact so
        /// re-plugging the monitor restores the exact same layout. The GroupBox is never
        /// disabled: WinForms disables all of a container's children when the container itself
        /// is disabled, which would also gray the group caption.
        /// </summary>
        private void ApplyScreenSectionEnableGate()
        {
            if (_screenChecklists == null || _gridSpinButtons == null)
                return;

            for (int s = 0; s < _screenChecklists.Length; s++)
            {
                bool on = s < _screens.Length;
                _screenChecklists[s].Enabled = on;
                _autoFitChecks[s].Enabled = on;
                _borderlessChecks[s].Enabled = on;
                _gridRowsFields[s].Enabled = on;
                _gridColsFields[s].Enabled = on;
                foreach (var button in _gridSpinButtons[s])
                    if (button != null)
                        button.Enabled = on;

                switch (s)
                {
                    case 0: gridMap1.Enabled = on; break;
                    case 1: gridMap2.Enabled = on; break;
                    case 2: gridMap3.Enabled = on; break;
                    default: gridMap4.Enabled = on; break;
                }
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Form loading
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Populates all UI controls from the current app.config values and updates
        /// the in-memory timer state used by the background threads.
        /// Also called when the user re-opens the form from the tray (to refresh stale values).
        /// </summary>
        private void InitializeOrReload()
        {
            WithoutRedraw(this, InitializeOrReloadCore);
        }

        private void InitializeOrReloadCore()
        {
            RefreshScreenCache();
            MoveToPrimaryScreen();

            // Translate legacy ScreenN names/keys to stable ids before any list is rebuilt,
            // so the ids loaded into the ListView are exactly the ones written to config.
            MigrateScreenAssignmentsToIds();

            RebuildProgramList(LoadEntriesFromConfig());
            RebuildPositionProgramList(LoadPositionEntriesFromConfig());
            // Seed the one-time Keep Running → Arrange mirror and move legacy screen ids onto it.
            // Must run before the screen checklists are populated.
            EnsurePositionMirror();
            // Publish the freshly loaded rows before the screen lists are rebuilt — dedup and
            // grid badges resolve args from this snapshot, not from the config file.
            RefreshLiveSnapshot();

            // Load process-persistence settings
            txtCycle.Text   = GetConfigValue("CycleSeconds", "15");
            bool.TryParse(ConfigurationManager.AppSettings["EnablePersistRun"], out bool enablePersistRun);
            chkEnablePersist.Checked = enablePersistRun;            

            // Load screen-position keeper settings
            txtCycleScreen.Text = GetConfigValue("CycleKeepScreen", "30");
            bool.TryParse(ConfigurationManager.AppSettings["EnableCustomPosition"], out bool enableCustomPos);
            chkEnableCustPos.Checked = enableCustomPos;
            bool.TryParse(ConfigurationManager.AppSettings["Screen1AutoFit"], out bool autoFit1);
            chkAutoFit1.Checked = autoFit1;
            bool.TryParse(ConfigurationManager.AppSettings["Screen2AutoFit"], out bool autoFit2);
            chkAutoFit2.Checked = autoFit2;
            bool.TryParse(ConfigurationManager.AppSettings["Screen3AutoFit"], out bool autoFit3);
            chkAutoFit3.Checked = autoFit3;
            bool.TryParse(ConfigurationManager.AppSettings["Screen4AutoFit"], out bool autoFit4);
            chkAutoFit4.Checked = autoFit4;

            // Load the per-screen borderless (frame-stripping) toggles; default ON
            // (matches the historic behavior of stripping chrome from tiled windows).
            chkBorderless1.Checked = GetConfigValue("Screen1Borderless", "True") == bool.TrueString;
            chkBorderless2.Checked = GetConfigValue("Screen2Borderless", "True") == bool.TrueString;
            chkBorderless3.Checked = GetConfigValue("Screen3Borderless", "True") == bool.TrueString;
            chkBorderless4.Checked = GetConfigValue("Screen4Borderless", "True") == bool.TrueString;

            // Load grid shape. 1..3 only; legacy 0 (the old "auto" value) becomes 1.
            for (int s = 0; s < _gridRowsFields.Length; s++)
            {
                _gridRowsFields[s].Text = GridValue(GetConfigValue("GridRows" + (s + 1), "1")).ToString();
                _gridColsFields[s].Text = GridValue(GetConfigValue("GridCols" + (s + 1), "1")).ToString();
            }

            // Populate the per-screen checked lists and manual cell pins. Wrapped in
            // _suppressingDedup so the SetItemChecked calls inside PopulateScreen never trigger
            // ScreenItemCheck's own dedup logic — gray state is recomputed fresh afterward.
            _suppressingDedup = true;
            try
            {
                var screen1 = SplitConfigList(GetConfigValue("Screen1", ""));
                var screen2 = SplitConfigList(GetConfigValue("Screen2", ""));
                LoadScreenSlots(_screenSlot1, screen1, GetConfigValue("Screen1Slots", ""));
                LoadSlotPins(GetConfigValue("Screen1SlotPins", ""), _screenSlot1);
                LoadReservedCells(GetConfigValue("Screen1ReservedCells", ""), _screenReservedCells[0], _screenSlot1);
                LoadScreenSlots(_screenSlot2, screen2, GetConfigValue("Screen2Slots", ""));
                LoadSlotPins(GetConfigValue("Screen2SlotPins", ""), _screenSlot2);
                LoadReservedCells(GetConfigValue("Screen2ReservedCells", ""), _screenReservedCells[1], _screenSlot2);
                PopulateScreen(checkedScreen1, BuildCandidates(), new HashSet<string>(screen1, StringComparer.OrdinalIgnoreCase));
                PopulateScreen(checkedScreen2, BuildCandidates(), new HashSet<string>(screen2, StringComparer.OrdinalIgnoreCase));

                // Screen3/Screen4 lists are always populated, same as Screen1/2 — they're only
                // grayed out (ApplyScreenSectionEnableGate) when the monitor isn't detected,
                // never emptied.
                var screen3 = SplitConfigList(GetConfigValue("Screen3", ""));
                LoadScreenSlots(_screenSlot3, screen3, GetConfigValue("Screen3Slots", ""));
                LoadSlotPins(GetConfigValue("Screen3SlotPins", ""), _screenSlot3);
                LoadReservedCells(GetConfigValue("Screen3ReservedCells", ""), _screenReservedCells[2], _screenSlot3);
                PopulateScreen(checkedScreen3, BuildCandidates(), new HashSet<string>(screen3, StringComparer.OrdinalIgnoreCase));

                var screen4 = SplitConfigList(GetConfigValue("Screen4", ""));
                LoadScreenSlots(_screenSlot4, screen4, GetConfigValue("Screen4Slots", ""));
                LoadSlotPins(GetConfigValue("Screen4SlotPins", ""), _screenSlot4);
                LoadReservedCells(GetConfigValue("Screen4ReservedCells", ""), _screenReservedCells[3], _screenSlot4);
                PopulateScreen(checkedScreen4, BuildCandidates(), new HashSet<string>(screen4, StringComparer.OrdinalIgnoreCase));
            }
            finally
            {
                _suppressingDedup = false;
            }
            RecomputeDedupGrayed();

            // Load display resolution hold settings
            bool.TryParse(ConfigurationManager.AppSettings["EnableResolutionHold"], out bool enableRes);
            chkEnableRes.Checked = enableRes;
            txtResWidth.Text  = GetConfigValue("ResolutionWidth", "1920");
            txtResHeight.Text = GetConfigValue("ResolutionHeight", "1080");
            txtResCycle.Text  = GetConfigValue("ResolutionCheckSeconds", "10");

            // Registry / Intranet Zone opt-in (default off). Nothing is written unless
            // the user explicitly enables this in the System tab.
            chkEnableClickOnceZone.Checked = IsClickOnceZoneEnabled();
            UpdateClickOnceZoneStatus();

            // Process termination protection (default on). The DACL is applied during
            // Program.Main, so a change here takes effect after TileKeeper restarts.
            string protectValue = GetConfigValue("EnableTerminateProtection", "True");
            chkEnableTerminateProtection.Checked =
                !bool.TryParse(protectValue, out bool protectProcess) || protectProcess;

            // Convert the config values (seconds) to milliseconds for Thread.Sleep
            monitorTimer       = int.Parse(txtCycle.Text)       * 1000;
            keepScreenPosTimer = int.Parse(txtCycleScreen.Text) * 1000;

            RefreshAllGrids();
            RememberConfigStamp();
            RememberSavedConfig();
        }

        /// <summary>
        /// Starts the background threads after the form has fully loaded.
        /// All threads are marked IsBackground so they terminate automatically when
        /// the main UI thread exits — no manual cleanup needed on normal shutdown.
        /// </summary>
        private void Form1_Load(object sender, EventArgs e)
        {
            InitializeOrReload();
            ApplyIntranetZoneForClickOnceServers(); // No-op unless the user opted in

            // async Task entry — an `async void` here would die at the first await under a
            // plain Thread and crash the process on any uncaught exception.
            _keepRun = Task.Run(() => OnMonitorEvent());

            _keepPosition = new Thread(OnMonitorPositionEvent) { IsBackground = true };
            _keepPosition.Start();            

            _displayRes = new Thread(OnResolutionHoldEvent) { IsBackground = true };
            _displayRes.Start();
        }

        /// <summary>
        /// Keeps the form on the primary screen. If the user moves the form to a secondary
        /// monitor (which can happen when monitors are reconnected in a different order),
        /// the form snaps back so it is always accessible.
        /// </summary>
        private void MainForm_Move(object sender, EventArgs e)
        {
            // Restoring from the tray sets the position explicitly; reacting to that Move
            // would fight the placement (and briefly snap the window to a corner).
            if (_restoringFromTray)
                return;
            MoveToPrimaryScreen();
        }

        /// <summary>
        /// Keeps the whole form inside the primary working area (not just its top-left
        /// corner). A window restored from the tray used to be judged by Location alone,
        /// so one sitting mostly below the taskbar counted as "inside" and stayed hidden.
        /// </summary>
        private void MoveToPrimaryScreen()
        {
            Screen primary = CachedPrimaryScreen();
            if (primary == null)
                return;
            Rectangle area = primary.WorkingArea;
            int x = this.Location.X;
            int y = this.Location.Y;

            bool outside =
                x < area.X ||
                y < area.Y ||
                x + this.Width  > area.Right ||
                y + this.Height > area.Bottom;

            if (!outside)
                return;

            x = Math.Min(Math.Max(x, area.X), Math.Max(area.X, area.Right  - this.Width));
            y = Math.Min(Math.Max(y, area.Y), Math.Max(area.Y, area.Bottom - this.Height));
            this.Location = new Point(x, y);
        }

        /// <summary>
        /// Centres the form in the primary working area when it is restored from the tray.
        /// While minimized, <c>Location</c> is not a usable position (Windows reports the
        /// off-screen minimized origin), which made tray restores appear in a corner.
        /// </summary>
        private void PlaceRestoredWindow()
        {
            Screen primary = CachedPrimaryScreen();
            if (primary == null)
                return;

            Rectangle area = primary.WorkingArea;
            int x = area.X + Math.Max(0, (area.Width  - this.Width)  / 2);
            int y = area.Y + Math.Max(0, (area.Height - this.Height) / 2);
            this.Location = new Point(x, y);
        }

        /// <summary>Returns the value for <paramref name="key"/> from app.config, or <paramref name="defaultValue"/> if missing or empty.</summary>
        private string GetConfigValue(string key, string defaultValue)
        {
            string value = ConfigurationManager.AppSettings[key];
            return string.IsNullOrEmpty(value) ? defaultValue : value;
        }

        /// <summary>
        /// Splits the comma-separated <c>ProgramPath</c> config value into individual
        /// program entries, re-attaching the continuation of a ClickOnce dfshim command.
        /// </summary>
        private string[] SplitProgramPaths(string rawProgramPath)
        {
            string[] parts = rawProgramPath.Split(',');
            var result = new List<string>();

            foreach (string part in parts)
            {
                if (result.Count > 0 && part.StartsWith("ShOpenVerbApplication", StringComparison.OrdinalIgnoreCase))
                    result[result.Count - 1] = result[result.Count - 1] + "," + part;
                else
                    result.Add(part);
            }

            return result.ToArray();
        }

        private static List<string> SplitConfigList(string csv) =>
            (csv ?? "").Split(',')
                       .Select(s => s.Trim())
                       .Where(s => s.Length > 0)
                       .ToList();

        // ─────────────────────────────────────────────────────────────────────
        // Program list model
        // ─────────────────────────────────────────────────────────────────────


        /// <summary>Splits a raw Programs-tab "Name" (e.g. "app.exe arg1 arg2") into exe base name and args.</summary>
        internal static void ParseNameParts(string raw, out string baseName, out string baseArgs) =>
            ProgramEntry.ParseNameParts(raw, out baseName, out baseArgs);

        /// <summary>
        /// Recomputes legacy display/process keys over the combined set. Screen assignments no
        /// longer use these — they use the persistent <see cref="ProgramEntry.Id"/> — but the
        /// resolver still needs them for configs written before ids existed.
        /// </summary>
        private static void AssignIdentityKeys(List<ProgramEntry> entries)
        {
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in entries)
                if (!string.IsNullOrWhiteSpace(e.BaseName))
                    counts[e.BaseName] = counts.TryGetValue(e.BaseName, out int c) ? c + 1 : 1;

            foreach (var e in entries)
            {
                if (string.IsNullOrWhiteSpace(e.BaseName))
                    continue;
                e.Key = counts[e.BaseName] > 1
                    ? $"{e.BaseName} ({(e.BaseArgs.Length > 0 ? e.BaseArgs : "no args")})"
                    : e.BaseName;
            }
        }

        // Row layout is: [0] checkbox/header only, [1] name, [2] path or Match-largest glyph.
        private const string MatchLargestOn  = "\u2611"; // ☑
        private const string MatchLargestOff = "\u2610"; // ☐

        // ItemChecked is raised by the native ListView while rows are being added or removed.
        // During that re-entrant notification a row can still be in the collection with its
        // subitems detached (or be null), so every helper must tolerate incomplete rows.
        private static string RowName(ListViewItem item)
        {
            if (item == null) return "";
            return item.SubItems.Count > 1 ? item.SubItems[1]?.Text ?? item.Text ?? "" : item.Text ?? "";
        }

        private static string RowPath(ListViewItem item) =>
            item != null && item.SubItems.Count > 2 ? item.SubItems[2]?.Text ?? "" : "";

        private static bool RowMatchLargest(ListViewItem item) =>
            item != null && item.SubItems.Count > 2 && item.SubItems[2]?.Text == MatchLargestOn;

        /// <summary>Persistent row id stored on the ListView item; empty only for transient rows.</summary>
        private static string RowId(ListViewItem item) => item?.Tag as string ?? "";

        private static ListViewItem NewProgramRow(string name, string path, bool enabled, string id)
        {
            var item = new ListViewItem(new[] { "", name, path }) { Checked = enabled, Tag = id };
            ApplyRowEnabledColor(item);
            return item;
        }

        private static ListViewItem NewPositionRow(string name, bool matchLargest, bool enabled, string id)
        {
            var item = new ListViewItem(new[] { "", name, matchLargest ? MatchLargestOn : MatchLargestOff })
                { Checked = enabled, Tag = id };
            ApplyRowEnabledColor(item);
            return item;
        }

        /// <summary>Enabled rows render normally; a disabled row is grayed in both lists.</summary>
        private static void ApplyRowEnabledColor(ListViewItem item)
        {
            item.ForeColor = item.Checked ? SystemColors.WindowText : SystemColors.GrayText;
        }

        /// <summary>Builds the program entries currently shown in the ListView.</summary>
        private List<ProgramEntry> GetProgramEntries()
        {
            var list = new List<ProgramEntry>();
            foreach (ListViewItem item in listPrograms.Items)
            {
                if (item == null) continue; // transient native row during Clear/Add
                list.Add(new ProgramEntry(RowName(item), RowPath(item),
                                          enabled: item.Checked, id: RowId(item)));
            }
            AssignIdentityKeys(list);
            return list;
        }

        private void RebuildProgramList(List<ProgramEntry> entries)
        {
            _suppressLiveSnapshotRefresh = true;
            _allowProgramListItemCheck = true;
            try
            {
                listPrograms.BeginUpdate();
                try
                {
                    listPrograms.Items.Clear();
                    foreach (var e in entries)
                        listPrograms.Items.Add(NewProgramRow(e.Name, e.Path, e.Enabled, e.Id));
                }
                finally
                {
                    listPrograms.EndUpdate();
                }

                if (!listPrograms.IsHandleCreated && listPrograms.Parent != null)
                {
                    try { _ = listPrograms.Handle; } catch { /* best effort */ }
                }
            }
            finally
            {
                _allowProgramListItemCheck = false;
                _suppressLiveSnapshotRefresh = false;
            }
        }

        /// <summary>Reads ProgramName/ProgramPath/ProgramEnabled from config into parallel entries.</summary>
        private List<ProgramEntry> LoadEntriesFromConfig()
        {
            var result = new List<ProgramEntry>();
            if (string.IsNullOrWhiteSpace(GetConfigValue("ProgramName", "")))
                return result;

            string[] names = GetConfigValue("ProgramName", "").Split(',');
            string[] paths = SplitProgramPaths(GetConfigValue("ProgramPath", ""));
            bool[] enabled = ProgramEnableFlags.Parse(GetConfigValue("ProgramEnabled", ""), names.Length);
            string[] ids = ProgramCatalog.SplitCsv(GetConfigValue("ProgramId", ""));

            for (int i = 0; i < names.Length && i < paths.Length; i++)
                result.Add(new ProgramEntry(names[i], paths[i], enabled: enabled[i],
                                            id: i < ids.Length ? ids[i] : null));
            AssignIdentityKeys(result);
            return result;
        }

        /// <summary>Builds the position-only program entries currently shown in the Position tab's list.</summary>
        private List<ProgramEntry> GetPositionEntries()
        {
            var list = new List<ProgramEntry>();
            foreach (ListViewItem item in listPositionPrograms.Items)
            {
                if (item == null) continue; // transient native row during Clear/Add
                list.Add(new ProgramEntry(RowName(item), "", matchLargestWindow: RowMatchLargest(item),
                                          positionOnly: true, enabled: item.Checked, id: RowId(item)));
            }
            AssignIdentityKeys(list);
            return list;
        }

        /// <summary>Reads PositionProgramName/MatchLargest/Enabled from config into parallel entries.</summary>
        private List<ProgramEntry> LoadPositionEntriesFromConfig()
        {
            var result = new List<ProgramEntry>();
            string namesCsv = GetConfigValue("PositionProgramName", "");
            if (string.IsNullOrWhiteSpace(namesCsv))
                return result;

            string[] names = namesCsv.Split(',');
            string[] matches = GetConfigValue("PositionProgramMatchLargest", "").Split(',');
            bool[] enabled = ProgramEnableFlags.Parse(GetConfigValue("PositionProgramEnabled", ""), names.Length);
            string[] ids = ProgramCatalog.SplitCsv(GetConfigValue("PositionProgramId", ""));

            for (int i = 0; i < names.Length; i++)
            {
                bool matchLargest = i < matches.Length && matches[i].Trim() == "1";
                result.Add(new ProgramEntry(names[i], "", matchLargestWindow: matchLargest,
                                            positionOnly: true, enabled: enabled[i],
                                            id: i < ids.Length ? ids[i] : null));
            }
            AssignIdentityKeys(result);
            return result;
        }

        private void RebuildPositionProgramList(List<ProgramEntry> entries)
        {
            _suppressLiveSnapshotRefresh = true;
            _allowProgramListItemCheck = true;
            try
            {
                listPositionPrograms.BeginUpdate();
                try
                {
                    listPositionPrograms.Items.Clear();
                    foreach (var e in entries)
                        listPositionPrograms.Items.Add(NewPositionRow(e.Name, e.MatchLargestWindow, e.Enabled, e.Id));
                }
                finally
                {
                    listPositionPrograms.EndUpdate();
                }

                // Ensure handle exists so checked states are not lost on later handle creation
                // where the checkbox-click guard would block the sync.
                if (!listPositionPrograms.IsHandleCreated && listPositionPrograms.Parent != null)
                {
                    try { _ = listPositionPrograms.Handle; } catch { /* best effort */ }
                }
            }
            finally
            {
                _allowProgramListItemCheck = false;
                _suppressLiveSnapshotRefresh = false;
            }
        }

        /// <summary>All program entries the Position tab can offer as candidates — Programs tab + position-only list.</summary>
        private List<ProgramEntry> GetAllPositionableEntries()
        {
            var all = new List<ProgramEntry>();
            all.AddRange(GetProgramEntries());
            all.AddRange(GetPositionEntries());
            AssignIdentityKeys(all); // re-key over the combined set so cross-list same-exe entries disambiguate too
            return all;
        }

        /// <summary>Same as <see cref="GetAllPositionableEntries"/> but reading from saved config instead of the live UI.</summary>
        private List<ProgramEntry> LoadAllPositionableEntriesFromConfig()
        {
            var all = new List<ProgramEntry>();
            all.AddRange(LoadEntriesFromConfig());
            all.AddRange(LoadPositionEntriesFromConfig());
            AssignIdentityKeys(all);
            return all;
        }

        /// <summary>
        /// Republishes the UI snapshots the background loops read. Called after every list
        /// mutation (load, add, remove, reorder, check/uncheck) and after Save, so the loops see
        /// exactly what the form shows — no Save required, no stale index left behind.
        /// </summary>
        private void RefreshLiveSnapshot()
        {
            // Re-entrant ItemChecked notifications fire while a list is mid-rebuild.
            // Ignore them; the rebuild's caller publishes the finished snapshot.
            if (_suppressLiveSnapshotRefresh)
                return;

            var programs = GetProgramEntries();
            var all = new List<ProgramEntry>(programs);
            all.AddRange(GetPositionEntries());
            AssignIdentityKeys(all);

            Volatile.Write(ref _livePrograms, programs);
            Volatile.Write(ref _livePositionable, all);
            RefreshIdDisplay();
        }

        /// <summary>
        /// Entries the position keeper should use: the live snapshot once the form has built its
        /// lists, otherwise (very early startup) the saved config.
        /// </summary>
        private List<ProgramEntry> CurrentPositionableEntries() =>
            Volatile.Read(ref _livePositionable) ?? LoadAllPositionableEntriesFromConfig();

        private static string DisplayFor(ProgramEntry e) =>
            e == null ? "" : string.IsNullOrWhiteSpace(e.BaseArgs)
                ? e.BaseName
                : $"{e.BaseName} ({e.BaseArgs})";

        private string DisplayForId(string id) =>
            _idDisplay.TryGetValue(id ?? "", out string label) ? label : id ?? "";

        private string DisplayNameOnlyForId(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return "";
            foreach (var e in CurrentPositionableEntries())
                if (string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase))
                    return e.BaseName ?? "";
            return _idDisplay.TryGetValue(id, out string label) ? label : id;
        }

        /// <summary>Identity used for the "one program, one screen" rule: exe + args, never the row id.</summary>
        private static string ProcessIdentityFor(ProgramEntry e) =>
            e == null ? "" : e.BaseName + "\u0000" + e.BaseArgs;

        private string ProcessIdentityForId(string id) =>
            ProcessIdentityFor(CurrentPositionableEntries().FirstOrDefault(
                e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase)));

        private void RefreshIdDisplay()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in CurrentPositionableEntries())
                if (!string.IsNullOrWhiteSpace(e.Id))
                    map[e.Id] = DisplayFor(e);
            _idDisplay = map;
        }

        private static List<PositionTargets.Entry> ToPositionTargetsEntries(IEnumerable<ProgramEntry> entries)
        {
            var result = new List<PositionTargets.Entry>();
            foreach (var e in entries ?? Enumerable.Empty<ProgramEntry>())
                if (!string.IsNullOrWhiteSpace(e.Id))
                    result.Add(new PositionTargets.Entry(
                        e.Key, e.BaseName, e.BaseArgs, e.MatchLargestWindow,
                        e.PositionOnly, e.Enabled, e.Id));
            return result;
        }

        /// <summary>
        /// One-time translation of Screen1-4 from legacy names/keys to persistent row ids.
        /// Also fills <see cref="_catalog"/> and persists ids that were generated for rows
        /// written before this version. Safe to run on every load: tokens that are already ids
        /// are left untouched, and a key that cannot be matched keeps its historic text so a
        /// stale assignment is never silently dropped.
        /// </summary>
        private void MigrateScreenAssignmentsToIds()
        {
            var entries = LoadAllPositionableEntriesFromConfig();
            var targets = ToPositionTargetsEntries(entries);
            var knownIds = new HashSet<string>(entries.Select(e => e.Id), StringComparer.OrdinalIgnoreCase);

            _catalog.Clear();
            foreach (var e in entries)
                (e.PositionOnly ? _catalog.PositionOnly : _catalog.Programs).Add(e);
            _catalog.Rekey();

            var pending = new List<KeyValuePair<string, string>>();
            void Queue(string key, string value) =>
                pending.Add(new KeyValuePair<string, string>(key, value));

            bool screenChanged = false;
            for (int s = 0; s < 4; s++)
            {
                string key = "Screen" + (s + 1);
                var oldTokens = SplitConfigList(GetConfigValue(key, ""));
                if (oldTokens.Count == 0)
                    continue;

                int[] slots = SlotsFromConfig(GetConfigValue(key + "Slots", ""), oldTokens.Count);
                var ids = ProgramCatalog.MigrateTokens(oldTokens, targets, knownIds);

                string joined = string.Join(",", ids);
                if (!string.Equals(joined, string.Join(",", oldTokens), StringComparison.Ordinal))
                {
                    Queue(key, joined);
                    Queue(key + "Slots", string.Join(",", slots));
                    screenChanged = true;
                }
            }

            // Persist generated ids even when no screen token needed translation.
            Queue("ProgramId", ProgramCatalog.JoinIds(entries.Where(e => !e.PositionOnly)));
            Queue("PositionProgramId", ProgramCatalog.JoinIds(entries.Where(e => e.PositionOnly)));
            Queue("ScreenIdsMigrated", "True");

            if (pending.Count > 0)
            {
                if (screenChanged)
                    BackupConfig();
                SaveSettingsBatch(pending);
            }
            RefreshIdDisplay();
        }

        /// <summary>
        /// Mirrors Keep Running rows into the Arrange list once, and re-points any legacy screen
        /// assignment from a Keep Running row id to the matching Arrange row id.
        ///
        /// The mirror is seeded once (`PositionMirrorSeeded`), not on every load: after the user
        /// deliberately removes an Arrange row, it must stay removed even though the Keep Running
        /// row still exists. New Keep Running rows are mirrored immediately by
        /// <see cref="AddProgramRowFromFile"/>.
        ///
        /// Mirror rows get their own persistent id. Removing either list therefore leaves the
        /// other list intact; only removing an Arrange row also clears its Screen1-4 entries.
        /// </summary>
        private void EnsurePositionMirror()
        {
            bool seeded = string.Equals(GetConfigValue("PositionMirrorSeeded", "False"),
                                        bool.TrueString, StringComparison.OrdinalIgnoreCase);

            var programs = GetProgramEntries();
            var positions = GetPositionEntries();

            var positionByName = new Dictionary<string, ProgramEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in positions)
                if (!string.IsNullOrWhiteSpace(entry.Name) && !positionByName.ContainsKey(entry.Name))
                    positionByName[entry.Name] = entry;

            var programById = new Dictionary<string, ProgramEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in programs)
                if (!string.IsNullOrWhiteSpace(entry.Id))
                    programById[entry.Id] = entry;

            bool added = false;
            if (!seeded)
            {
                foreach (var program in programs)
                {
                    if (string.IsNullOrWhiteSpace(program.Name) || positionByName.ContainsKey(program.Name))
                        continue;

                    var mirror = new ProgramEntry(program.Name, "", positionOnly: true,
                                                  enabled: true, id: ProgramEntry.NewId());
                    ListViewItem item = null;
                    _allowProgramListItemCheck = true;
                    try
                    {
                        item = NewPositionRow(mirror.Name, false, true, mirror.Id);
                        listPositionPrograms.Items.Add(item);
                        SetProgramListChecked(listPositionPrograms, item, true);
                    }
                    finally { _allowProgramListItemCheck = false; }
                    if (item != null) SetProgramListChecked(listPositionPrograms, item, true);
                    positionByName[mirror.Name] = mirror;
                    added = true;
                }
            }

            // Screen1-4 can still contain ids written by an earlier build where both lists were
            // offered as candidates. Follow the matching Arrange row so its Enable switch wins.
            var remap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in programById)
                if (positionByName.TryGetValue(pair.Value.Name, out ProgramEntry mirror))
                    remap[pair.Key] = mirror.Id;

            var pending = new List<KeyValuePair<string, string>>();
            bool screensChanged = false;
            for (int s = 0; s < 4; s++)
            {
                string key = "Screen" + (s + 1);
                var tokens = SplitConfigList(GetConfigValue(key, ""));
                if (tokens.Count == 0)
                    continue;

                var mapped = ProgramCatalog.RemapTokens(tokens, remap);
                if (!mapped.SequenceEqual(tokens, StringComparer.Ordinal))
                {
                    pending.Add(new KeyValuePair<string, string>(key, string.Join(",", mapped)));
                    screensChanged = true;
                }
            }

            if (!seeded)
                pending.Add(new KeyValuePair<string, string>("PositionMirrorSeeded", bool.TrueString));

            if (added)
            {
                var allPositions = GetPositionEntries();
                pending.Add(new KeyValuePair<string, string>(
                    "PositionProgramName", string.Join(",", allPositions.Select(x => x.Name))));
                pending.Add(new KeyValuePair<string, string>(
                    "PositionProgramMatchLargest",
                    string.Join(",", allPositions.Select(x => x.MatchLargestWindow ? "1" : "0"))));
                pending.Add(new KeyValuePair<string, string>(
                    "PositionProgramEnabled", ProgramEnableFlags.Serialize(allPositions.Select(x => x.Enabled))));
                pending.Add(new KeyValuePair<string, string>(
                    "PositionProgramId", ProgramCatalog.JoinIds(allPositions)));
            }

            if (pending.Count == 0)
                return;

            if (screensChanged)
                BackupConfig();
            SaveSettingsBatch(pending);
            RememberConfigStamp();
        }

        // ─────────────────────────────────────────────────────────────────────
        // Screen assignment model (checked list + grid slots)
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Candidate row ids for all screen lists. The Arrange list is the single source: every
        /// Keep Running row is mirrored there once (see <see cref="EnsurePositionMirror"/>), so
        /// checking a program for a screen and unchecking its positioning switch both act on the
        /// same row id. Including both lists here used to expose two ids for one process and let
        /// an unchecked Arrange row be bypassed by its Keep Running twin.
        /// </summary>
        private List<string> BuildCandidates()
        {
            var candidates = new List<string>();
            foreach (var e in GetPositionEntries())
                if (!string.IsNullOrWhiteSpace(e.Id) && !candidates.Contains(e.Id, StringComparer.OrdinalIgnoreCase))
                    candidates.Add(e.Id);

            // An id may still be present only in config (e.g. a row removed from a list but not
            // yet saved). Keep it visible so its assignment can be edited or cleared.
            for (int s = 0; s < 4; s++)
                foreach (var id in SplitConfigList(GetConfigValue("Screen" + (s + 1), "")))
                    if (!candidates.Contains(id, StringComparer.OrdinalIgnoreCase))
                        candidates.Add(id);

            return candidates;
        }

        /// <summary>Rebuilds all four checklist items, preserving current checks (called after program list edits).</summary>
        private void RebuildScreenCandidates()
        {
            WithoutRedraw(tabPosition, () =>
            {
                var candidates = BuildCandidates();
                _suppressingDedup = true;
                try
                {
                    for (int s = 0; s < _screenChecklists.Length; s++)
                    {
                        var prev = new HashSet<string>(GetCheckedNames(_screenChecklists[s]), StringComparer.OrdinalIgnoreCase);
                        foreach (var n in SplitConfigList(GetConfigValue("Screen" + (s + 1), "")))
                            prev.Add(n);
                        PopulateScreen(_screenChecklists[s], candidates, prev);
                    }
                }
                finally
                {
                    _suppressingDedup = false;
                }
                RecomputeDedupGrayed();
                RefreshAllGrids();
            });
        }

        /// <summary>Slots dictionary for screen index 0..3.</summary>
        private Dictionary<string, int> ScreenSlotsFor(int index)
        {
            switch (index)
            {
                case 0: return _screenSlot1;
                case 1: return _screenSlot2;
                case 2: return _screenSlot3;
                default: return _screenSlot4;
            }
        }

        /// <summary>Deliberately emptied cells for screen index 0..3.</summary>
        private HashSet<int> ScreenReservedFor(int index)
        {
            if (index < 0 || index >= _screenReservedCells.Length)
                return _screenReservedCells[0];
            return _screenReservedCells[index];
        }

        /// <summary>
        /// Removes the screen assignments belonging to the given persistent row ids from every
        /// checklist and pin map, then writes ScreenN/ScreenNSlots through to config. There is no
        /// string matching: two rows may share a name and args, but only the removed row's id may
        /// be cleared. The write-through is deliberate — <see cref="BuildCandidates"/> keeps ids
        /// that still exist only in config, so an in-memory-only removal would be resurrected.
        /// </summary>
        private void ExpungeFromScreens(IEnumerable<string> removedIds)
        {
            var ids = new HashSet<string>(removedIds ?? new string[0], StringComparer.OrdinalIgnoreCase);
            if (ids.Count == 0)
                return;

            var pending = new List<KeyValuePair<string, string>>();
            void Queue(string key, string value) =>
                pending.Add(new KeyValuePair<string, string>(key, value));

            // Removing a checked item raises ItemCheck on CheckedListBox. Without this guard
            // ScreenItemCheck would rewrite click order and republish grids mid-expunge.
            bool previousGuard = _suppressingDedup;
            _suppressingDedup = true;
            try
            {
                for (int s = 0; s < _screenChecklists.Length; s++)
                {
                    CheckedListBox chk = _screenChecklists[s];
                    chk.BeginUpdate();
                    try
                    {
                        for (int i = chk.Items.Count - 1; i >= 0; i--)
                        {
                            string item = chk.Items[i].ToString();
                            if (!ids.Contains(item))
                                continue;
                            chk.Items.RemoveAt(i);
                            _screenCheckOrder[chk].RemoveAll(n =>
                                string.Equals(n, item, StringComparison.OrdinalIgnoreCase));
                        }
                    }
                    finally
                    {
                        chk.EndUpdate();
                    }

                    // Config is the source of truth for assignments: remove the ids from the
                    // saved list itself so a stale UI snapshot can never resurrect them.
                    string key = "Screen" + (s + 1);
                    var oldIds = SplitConfigList(GetConfigValue(key, ""));
                    int[] oldSlots = SlotsFromConfig(GetConfigValue(key + "Slots", ""), oldIds.Count);
                    ProgramCatalog.RemoveIdsFromTokens(oldIds, ids, oldSlots,
                                                       out List<string> keptIds, out List<int> keptSlots);
                    Queue(key, string.Join(",", keptIds));
                    Queue(key + "Slots", string.Join(",", keptSlots));

                    var slots = ScreenSlotsFor(s);
                    slots.Clear();
                    for (int i = 0; i < keptIds.Count; i++)
                        if (keptSlots[i] >= 0)
                            slots[keptIds[i]] = keptSlots[i];
                }

                // Keep a recoverable copy before changing the saved assignments.
                if (pending.Count > 0)
                    BackupConfig();
                SaveSettingsBatch(pending);

                // Our own write must not look like an external edit when the form is reopened.
                RememberConfigStamp();
            }
            finally
            {
                _suppressingDedup = previousGuard;
            }
        }

        /// <summary>
        /// Identity strings a removed row can be stored under in ScreenN. Pure helper so odd
        /// argument/quote/.lnk cases can be regression-tested without a Form.
        /// </summary>
        internal static HashSet<string> BuildRemovalAliases(string name, string key,
                                                             string baseName, string baseArgs)
            => ProgramCatalog.BuildRemovalAliases(name, key, baseName, baseArgs);

        private void PopulateScreen(CheckedListBox chk, List<string> candidates, ISet<string> checkedSet)
        {
            chk.BeginUpdate();
            try
            {
                chk.Items.Clear();
                for (int i = 0; i < candidates.Count; i++)
                {
                    chk.Items.Add(candidates[i]);
                    chk.SetItemChecked(i, checkedSet.Contains(candidates[i]));
                }
            }
            finally
            {
                chk.EndUpdate();
            }
            SeedClickOrder(chk);
        }

        /// <summary>
        /// Re-derives a checklist's click-order list from its current checked state, keeping
        /// the already-tracked click prefix (rows the user actually clicked this session) and
        /// appending any checked-but-untracked names in list order. Runs after every populate
        /// so <see cref="_screenCheckOrder"/> is always the sole source of badge order —
        /// <see cref="GetCheckedNamesInClickOrder"/> needs no list-order fallback.
        /// </summary>
        private void SeedClickOrder(CheckedListBox chk)
        {
            var tracked = _screenCheckOrder[chk];
            var seed = MergeClickOrder(tracked, GetCheckedNames(chk));
            tracked.Clear();
            tracked.AddRange(seed);
        }

        private static List<string> GetCheckedNames(CheckedListBox chk)
        {
            var names = new List<string>();
            foreach (object item in chk.CheckedItems)
                names.Add(item.ToString());
            return names;
        }

        /// <summary>
        /// Checked program names in click order. Tracks how the user actually checked rows so
        /// auto-fill badge numbers sequence 1..N by click order instead of list order. The
        /// click-order list supplies the order; any checked name it lost is appended in list
        /// order (see <see cref="MergeClickOrder"/>). That fallback is what keeps the grid cell
        /// count equal to the checked count when a row was checked by a bulk populate or config
        /// reload and never went through <see cref="ScreenItemCheck"/>.
        /// </summary>
        private List<string> GetCheckedNamesInClickOrder(CheckedListBox chk)
        {
            var checkedNow = GetCheckedNames(chk);
            List<string> tracked;
            _screenCheckOrder.TryGetValue(chk, out tracked);
            var merged = MergeClickOrder(tracked, checkedNow);

            // Re-sync the tracker so drift can never persist across another refresh.
            if (tracked != null && !SameNameList(tracked, merged))
            {
                tracked.Clear();
                tracked.AddRange(merged);
            }
            return merged;
        }

        /// <summary>
        /// Pure click-order merge: tracked names that are still checked come first, then any
        /// checked name the tracker missed. Exposed for the offline harness.
        /// </summary>
        internal static List<string> MergeClickOrder(IEnumerable<string> ordered,
                                                     IEnumerable<string> checkedNow)
        {
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var checkedSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (checkedNow != null)
                foreach (var name in checkedNow)
                    if (!string.IsNullOrWhiteSpace(name))
                        checkedSet.Add(name);

            if (ordered != null)
                foreach (var name in ordered)
                    if (!string.IsNullOrWhiteSpace(name) && checkedSet.Contains(name) && seen.Add(name))
                        result.Add(name);

            if (checkedNow != null)
                foreach (var name in checkedNow)
                    if (!string.IsNullOrWhiteSpace(name) && seen.Add(name))
                        result.Add(name);

            return result;
        }

        private static bool SameNameList(List<string> a, List<string> b)
        {
            if (a == null || b == null || a.Count != b.Count)
                return false;
            for (int i = 0; i < a.Count; i++)
                if (!string.Equals(a[i], b[i], StringComparison.OrdinalIgnoreCase))
                    return false;
            return true;
        }

        private static bool IsCheckedName(CheckedListBox chk, string name)
        {
            for (int i = 0; i < chk.Items.Count; i++)
                if (string.Equals(chk.Items[i].ToString(), name, StringComparison.OrdinalIgnoreCase) && chk.GetItemChecked(i))
                    return true;
            return false;
        }

        /// <summary>Loads the parallel slots list (comma-int) keyed by the screen's program names.</summary>
        private static void LoadScreenSlots(Dictionary<string, int> slots, List<string> names, string csv)
        {
            slots.Clear();
            var parts = csv.Split(',');
            for (int i = 0; i < names.Count; i++)
            {
                if (i < parts.Length && int.TryParse(parts[i].Trim(), out int v) && v >= 0)
                    slots[names[i]] = v;
            }
        }

        private static string SaveScreenSlots(List<string> names, Dictionary<string, int> slots)
        {
            var vals = new string[names.Count];
            for (int i = 0; i < names.Count; i++)
                vals[i] = slots.TryGetValue(names[i], out int v) ? v.ToString() : "-1";
            return string.Join(",", vals);
        }

        private static int[] SlotsArray(List<string> names, Dictionary<string, int> slots)
        {
            var arr = new int[names.Count];
            for (int i = 0; i < names.Count; i++)
                arr[i] = slots.TryGetValue(names[i], out int v) ? v : -1;
            return arr;
        }

        /// <summary>
        /// Loads the id→cell pin map, including pins of rows that are currently unchecked.
        /// ScreenNSlots only carries checked rows, so without this a row that was unchecked
        /// lost its cell on Save and its neighbours shifted into the freed tile on reload.
        /// </summary>
        private static void LoadSlotPins(string csv, Dictionary<string, int> slots)
        {
            if (string.IsNullOrWhiteSpace(csv))
                return;
            foreach (string token in SplitConfigList(csv))
            {
                int eq = token.IndexOf('=');
                if (eq <= 0 || eq == token.Length - 1)
                    continue;
                string id = token.Substring(0, eq);
                if (int.TryParse(token.Substring(eq + 1), out int cell) && cell >= 0)
                    slots[id] = cell;
            }
        }

        /// <summary>
        /// Loads cells the user cleared on a screen. A stale pin claiming a cleared cell is
        /// dropped so config written by an older build cannot refill a cell the user emptied.
        /// </summary>
        private static void LoadReservedCells(string csv, HashSet<int> cells, Dictionary<string, int> slots)
        {
            cells.Clear();
            if (string.IsNullOrWhiteSpace(csv))
                return;
            foreach (string token in SplitConfigList(csv))
            {
                if (!int.TryParse(token.Trim(), out int cell) || cell < 0)
                    continue;
                cells.Add(cell);
                foreach (var kv in new List<KeyValuePair<string, int>>(slots))
                    if (kv.Value == cell)
                        slots.Remove(kv.Key);
            }
        }

        /// <summary>Writes cleared cell indexes in ascending order as a CSV list.</summary>
        private static string SaveReservedCells(HashSet<int> cells)
        {
            if (cells == null || cells.Count == 0)
                return "";
            var indexes = cells.ToList();
            indexes.Sort();
            return string.Join(",", indexes);
        }

        /// <summary>Parses a persisted reserved-cell CSV into a set.</summary>
        private static HashSet<int> ParseReservedCellSet(string csv)
        {
            var cells = new HashSet<int>();
            if (string.IsNullOrWhiteSpace(csv))
                return cells;
            foreach (string token in SplitConfigList(csv))
                if (int.TryParse(token.Trim(), out int cell) && cell >= 0)
                    cells.Add(cell);
            return cells;
        }

        /// <summary>Stable array form used by the layout resolver and the grid preview.</summary>
        internal static int[] ReservedArray(HashSet<int> cells)
        {
            if (cells == null || cells.Count == 0)
                return new int[0];
            var indexes = cells.ToList();
            indexes.Sort();
            return indexes.ToArray();
        }

        /// <summary>Highest pinned OR cleared cell + 1, so an auto grid keeps its full shape.</summary>
        internal static int ReservedCount(HashSet<int> cells, Dictionary<string, int> slots)
        {
            int max = ReservedCellCount(slots) - 1;
            if (cells != null)
                foreach (int cell in cells)
                    if (cell > max)
                        max = cell;
            return max + 1;
        }

        /// <summary>Writes every surviving pin (checked or not) as <c>id=cell</c> pairs.</summary>
        private static string SaveSlotPins(CheckedListBox chk, Dictionary<string, int> slots)
        {
            var parts = new List<string>();
            foreach (object item in chk.Items)
            {
                string id = item.ToString();
                if (slots.TryGetValue(id, out int cell) && cell >= 0)
                    parts.Add(id + "=" + cell);
            }
            return string.Join(",", parts);
        }

        private static int[] SlotsFromConfig(string csv, int count)
        {
            // Missing/empty slots mean "auto-fill": returning null here made the position loop
            // dereference a null array for a screen whose ScreenNSlots key was never written.
            var arr = new int[count];
            for (int i = 0; i < count; i++)
                arr[i] = -1;

            if (string.IsNullOrWhiteSpace(csv))
                return arr;

            var parts = csv.Split(',');
            for (int i = 0; i < count; i++)
                if (i < parts.Length && int.TryParse(parts[i].Trim(), out int v))
                    arr[i] = v;
            return arr;
        }

        /// <summary>
        /// Grid dimension in 1..3. Legacy configs used 0 for "auto"; that value is normalised
        /// to 1 here so no path can produce a grid with fewer than one cell.
        /// </summary>
        internal static int GridValue(string text) =>
            int.TryParse(text, out int v) ? Math.Max(1, Math.Min(MAX_GRID_DIM, v)) : 1;

        /// <summary>
        /// Picks the smallest 1..3 x 1..3 grid that fits <paramref name="required"/> cells.
        /// Ties prefer smaller area, then a shape closer to square, then preserve current orientation.
        /// </summary>
        internal static void SuggestGridSize(int required, int currentRows, int currentCols,
                                             out int rows, out int cols)
        {
            currentRows = Math.Max(1, Math.Min(MAX_GRID_DIM, currentRows));
            currentCols = Math.Max(1, Math.Min(MAX_GRID_DIM, currentCols));
            required = Math.Max(1, Math.Min(MAX_GRID_DIM * MAX_GRID_DIM, required));
            int bestR = currentRows, bestC = currentCols, bestArea = int.MaxValue, bestSkew = int.MaxValue;
            bool bestKeep = false;
            for (int r = 1; r <= MAX_GRID_DIM; r++)
                for (int c = 1; c <= MAX_GRID_DIM; c++)
                {
                    int area = r * c;
                    if (area < required)
                        continue;
                    int skew = Math.Abs(r - c);
                    bool keep = r == currentRows && c == currentCols;
                    bool better = area < bestArea
                                  || (area == bestArea && skew < bestSkew)
                                  || (area == bestArea && skew == bestSkew && keep && !bestKeep);
                    if (!better)
                        continue;
                    bestR = r; bestC = c; bestArea = area; bestSkew = skew; bestKeep = keep;
                }
            rows = bestR;
            cols = bestC;
        }

        /// <summary>
        /// Hybrid FR-04: manual 1..3 stays, but checking/assigning beyond current capacity grows
        /// the grid to the smallest size that fits (max 3x3) so apps never overflow into the same
        /// cell. Clearing/unchecking never shrinks the matrix (FR-01).
        /// </summary>
        private void EnsureGridCapacity(int screenIndex, int requiredCount)
        {
            if (_gridRowsFields == null || _gridColsFields == null
                || screenIndex < 0 || screenIndex >= _gridRowsFields.Length)
                return;
            if (requiredCount <= 0)
                return;

            int rows = GridValue(_gridRowsFields[screenIndex].Text);
            int cols = GridValue(_gridColsFields[screenIndex].Text);
            if (rows * cols >= requiredCount)
                return;
            if (requiredCount > MAX_GRID_DIM * MAX_GRID_DIM)
                return; // can't fit; ValidateGrid will reject save

            SuggestGridSize(requiredCount, rows, cols, out int nr, out int nc);
            // Setting Text triggers TextChanged -> clamp/refresh/publish, so just update fields.
            if (nr != rows)
                _gridRowsFields[screenIndex].Text = nr.ToString();
            if (nc != cols)
                _gridColsFields[screenIndex].Text = nc.ToString();
        }

        /// <summary>
        /// Rebuilds every screen preview. Deliberately not wrapped in
        /// <see cref="WithoutRedraw"/>: suspending WM_SETREDRAW on the whole Arrange tab and
        /// then forcing an Update() made one checkbox click repaint all four lists and both
        /// grids, which read as flicker. Each grid already skips its repaint when its model is
        /// unchanged, so refreshing all four is cheap.
        /// </summary>
        private void RefreshAllGrids()
        {
            ApplyScreenSectionEnableGate();
            if (_screenChecklists != null)
            {
                for (int s = 0; s < _screenChecklists.Length; s++)
                    EnsureGridCapacity(s, GetCheckedNamesInClickOrder(_screenChecklists[s]).Count);
                for (int s = 0; s < _screenChecklists.Length; s++)
                    RefreshScreenGrid(s);
            }
            PublishLiveScreenPlan();
        }

        /// <summary>Refreshes one screen's grid preview/badges (0-based index).</summary>
        private void RefreshScreenGrid(int screen)
        {
            switch (screen)
            {
                case 0:
                    RefreshGrid(checkedScreen1, gridMap1, _screenSlot1, GridValue(txtGridRows1.Text), GridValue(txtGridCols1.Text), ScreenReservedFor(0));
                    break;
                case 1:
                    RefreshGrid(checkedScreen2, gridMap2, _screenSlot2, GridValue(txtGridRows2.Text), GridValue(txtGridCols2.Text), ScreenReservedFor(1));
                    break;
                case 2:
                    RefreshGrid(checkedScreen3, gridMap3, _screenSlot3, GridValue(txtGridRows3.Text), GridValue(txtGridCols3.Text), ScreenReservedFor(2));
                    break;
                case 3:
                    RefreshGrid(checkedScreen4, gridMap4, _screenSlot4, GridValue(txtGridRows4.Text), GridValue(txtGridCols4.Text), ScreenReservedFor(3));
                    break;
            }
        }

        /// <summary>
        /// Publishes the current Arrange tab as an immutable snapshot. Until this first runs the
        /// position keeper falls back to app.config; after it runs, an unchecked ScreenN row or
        /// Enable box is honoured on the very next cycle without a Save.
        /// </summary>
        private void PublishLiveScreenPlan()
        {
            if (_screenChecklists == null || _screenChecklists.Length < 4
                || _autoFitChecks == null || _gridRowsFields == null)
                return;

            var plan = new ScreenPlan();
            for (int s = 0; s < 4; s++)
            {
                List<string> names = GetCheckedNamesInClickOrder(_screenChecklists[s]);
                plan.Lists[s] = names;
                plan.Slots[s] = SlotsArray(names, ScreenSlotsFor(s));
                plan.AutoFit[s] = _autoFitChecks[s].Checked;
                plan.Borderless[s] = _borderlessChecks[s].Checked;
                plan.Rows[s] = GridValue(_gridRowsFields[s].Text);
                plan.Cols[s] = GridValue(_gridColsFields[s].Text);
                plan.ReservedCells[s] = ReservedCount(ScreenReservedFor(s), ScreenSlotsFor(s));
                plan.EmptyCells[s] = ReservedArray(ScreenReservedFor(s));
            }
            Volatile.Write(ref _liveScreenPlan, plan);
        }

        // ─────────────────────────────────────────────────────────────────────
        // Grid cell picker (click a cell → pick which program is pinned to it)
        // ─────────────────────────────────────────────────────────────────────

        private void GridMap1_CellClicked(int cell) =>
            ShowCellPickerFor(0, checkedScreen1, gridMap1, _screenSlot1,
                GridValue(txtGridRows1.Text), GridValue(txtGridCols1.Text), cell);

        private void GridMap2_CellClicked(int cell) =>
            ShowCellPickerFor(1, checkedScreen2, gridMap2, _screenSlot2,
                GridValue(txtGridRows2.Text), GridValue(txtGridCols2.Text), cell);

        private void GridMap3_CellClicked(int cell) =>
            ShowCellPickerFor(2, checkedScreen3, gridMap3, _screenSlot3,
                GridValue(txtGridRows3.Text), GridValue(txtGridCols3.Text), cell);

        private void GridMap4_CellClicked(int cell) =>
            ShowCellPickerFor(3, checkedScreen4, gridMap4, _screenSlot4,
                GridValue(txtGridRows4.Text), GridValue(txtGridCols4.Text), cell);

        private void ShowCellPickerFor(int screen, CheckedListBox chk, GridMapPanel map,
                                       Dictionary<string, int> slots, int rows, int cols, int cell)
        {
            // A disabled control normally receives no mouse input, but the map's CellClicked
            // can also be raised by code (and a stale context menu may still be open). Never
            // let a disconnected screen be assigned from the grid.
            if (chk == null || map == null || !chk.Enabled || !map.Enabled)
                return;

            var names = GetCheckedNamesInClickOrder(chk);
            var menuNames = new List<string>(names);

            // An explicit grid can be laid out before any application is mapped. In that case
            // every gray cell is still clickable, so offer all rows of this screen's list — not
            // just the checked ones — and let choosing one check it and pin it in one step.
            for (int i = 0; i < chk.Items.Count; i++)
            {
                string id = chk.Items[i].ToString();
                if (!string.IsNullOrWhiteSpace(id) && !menuNames.Contains(id, StringComparer.OrdinalIgnoreCase))
                    menuNames.Add(id);
            }
            if (menuNames.Count == 0)
                return;

            WindowMonitor.ResolveLayout(names, rows, cols, SlotsArray(names, slots), out _, out int[] cells,
                                        ReservedCount(ScreenReservedFor(screen), slots),
                                        ReservedArray(ScreenReservedFor(screen)));

            var menu = new ContextMenuStrip();
            menu.MaximumSize = new Size(480, Math.Max(200, Screen.FromPoint(Cursor.Position).WorkingArea.Height - 20));
            if (names.Count > 0)
            {
                menu.Items.Add("Auto-fill (clear pin)").Click += (s, e) =>
                {
                    for (int i = 0; i < names.Count && i < cells.Length; i++)
                        if (cells[i] == cell)
                            slots.Remove(names[i]);
                    ApplyGridPinChange(screen, chk, map, slots, rows, cols, cell);
                };
            }
            menu.Items.Add("Clear this cell (leave empty)").Click += (s, e) =>
                ClearGridCell(screen, chk, map, slots, rows, cols, cell);
            if (names.Count > 0)
                menu.Items.Add(new ToolStripSeparator());

            foreach (var name in menuNames)
            {
                bool occupies = false;
                for (int i = 0; i < names.Count && i < cells.Length; i++)
                {
                    if (cells[i] == cell && string.Equals(names[i], name, StringComparison.OrdinalIgnoreCase))
                    {
                        occupies = true;
                        break;
                    }
                }

                var item = menu.Items.Add((occupies ? "• " : "") + DisplayNameOnlyForId(name));
                item.Click += (s, e) => AssignProgramToCell(screen, chk, map, slots, rows, cols, cell, name);
            }

            menu.Show(Cursor.Position);
        }

        /// <summary>
        /// Picks the smallest reserved-empty cell index that fits inside <paramref name="total"/>.
        /// Returns -1 when none is available.
        /// </summary>
        internal static int SmallestReservedCell(IEnumerable<int> reserved, int total)
        {
            if (reserved == null || total <= 0)
                return -1;
            int cell = int.MaxValue;
            foreach (int c in reserved)
                if (c >= 0 && c < total && c < cell)
                    cell = c;
            return cell == int.MaxValue ? -1 : cell;
        }

        /// <summary>
        /// When a row is checked from the screen list, a cell the user deliberately cleared
        /// (reserved-empty) is the intended target. Pin the row to the smallest-index reserved
        /// empty cell within the current grid and consume the reservation so no duplicate cell
        /// assignment is created.
        /// </summary>
        private void AssignReservedEmptyCell(int screenIndex, string id)
        {
            var reserved = ScreenReservedFor(screenIndex);
            if (reserved == null || reserved.Count == 0 || string.IsNullOrWhiteSpace(id))
                return;
            if (_gridRowsFields == null || _gridColsFields == null
                || screenIndex < 0 || screenIndex >= _gridRowsFields.Length)
                return;

            int rows = GridValue(_gridRowsFields[screenIndex].Text);
            int cols = GridValue(_gridColsFields[screenIndex].Text);
            int cell = SmallestReservedCell(reserved, Math.Max(1, rows * cols));
            if (cell < 0)
                return;

            var slots = ScreenSlotsFor(screenIndex);
            slots.Remove(id);
            slots[id] = cell;
            foreach (var kv in new List<KeyValuePair<string, int>>(slots))
                if (kv.Value == cell && !string.Equals(kv.Key, id, StringComparison.OrdinalIgnoreCase))
                    slots.Remove(kv.Key);
            reserved.Remove(cell);
        }

        /// <summary>
        /// Pins a screen-list row to a cell. If the row was unchecked, check it first so it
        /// enters the live plan; <see cref="ScreenItemCheck"/> keeps its click order and dedup
        /// intact. The pin is then published immediately by <see cref="ApplyGridPinChange"/>.
        /// </summary>
        private void AssignProgramToCell(int screen, CheckedListBox chk, GridMapPanel map,
                                         Dictionary<string, int> slots, int rows, int cols,
                                         int cell, string id)
        {
            int index = chk.Items.IndexOf(id);
            if (index >= 0 && !chk.GetItemChecked(index))
                chk.SetItemChecked(index, true);

            slots.Remove(id);
            slots[id] = cell;
            foreach (var kv in new List<KeyValuePair<string, int>>(slots))
                if (kv.Value == cell && !string.Equals(kv.Key, id, StringComparison.OrdinalIgnoreCase))
                    slots.Remove(kv.Key);
            // Reusing an intentionally empty cell cancels its reservation; otherwise the
            // resolver would keep the new app out of the cell it was just pinned to.
            ScreenReservedFor(screen).Remove(cell);

            ApplyGridPinChange(screen, chk, map, slots, rows, cols, cell);
        }

        /// <summary>
        /// Empties a cell: unchecks its occupant (if any), removes every pin claiming that cell,
        /// and records the cell as deliberately empty. Pin removal alone let an auto grid shrink
        /// (2×2 → 2×1/1×2) and let a different app fill the gap after Save; the reservation keeps
        /// both the shape and the empty tile. The other rows keep their numbers because
        /// <see cref="FreezeGridPins"/> pins them where they already are.
        /// </summary>
        private void ClearGridCell(int screen, CheckedListBox chk, GridMapPanel map,
                                   Dictionary<string, int> slots, int rows, int cols, int cell)
        {
            var names = GetCheckedNamesInClickOrder(chk);
            var preview = PreviewNames(names);
            int[] empty = ReservedArray(ScreenReservedFor(screen));
            WindowMonitor.ResolveLayout(preview, rows, cols, SlotsArray(names, slots),
                                        out _, out int[] cells, ReservedCount(ScreenReservedFor(screen), slots),
                                        empty);

            for (int i = 0; i < names.Count && i < cells.Length; i++)
            {
                if (cells[i] != cell || string.IsNullOrWhiteSpace(preview[i]))
                    continue;
                int index = chk.Items.IndexOf(names[i]);
                if (index >= 0 && chk.GetItemChecked(index))
                    chk.SetItemChecked(index, false); // also schedules a pin freeze for this screen
                break;
            }

            foreach (var kv in new List<KeyValuePair<string, int>>(slots))
                if (kv.Value == cell)
                    slots.Remove(kv.Key);
            ScreenReservedFor(screen).Add(cell);

            ApplyGridPinChange(screen, chk, map, slots, rows, cols, cell);
            Logger.Log("Grid cell cleared: screen " + (screen + 1) + " cell " + cell);
        }

        /// <summary>
        /// A cell pin is an Arrange edit like any other: repaint the preview, republish the
        /// immutable plan the position thread reads, and wake it immediately. Publishing is
        /// essential — Save writes the slots to app.config but the worker never re-read config
        /// while the form is open, so without this the new cell order only took effect after a
        /// full restart.
        /// </summary>
        private void ApplyGridPinChange(int screen, CheckedListBox chk, GridMapPanel map,
                                        Dictionary<string, int> slots, int rows, int cols, int cell)
        {
            RefreshGrid(chk, map, slots, rows, cols, ScreenReservedFor(screen));
            PublishLiveScreenPlan();
            _positionWake.Set();
            Logger.Log("Grid pin changed: screen " + (screen + 1) + " -> cell " + cell);
        }

        /// <summary>
        /// Pure form of the pin freeze, so the exact "uncheck must not renumber its neighbours"
        /// behaviour can be regression-tested without a WinForms desktop session. The frozen
        /// map includes pins of the row being unchecked: it keeps its old cell reserved (and is
        /// restored there if re-checked) while every other row stays where it already was.
        /// </summary>
        internal static Dictionary<string, int> FrozenPins(List<string> ids, List<string> preview,
            int rows, int cols, Dictionary<string, int> slots, int[] emptyCells = null,
            int reservedCells = 0)
        {
            var source = slots ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var frozen = new Dictionary<string, int>(source, StringComparer.OrdinalIgnoreCase);
            if (ids == null || ids.Count == 0)
                return frozen;

            var effectivePreview = preview ?? ids;
            WindowMonitor.ResolveLayout(effectivePreview, rows, cols, SlotsArray(ids, source),
                                        out _, out int[] cells, reservedCells, emptyCells);
            for (int i = 0; i < ids.Count && i < cells.Length; i++)
                if (cells[i] >= 0)
                    frozen[ids[i]] = cells[i];
            return frozen;
        }

        /// <summary>
        /// Freezes every row's current cell into the pin map. Called from ItemCheck BEFORE the
        /// check state is committed, so the layout used is still the one the user sees. In an
        /// explicit rows×cols grid this keeps the shape fixed and lets the unchecked row's cell
        /// stay empty instead of sliding its neighbours into the gap. In an auto grid (0×0) the
        /// retained pins reserve the same number of cells, so the shape does not collapse either.
        /// </summary>
        private void FreezeGridPins(int s)
        {
            if (s < 0 || s >= _screenChecklists.Length || _gridRowsFields == null)
                return;

            var chk = _screenChecklists[s];
            var ids = GetCheckedNamesInClickOrder(chk);
            var slots = ScreenSlotsFor(s);
            if (ids.Count == 0)
            {
                slots.Clear();
                return;
            }

            int rows = GridValue(_gridRowsFields[s].Text);
            int cols = GridValue(_gridColsFields[s].Text);
            var empty = ScreenReservedFor(s);
            var frozen = FrozenPins(ids, PreviewNames(ids), rows, cols, slots,
                                    ReservedArray(empty), ReservedCount(empty, slots));
            slots.Clear();
            foreach (var kv in frozen)
                slots[kv.Key] = kv.Value;
        }

        /// <summary>Highest pinned cell + 1; includes pins of unchecked rows.</summary>
        internal static int ReservedCellCount(Dictionary<string, int> slots)
        {
            int max = -1;
            if (slots != null)
                foreach (var kv in slots)
                    if (kv.Value > max)
                        max = kv.Value;
            return max + 1;
        }

        /// <summary>Highest pinned cell + 1 from the persisted ScreenNSlotPins string.</summary>
        internal static int ReservedCellCount(string pinsCsv)
        {
            int max = -1;
            if (string.IsNullOrWhiteSpace(pinsCsv))
                return 0;
            foreach (string token in SplitConfigList(pinsCsv))
            {
                int eq = token.IndexOf('=');
                if (eq <= 0 || eq == token.Length - 1)
                    continue;
                if (int.TryParse(token.Substring(eq + 1), out int cell) && cell > max)
                    max = cell;
            }
            return max + 1;
        }

        /// <summary>Highest pinned or reserved cell + 1 from persisted ScreenN* strings.</summary>
        internal static int ReservedCount(string pinsCsv, string reservedCsv)
        {
            int max = ReservedCellCount(pinsCsv) - 1;
            if (!string.IsNullOrWhiteSpace(reservedCsv))
                foreach (string token in SplitConfigList(reservedCsv))
                    if (int.TryParse(token.Trim(), out int cell) && cell > max)
                        max = cell;
            return max + 1;
        }

        private void RefreshGrid(CheckedListBox chk, GridMapPanel map, Dictionary<string, int> slots,
                                 int rows, int cols, HashSet<int> emptyCells = null)
        {
            var names = GetCheckedNamesInClickOrder(chk);
            var preview = PreviewNames(names);
            var displayNames = new List<string>(preview.Count);
            for (int i = 0; i < preview.Count; i++)
                displayNames.Add(string.IsNullOrWhiteSpace(preview[i]) ? "" : DisplayNameOnlyForId(preview[i]));

            int[] slotArray = SlotsArray(names, slots);
            int reserved = ReservedCount(emptyCells, slots);
            int[] emptyArray = ReservedArray(emptyCells);
            map.SetModel(preview, slotArray, rows, cols, reserved, emptyArray, displayNames);
            StoreRowCells(chk, preview, slotArray, rows, cols, reserved, emptyArray);
        }

        /// <summary>
        /// Preview list for one screen: a checked Arrange row whose Enable box is off becomes
        /// "" so the grid keeps reserving its cell but draws it empty — exactly the placeholder
        /// the runtime resolver sends to <see cref="WindowMonitor"/>. The parallel slot array
        /// stays aligned with the original ids so a disabled row cannot hand its pinned cell to
        /// a different program.
        /// </summary>
        private List<string> PreviewNames(List<string> ids)
        {
            ResolveScreenPrograms(ids, out List<string> programs, out _, out _, out _);
            var result = new List<string>(ids.Count);
            for (int i = 0; i < ids.Count; i++)
                result.Add(i < programs.Count && string.IsNullOrEmpty(programs[i]) ? "" : ids[i]);
            return result;
        }

        /// <summary>
        /// Recomputes each program's cell and stores name → 1-based cell for the checklist
        /// badge. Uses the same <see cref="WindowMonitor.ResolveLayout"/> the grid panel
        /// paints with, so the badge number/colour always matches its tile.
        /// </summary>
        private void StoreRowCells(CheckedListBox chk, List<string> names, int[] slots,
                                   int rows, int cols, int reservedCells, int[] emptyCells = null)
        {
            WindowMonitor.ResolveLayout(names, rows, cols, slots, out _, out int[] cells,
                                        reservedCells, emptyCells);

            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var active = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < cells.Length && i < names.Count; i++)
                if (!string.IsNullOrEmpty(names[i]))
                {
                    active.Add(names[i]);
                    if (cells[i] >= 0)
                        map[names[i]] = cells[i] + 1;
                }
            _screenActiveIds[chk] = active;

            // Re-fire the Format event only when the badge map actually changed. Toggling
            // FormattingEnabled runs RefreshItems(), which resets the native scroll position to
            // the top and forces a full repaint — doing it unconditionally on every grid refresh
            // (all four screens!) makes the checklist flicker and jump to row 0 on each check.
            if (_screenRowCell.TryGetValue(chk, out Dictionary<string, int> prev) && MapsEqual(prev, map))
                return;

            _screenRowCell[chk] = map;

            int top = chk.TopIndex;
            int sel = chk.SelectedIndex;
            chk.BeginUpdate();
            // Re-fire the Format event (RefreshItems is protected) so the "(N)" display badges
            // follow the freshly computed map without touching the raw item names.
            chk.FormattingEnabled = false;
            chk.FormattingEnabled = true;
            chk.EndUpdate();
            // Restore the scroll/selection the RefreshItems reset just wiped.
            chk.TopIndex = top;
            if (sel >= 0)
                chk.SelectedIndex = sel;
        }

        private static bool MapsEqual(Dictionary<string, int> a, Dictionary<string, int> b)
        {
            if (a.Count != b.Count)
                return false;
            foreach (var kv in a)
                if (!b.TryGetValue(kv.Key, out int v) || v != kv.Value)
                    return false;
            return true;
        }

        /// <summary>
        /// True when the row may still show a fallback badge: either the preview map has not
        /// been built yet, or the row is a real program (a disabled Arrange row is excluded).
        /// </summary>
        private bool IsActivePreviewId(CheckedListBox chk, string id) =>
            !_screenActiveIds.TryGetValue(chk, out HashSet<string> active) || active.Contains(id);

        /// <summary>
        /// Cell colour for a checklist row, read live at paint time by
        /// <see cref="ColoredCheckedListBox"/>. A checked row that owns a cell returns that
        /// cell's tile colour (same table the grid panel paints with); anything else is blank.
        /// </summary>
        private Color RowColorFor(CheckedListBox chk, int index)
        {
            if (index < 0 || index >= chk.Items.Count || !chk.GetItemChecked(index))
                return Color.Empty;
            if (!_screenRowCell.TryGetValue(chk, out Dictionary<string, int> map)
                || !map.TryGetValue(chk.Items[index].ToString(), out int cellNo))
                return Color.Empty;

            var palette = (cellNo - 1) % Class.GridMapPanel.CellColors.Length;
            return Class.GridMapPanel.CellColors[palette];
        }

        // ─────────────────────────────────────────────────────────────────────
        // Screen-list select-time dedup
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Handles ItemCheck on any screen's checklist. Checks the process identity
        /// (exe + args) from the row's persistent id and unchecks matching items on other screens so a
        /// program is never checked on two screens simultaneously. Gray state itself is not
        /// mutated here; it's always recomputed from scratch afterward (see
        /// <see cref="RecomputeDedupGrayed"/>) so it can never drift from actual checked state.
        /// </summary>
        private void ScreenItemCheck(object sender, ItemCheckEventArgs e)
        {
            if (_suppressingDedup)
                return;

            var source = (CheckedListBox)sender;
            string id = source.Items[e.Index].ToString();
            int sourceIndex = ScreenIndexOf(source);
            var affected = new HashSet<int> { sourceIndex };

            // Grayed by another screen — not checkable
            if (IsGrayedOnScreen(sourceIndex, id))
            {
                e.NewValue = e.CurrentValue;
                return;
            }

            // ItemCheck fires BEFORE the native control commits e.NewValue. Freeze now, while
            // the layout still includes the row being unchecked; freezing afterwards (as before)
            // computed the pins from the already-reflowed grid and made the survivors shift.
            if (e.NewValue == CheckState.Unchecked)
            {
                FreezeGridPins(sourceIndex);
                ScreenSlotsFor(sourceIndex).Remove(id);
            }

            if (e.NewValue == CheckState.Checked)
            {
                // One program (exe + args) may occupy only one screen, even when two rows
                // mirror it. Identity is the process, not the persistent row id.
                string identity = ProcessIdentityForId(id);
                if (!string.IsNullOrEmpty(identity))
                {
                    _suppressingDedup = true;
                    try
                    {
                        for (int s = 0; s < _screenChecklists.Length; s++)
                        {
                            var chk = _screenChecklists[s];
                            if (chk == source)
                                continue;

                            for (int i = 0; i < chk.Items.Count; i++)
                            {
                                string otherId = chk.Items[i].ToString();
                                if (string.Equals(otherId, id, StringComparison.OrdinalIgnoreCase))
                                    continue;
                                if (!string.Equals(ProcessIdentityForId(otherId), identity, StringComparison.OrdinalIgnoreCase))
                                    continue;

                                if (chk.GetItemChecked(i))
                                {
                                    // Same pre-commit freeze for a row this dedup pass is about
                                    // to uncheck on another screen.
                                    FreezeGridPins(s);
                                    chk.SetItemChecked(i, false);
                                    ScreenSlotsFor(s).Remove(otherId);
                                    if (_screenCheckOrder.TryGetValue(chk, out List<string> otherOrder))
                                        otherOrder.RemoveAll(n => string.Equals(n, otherId, StringComparison.OrdinalIgnoreCase));
                                    affected.Add(s);
                                }
                            }
                        }
                    }
                    finally
                    {
                        _suppressingDedup = false;
                    }
                }
            }

            // Maintain the per-screen click order: the newly (un)checked id leaves every
            // list's order (covers the dedup auto-unchecks above), then a fresh check is
            // appended to the source list so auto-fill badge numbers sequence by click order.
            for (int s = 0; s < _screenChecklists.Length; s++)
                if (_screenCheckOrder.TryGetValue(_screenChecklists[s], out List<string> order))
                    order.RemoveAll(n => string.Equals(n, id, StringComparison.OrdinalIgnoreCase));

            bool checkedNow = e.NewValue == CheckState.Checked;
            if (checkedNow)
            {
                _screenCheckOrder[source].Add(id);
                AssignReservedEmptyCell(sourceIndex, id);
            }

            // Deferred so it runs after this event finishes and the CheckedListBox has
            // actually committed e.NewValue to the source item. Only the source screen (and any
            // other screen the dedup pass auto-unchecked) is refreshed; repainting all four
            // grids on every click was the visible flicker.
            BeginInvoke(new Action(() =>
            {
                if (IsDisposed || !IsHandleCreated)
                    return;
                if (checkedNow)
                    EnsureGridCapacity(sourceIndex, GetCheckedNamesInClickOrder(source).Count);
                RecomputeDedupGrayed();
                // Pins were already frozen in ItemCheck, before the check state changed. Only
                // repaint here; freezing again now would lock in the reflowed layout.
                // Graying changes on the other screens too; repaint just the lists (they are
                // double-buffered) instead of the whole Arrange tab.
                foreach (var list in _screenChecklists)
                    list.Invalidate();
                foreach (int s in affected)
                    RefreshScreenGrid(s);
                PublishLiveScreenPlan();
                _positionWake.Set();
                Logger.Log("Arrange check changed: screen " + (sourceIndex + 1)
                           + " checked=" + GetCheckedNamesInClickOrder(source).Count);
            }));
        }

        /// <summary>
        /// Rebuilds <see cref="_dedupGrayed"/> from scratch against the checklists' current
        /// checked state: an item is grayed on screen <c>s</c> exactly when the same identity
        /// (name+args) is currently checked on some other screen. Always deriving this fresh
        /// — instead of accumulating it incrementally — means it can never carry a stale entry
        /// forward from a disabled screen's earlier state, a config reload, or a previous
        /// session; call after any bulk repopulation (<see cref="PopulateScreen"/> call sites)
        /// as well as after <see cref="ScreenItemCheck"/> reacts to a user click.
        /// </summary>
        private void RecomputeDedupGrayed()
        {
            for (int s = 0; s < _screenChecklists.Length; s++)
            {
                var grayed = _dedupGrayed[s] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var chk = _screenChecklists[s];
                for (int i = 0; i < chk.Items.Count; i++)
                {
                    string id = chk.Items[i].ToString();
                    string identity = ProcessIdentityForId(id);
                    if (string.IsNullOrEmpty(identity))
                        continue;
                    bool checkedElsewhere = false;

                    for (int s2 = 0; s2 < _screenChecklists.Length && !checkedElsewhere; s2++)
                    {
                        if (s2 == s)
                            continue;
                        var other = _screenChecklists[s2];
                        for (int j = 0; j < other.Items.Count; j++)
                        {
                            if (!other.GetItemChecked(j))
                                continue;
                            if (!string.Equals(ProcessIdentityForId(other.Items[j].ToString()),
                                               identity, StringComparison.OrdinalIgnoreCase))
                                continue;
                            checkedElsewhere = true;
                            break;
                        }
                    }

                    if (checkedElsewhere)
                        grayed.Add(id);
                }
            }
        }

        /// <summary>
        /// Formats one screen-checklist row's display text. A checked row owns a grid cell, so
        /// it is prefixed with "(N)" — its 1-based tile number — with the name following, so the
        /// checklist reads as a legend for the grid map. Rows with no cell (unchecked, or the
        /// grid is too small) keep their plain name. Implemented as display text because .NET
        /// Framework's CheckedListBox has no owner-draw support (see ctor).
        /// </summary>
        private void ScreenList_Format(object sender, ListControlConvertEventArgs e)
        {
            var chk = (CheckedListBox)sender;
            string id = e.ListItem as string;
            if (id == null)
                return;

            string label = DisplayForId(id);
            bool gray = IsGrayedOnScreen(ScreenIndexOf(chk), id);

            int cellNo = -1;
            bool hasCell = !gray
                && _screenRowCell.TryGetValue(chk, out var cellMap)
                && cellMap.TryGetValue(id, out cellNo);

            // Belt-and-braces: if the row→cell map is momentarily stale (e.g. the checklist was
            // just repopulated and RefreshAllGrids has not run yet), a checked row must still
            // number itself — derived from its position among the checked rows — so it never
            // renders without its cell badge. The normal path is untouched and keeps slot pins.
            if (!hasCell && !gray && IsActivePreviewId(chk, id))
            {
                int index = chk.Items.IndexOf(id);
                if (index >= 0 && chk.GetItemChecked(index))
                {
                    cellNo = IndexOfChecked(chk, index) + 1;
                    hasCell = true;
                }
            }

            e.Value = hasCell ? $"({cellNo}) {label}" : label;
        }

        private int ScreenIndexOf(CheckedListBox chk)
        {
            for (int s = 0; s < _screenChecklists.Length; s++)
                if (_screenChecklists[s] == chk)
                    return s;
            return -1;
        }

        /// <summary>How many items above <paramref name="index"/> are currently checked.</summary>
        private static int IndexOfChecked(CheckedListBox chk, int index)
        {
            int n = 0;
            for (int i = 0; i < index && i < chk.Items.Count; i++)
                if (chk.GetItemChecked(i))
                    n++;
            return n;
        }

        /// <summary>
        /// Null-safe gray-lookup. The per-screen sets are only populated by
        /// <see cref="RecomputeDedupGrayed"/>, so a lazy panel that has not been populated yet
        /// (or a screen index beyond the current array) simply reports "not grayed".
        /// </summary>
        private bool IsGrayedOnScreen(int screenIndex, string name)
        {
            return screenIndex >= 0
                && screenIndex < _dedupGrayed.Length
                && _dedupGrayed[screenIndex] != null
                && _dedupGrayed[screenIndex].Contains(name);
        }

        // ─────────────────────────────────────────────────────────────────────
        // Background thread 1: Process persistence
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>Builds the watchdog identity key for a row's exe and argument tail.</summary>
        private static string WatchdogKey(string baseName, string baseArgs) =>
            (baseName ?? "") + "|" + (baseArgs ?? "");

        /// <summary>
        /// Finds a live process for <paramref name="baseName"/> whose command line matches
        /// <paramref name="baseArgs"/>. A process whose command line cannot be read (cold WMI,
        /// elevated or protected process) is adopted rather than reported as stopped: that
        /// "unknown = dead" reading made the watchdog start another copy on every cycle.
        /// Returns 0 when nothing matches.
        /// </summary>
        private int FindRunningTarget(string baseName, string baseArgs)
        {
            if (string.IsNullOrWhiteSpace(baseName))
                return 0;

            Process[] procs = null;
            int unreadablePid = 0;
            try
            {
                procs = Process.GetProcessesByName(baseName);
                foreach (Process p in procs)
                {
                    if (!string.IsNullOrWhiteSpace(baseArgs))
                    {
                        string cmdLine = _win32Api.GetProcessCommandLine(p.Id);
                        if (string.IsNullOrWhiteSpace(cmdLine))
                        {
                            if (unreadablePid == 0)
                                unreadablePid = p.Id;
                            continue;
                        }
                        if (!WindowMonitor.CommandLineMatchesArgs(cmdLine, baseArgs))
                            continue;
                    }

                    return p.Id;
                }

                return unreadablePid;
            }
            catch (Exception ex)
            {
                Logger.Log($"FindRunningTarget [{baseName}] error: {ex.Message}");
                return 0;
            }
            finally
            {
                if (procs != null)
                    foreach (Process p in procs)
                        p.Dispose();
            }
        }

        /// <summary>
        /// Safe liveness check. Access-denied means the process still exists but cannot be
        /// queried (elevated/protected) and is treated as alive so it is never relaunched.
        /// A recorded start time guards against the pid being recycled by another process.
        /// </summary>
        private static bool IsPidAlive(int pid, long expectedStartTicksUtc)
        {
            if (pid <= 0)
                return false;
            try
            {
                using (Process p = Process.GetProcessById(pid))
                {
                    if (p.HasExited)
                        return false;
                    if (expectedStartTicksUtc > 0)
                    {
                        long current;
                        try { current = p.StartTime.ToUniversalTime().Ticks; }
                        catch { current = 0; }
                        if (current > 0 && current != expectedStartTicksUtc)
                            return false; // pid recycled
                    }
                    return true;
                }
            }
            catch (ArgumentException)        { return false; } // Not running any more
            catch (InvalidOperationException) { return false; } // Exited between calls
            catch (System.ComponentModel.Win32Exception) { return true; } // Access denied
            catch { return false; }
        }

        private static long ProcessStartTicksUtc(int pid)
        {
            if (pid <= 0)
                return 0;
            try
            {
                using (Process p = Process.GetProcessById(pid))
                    return p.StartTime.ToUniversalTime().Ticks;
            }
            catch
            {
                return 0; // Access denied or already exited — caller treats 0 as unknown
            }
        }

        private bool TryGetTrackedAlive(string key)
        {
            lock (_targetsGate)
            {
                TrackedProcess tracked;
                if (_trackedTargets.TryGetValue(key, out tracked))
                {
                    if (IsPidAlive(tracked.Pid, tracked.StartTicksUtc))
                        return true;
                    _trackedTargets.Remove(key);
                }
                return false;
            }
        }

        private void TrackTarget(string key, int pid)
        {
            if (pid <= 0)
                return;
            var tracked = new TrackedProcess
            {
                Pid = pid,
                StartTicksUtc = ProcessStartTicksUtc(pid)
            };
            lock (_targetsGate)
                _trackedTargets[key] = tracked;
        }

        /// <summary>
        /// Claims the right to launch <paramref name="key"/> for the next 30 seconds.
        /// Returns false while another row (or the previous cycle) still owns that window.
        /// </summary>
        private bool TryBeginLaunch(string key, DateTime nowUtc)
        {
            lock (_targetsGate)
            {
                DateTime notBefore;
                if (_launchNotBeforeUtc.TryGetValue(key, out notBefore) && notBefore > nowUtc)
                    return false;
                _launchNotBeforeUtc[key] = nowUtc.AddSeconds(30);
                return true;
            }
        }

        /// <summary>Releases a launch window early when another row already started the target.</summary>
        private void ReleaseLaunch(string key)
        {
            lock (_targetsGate)
                _launchNotBeforeUtc.Remove(key);
        }

        /// <summary>
        /// Pushes a launch window further out. Used when a ClickOnce hand-off has not produced
        /// the deployed process yet: launching again during the deployment just piles up
        /// another rundll32/dfshim attempt on every cycle.
        /// </summary>
        private void ExtendLaunch(string key, int seconds)
        {
            lock (_targetsGate)
                _launchNotBeforeUtc[key] = DateTime.UtcNow.AddSeconds(seconds);
        }

        /// <summary>True for a ClickOnce launch command (dfshim/rundll32 or the legacy IE form).</summary>
        internal static bool IsClickOnceLaunch(string fullCommand)
        {
            if (string.IsNullOrWhiteSpace(fullCommand))
                return false;
            if (fullCommand.StartsWith("rundll32.exe", StringComparison.OrdinalIgnoreCase)
                && fullCommand.IndexOf("dfshim.dll", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            return fullCommand.IndexOf(".application", StringComparison.OrdinalIgnoreCase) >= 0
                && fullCommand.IndexOf("http", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static HashSet<int> SnapshotProcessIds(string baseName)
        {
            var ids = new HashSet<int>();
            if (string.IsNullOrWhiteSpace(baseName))
                return ids;

            Process[] procs = null;
            try
            {
                procs = Process.GetProcessesByName(baseName);
                foreach (Process p in procs)
                    ids.Add(p.Id);
            }
            catch (Exception ex)
            {
                Logger.Log($"SnapshotProcessIds [{baseName}] error: {ex.Message}");
            }
            finally
            {
                if (procs != null)
                    foreach (Process p in procs)
                        p.Dispose();
            }
            return ids;
        }

        /// <summary>
        /// Returns a process of <paramref name="baseName"/> that did not exist before we
        /// launched the target. This adopts launcher/handler processes whose command line
        /// does not match the configured args (so they would otherwise be relaunched), while
        /// still leaving a genuinely second session free to start later.
        /// </summary>
        private static int FindNewProcessId(string baseName, HashSet<int> before)
        {
            if (string.IsNullOrWhiteSpace(baseName))
                return 0;

            Process[] procs = null;
            try
            {
                procs = Process.GetProcessesByName(baseName);
                foreach (Process p in procs)
                    if (before == null || !before.Contains(p.Id))
                        return p.Id;
                return 0;
            }
            catch (Exception ex)
            {
                Logger.Log($"FindNewProcessId [{baseName}] error: {ex.Message}");
                return 0;
            }
            finally
            {
                if (procs != null)
                    foreach (Process p in procs)
                        p.Dispose();
            }
        }

        /// <summary>
        /// Returns any live process with this image name. Last-resort adoption after a launch
        /// produced no new process: single-instance apps hand off to an already-running copy and
        /// the launcher exits, which used to look like "still not running" every cycle.
        /// </summary>
        private static int FindAnyProcessId(string baseName)
        {
            if (string.IsNullOrWhiteSpace(baseName))
                return 0;

            Process[] procs = null;
            try
            {
                procs = Process.GetProcessesByName(baseName);
                foreach (Process p in procs)
                    return p.Id;
                return 0;
            }
            catch (Exception ex)
            {
                Logger.Log($"FindAnyProcessId [{baseName}] error: {ex.Message}");
                return 0;
            }
            finally
            {
                if (procs != null)
                    foreach (Process p in procs)
                        p.Dispose();
            }
        }

        /// <summary>
        /// Long-running loop that runs on <see cref="_keepRun"/>. Every
        /// <see cref="monitorTimer"/> milliseconds it checks whether each configured program
        /// is still running and, if not, starts it. When resource monitoring is enabled
        /// (admin only), it also kills and restarts programs that are unresponsive or have
        /// exceeded their CPU/memory thresholds.
        /// </summary>
        private async Task OnMonitorEvent()
        {
            while (running)
            {
                try
                {
                    // Read the live UI snapshot the form publishes after every edit. It wins over
                    // the config file, so an un-saved Enable toggle (or Add/Remove) changes what
                    // runs on this cycle; Save only makes it permanent.
                    List<ProgramEntry> snapshot = Volatile.Read(ref _livePrograms);
                    var rows = new List<LaunchTargets.Entry>();
                    if (snapshot != null)
                    {
                        foreach (var e in snapshot)
                            rows.Add(new LaunchTargets.Entry(e.Name, e.Path, e.Enabled));
                    }
                    else
                    {
                        // Very early startup, before the form built its lists.
                        string[] names = GetConfigValue("ProgramName", "").Split(',');
                        string[] paths = SplitProgramPaths(GetConfigValue("ProgramPath", ""));
                        bool[] enabled = ProgramEnableFlags.Parse(
                            GetConfigValue("ProgramEnabled", ""), names.Length);
                        for (int i = 0; i < names.Length && i < paths.Length; i++)
                            rows.Add(new LaunchTargets.Entry(names[i], paths[i], enabled[i]));
                    }

                    bool.TryParse(ConfigurationManager.AppSettings["EnablePersistRun"], out bool enablePersistRun);
                    bool.TryParse(ConfigurationManager.AppSettings["EnableMonitoring"],   out bool enableMonitoring);

                    if (enablePersistRun)
                    {
                        var tasks = new List<Task>();

                        foreach (var target in LaunchTargets.Filter(rows))
                        {
                            string programName = target.Name;
                            string programPath = target.Path;

                            tasks.Add(Task.Run(async () =>
                            {
                                try
                                {
                                    string fullProgramPath = BuildFullPath(programName, programPath);
                                    if (string.IsNullOrEmpty(fullProgramPath))
                                        return;

                                    ParseNameParts(programName, out string baseName, out string baseArgs);
                                    string watchdogKey = WatchdogKey(baseName, baseArgs);

                                    // Already adopted earlier: the process is alive, so this
                                    // cycle (or a duplicate row) must not start another copy.
                                    if (TryGetTrackedAlive(watchdogKey))
                                        return;

                                    // A target that was already running before TileKeeper
                                    // started is adopted here instead of being relaunched.
                                    int runningPid = FindRunningTarget(baseName, baseArgs);
                                    if (runningPid > 0)
                                    {
                                        TrackTarget(watchdogKey, runningPid);
                                        return;
                                    }

                                    if (!TryBeginLaunch(watchdogKey, DateTime.UtcNow))
                                        return;

                                    // Re-check after taking the slot: another row with the same
                                    // exe+args may have started it while this task waited.
                                    runningPid = FindRunningTarget(baseName, baseArgs);
                                    if (runningPid > 0)
                                    {
                                        TrackTarget(watchdogKey, runningPid);
                                        ReleaseLaunch(watchdogKey);
                                        return;
                                    }

                                    // Snapshot the pids before launching so a hand-off process can
                                    // be told apart from an unrelated one afterwards.
                                    HashSet<int> existedBefore = SnapshotProcessIds(baseName);

                                    Logger.Log($"Relaunch [{programName}] check={baseName} args={baseArgs}");
                                    Process launched = StartProgram(fullProgramPath);
                                    if (launched == null)
                                        return;

                                    int launchedPid = launched.Id;
                                    string launchedName = "";
                                    try { launchedName = launched.ProcessName; } catch { }
                                    launched.Dispose(); // pid is all we need; the app keeps running

                                    // Only track the started process when it is the real target.
                                    // A ClickOnce launch starts rundll32, which exits right after
                                    // handing off to the deployed app; tracking it as the target
                                    // made the app look dead and started another copy.
                                    bool launchedIsTarget = string.Equals(launchedName, baseName,
                                                                          StringComparison.OrdinalIgnoreCase);
                                    if (launchedIsTarget)
                                        TrackTarget(watchdogKey, launchedPid);

                                    // Give the real target up to 30 s to appear. A ClickOnce
                                    // first run or a slow single-instance hand-off regularly
                                    // needs more than one 15 s cycle; "not visible yet" must not
                                    // be treated as dead.
                                    bool adopted = launchedIsTarget && TryGetTrackedAlive(watchdogKey);
                                    for (int attempt = 0; attempt < 20 && !adopted; attempt++)
                                    {
                                        await Task.Delay(1500);

                                        int running = FindRunningTarget(baseName, baseArgs);
                                        if (running <= 0)
                                            running = FindNewProcessId(baseName, existedBefore);
                                        if (running <= 0)
                                            running = FindAnyProcessId(baseName);
                                        if (running > 0)
                                        {
                                            TrackTarget(watchdogKey, running);
                                            adopted = true;
                                            Logger.Log($"Watchdog adopted existing process [{programName}] pid={running} (launch produced no new process)");
                                        }
                                    }

                                    // Still not visible after 30 s: hold the launch slot longer
                                    // for a ClickOnce hand-off, whose first-run deployment can be
                                    // very slow. A normal app keeps the standard 30 s retry.
                                    if (!adopted && IsClickOnceLaunch(fullProgramPath))
                                        ExtendLaunch(watchdogKey, 180);
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"Error in monitoring program: {ex.Message}");
                                }
                            }));
                        }

                        if (tasks.Count > 0)
                            await Task.WhenAll(tasks);
                    }
                }
                catch (Exception ex)
                {
                    // One bad cycle (e.g. corrupted config) must not kill the loop. This task
                    // replaces the old `async void` thread entry, on which any escaping
                    // exception would have terminated the whole process.
                    Logger.Log($"OnMonitorEvent error: {ex}");
                }

                await Task.Delay(monitorTimer);
            }
        }

        /// <summary>
        /// Builds the full executable path string for use with <see cref="StartProgram"/>.
        /// </summary>
        internal static string BuildFullPath(string name, string path)
        {
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(path))
                return string.Empty;

            // New format: already a complete dfshim command — return as-is
            if (path.StartsWith("rundll32.exe", StringComparison.OrdinalIgnoreCase))
                return path;

            // Legacy format: "C:\...\iexplore.exe "http://server/App/App.application""
            // IE is a no-op stub on Windows 11 — rewrite to dfshim so ClickOnce launches directly.
            if (path.Contains("iexplore.exe"))
            {
                int urlStart = path.IndexOf("http", StringComparison.OrdinalIgnoreCase);
                if (urlStart >= 0)
                {
                    string url = path.Substring(urlStart).Trim().Trim('"');
                    return $"rundll32.exe dfshim.dll,ShOpenVerbApplication {url}";
                }
                return path;
            }

            // RDP: mstsc.exe takes the .rdp path as its single argument. Rows added from now on
            // carry the already-quoted path inside Name (that is what gives each .rdp its own
            // command-line identity for Auto Position); legacy rows store it in the Path column.
            // Either way the path must end up quoted so spaces in it do not split into arguments.
            if (path.EndsWith(".rdp", StringComparison.OrdinalIgnoreCase)
                && name.IndexOf("mstsc.exe", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return name.EndsWith(".rdp\"", StringComparison.OrdinalIgnoreCase)
                    ? name
                    : $"{name} \"{path}\"";
            }

            // name may be "exe.exe" alone or "exe.exe <args>" — args can contain characters
            // (quotes, a drive-letter colon) that Path.Combine rejects as illegal path chars,
            // so only the bare exe filename (with extension) is combined with the directory;
            // args are reattached untouched afterward.
            int sp = name.IndexOf(' ');
            string exeFile  = sp < 0 ? name : name.Substring(0, sp);
            string nameArgs = sp < 0 ? ""   : name.Substring(sp + 1).Trim();
            string combined = Path.Combine(path, exeFile);
            return nameArgs.Length > 0 ? $"{combined} {nameArgs}" : combined;
        }

        /// <summary>
        /// Launches a program given its full path, optionally with arguments. Returns the
        /// started process (null on failure) so the watchdog can track its identity.
        /// </summary>
        private Process StartProgram(string fullProgramPath)
        {
            int exeEnd = fullProgramPath.LastIndexOf(".exe", StringComparison.OrdinalIgnoreCase) + 4;

            string programPath   = fullProgramPath.Substring(0, exeEnd);
            string arguments     = fullProgramPath.Substring(exeEnd).Trim();
            string workingDir    = Path.GetDirectoryName(programPath);

            try
            {
                return Process.Start(new ProcessStartInfo
                {
                    FileName         = programPath,
                    Arguments        = arguments,
                    WorkingDirectory = workingDir,
                    UseShellExecute  = true
                });
            }
            catch (Exception ex)
            {
                Logger.Log($"StartProgram failed [{fullProgramPath}]: {ex.Message}");
                return null;
            }
        }        

        // ─────────────────────────────────────────────────────────────────────
        // Background thread 2: Screen-position keeper
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>Looks up a checked screen-list Key's configured args ("" when none/unmatched).</summary>
        private string ArgsForKey(string key)
        {
            string fallback = null;
            foreach (var e in CurrentPositionableEntries())
            {
                if (string.Equals(e.Key, key, StringComparison.OrdinalIgnoreCase))
                    return e.BaseArgs;
                // Legacy config stored the bare process name (or the item is a stale key);
                // remember the first matching BaseName so identity comparisons still work.
                if (fallback == null && string.Equals(e.BaseName, key, StringComparison.OrdinalIgnoreCase))
                    fallback = e.BaseArgs;
            }
            return fallback ?? "";
        }

        /// <summary>
        /// Snapshot of Key → BaseArgs, first match wins (identical to <see cref="ArgsForKey"/>
        /// called once per Key). Build this once per bulk operation: the dedup passes are
        /// O(screens × items × screens × items), and re-reading config on every inner step
        /// was the dominant cost of rebuilding the checklists.
        /// </summary>
        private Dictionary<string, string> BuildKeyArgsLookup()
        {
            var byKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in CurrentPositionableEntries())
            {
                if (!string.IsNullOrWhiteSpace(e.Key) && !byKey.ContainsKey(e.Key))
                    byKey[e.Key] = e.BaseArgs;
                // Alias the bare process name so a screen list written by an older version
                // resolves to the same args for the dedup/gray passes.
                if (!string.IsNullOrWhiteSpace(e.BaseName) && !byKey.ContainsKey(e.BaseName))
                    byKey[e.BaseName] = e.BaseArgs;
            }
            return byKey;
        }

        private static string ArgsForKey(Dictionary<string, string> byKey, string key)
        {
            string args;
            return byKey.TryGetValue(key, out args) ? args : "";
        }

        /// <summary>
        /// Resolves a screen's checked Keys to the real process names/args/match-largest flags to
        /// hand to <see cref="WindowMonitor"/>. A Key with no matching Programs-tab or Position-tab
        /// entry (stale/legacy config value) falls back to treating the Key itself as a bare,
        /// args-less, MainWindowHandle-matched process name.
        /// </summary>
        private void ResolveScreenPrograms(List<string> keys, out List<string> programs, out List<string> args,
                                           out List<bool> matchLargest, out List<int> keptIndexes)
        {
            var entries = new List<PositionTargets.Entry>();
            foreach (var e in CurrentPositionableEntries())
                if (!string.IsNullOrWhiteSpace(e.Id))
                    entries.Add(new PositionTargets.Entry(
                        e.Key, e.BaseName, e.BaseArgs, e.MatchLargestWindow, e.PositionOnly, e.Enabled, e.Id));

            var resolved = PositionTargets.Resolve(keys, entries);
            programs = resolved.Programs;
            args = resolved.Args;
            matchLargest = resolved.MatchLargest;
            keptIndexes = resolved.KeptIndexes;
        }

        /// <summary>
        /// De-lists any program already claimed by an earlier screen, so a program is never
        /// tracked on two screens at once. Screen 0 (index 0) keeps its entries. Called at
        /// config load time, before ScreenAssignment build — Programs/Args/Slots stay parallel
        /// because whole names are removed before the assignment is constructed.
        /// </summary>
        private static void DelistDuplicates(List<List<string>> screens)
        {
            for (int s = 0; s < screens.Count; s++)
            {
                var keep = new List<string>();
                foreach (string name in screens[s])
                {
                    int earlier = -1;
                    for (int e = 0; e < s && earlier < 0; e++)
                        if (screens[e].Contains(name, StringComparer.OrdinalIgnoreCase))
                            earlier = e;
                    if (earlier >= 0)
                        Logger.Log($"Screen{s + 1} de-listed duplicate: {name} (already on Screen{earlier + 1})");
                    else
                        keep.Add(name);
                }
                screens[s] = keep;
            }
        }

        /// <summary>
        /// Long-running loop that runs on <see cref="_keepPosition"/>. Every
        /// <see cref="keepScreenPosTimer"/> milliseconds it re-reads the config and, if custom
        /// positioning is enabled, calls <see cref="WindowMonitor.ManageProgramScreenPosition"/>
        /// to move any out-of-place windows back to their designated screen and grid cell.
        /// </summary>
        private void OnMonitorPositionEvent()
        {
            while (running)
            {
                try
                {
                    bool.TryParse(ConfigurationManager.AppSettings["EnableCustomPosition"], out bool enableCustomPos);

                    if (enableCustomPos)
                    {
                        // Prefer the snapshot published by the Arrange tab: unchecking a ScreenN
                        // row must stop repositioning on the next pass, without waiting for Save.
                        // Use config only before the form has built its controls.
                        ScreenPlan plan = Volatile.Read(ref _liveScreenPlan);
                        int monitorCount = Math.Min(4, _screens.Length);

                        // Keep one slot per detected monitor (up to 4) even when a screen's list
                        // is empty, so ScreenN always binds to the Nth detected monitor.
                        var screenLists = new List<List<string>>();
                        var originalLists = new List<List<string>>();
                        for (int s = 0; s < monitorCount; s++)
                        {
                            List<string> names = plan != null && s < plan.Lists.Length && plan.Lists[s] != null
                                ? new List<string>(plan.Lists[s])
                                : SplitConfigList(GetConfigValue("Screen" + (s + 1), ""));
                            originalLists.Add(new List<string>(names));
                            screenLists.Add(names);
                        }
                        DelistDuplicates(screenLists);

                        var assignments = new List<ScreenAssignment>();
                        for (int i = 0; i < screenLists.Count; i++)
                        {
                            bool autoFit;
                            if (plan != null)
                                autoFit = plan.AutoFit[i];
                            else
                                bool.TryParse(ConfigurationManager.AppSettings["Screen" + (i + 1) + "AutoFit"], out autoFit);

                            bool borderless = plan != null
                                ? plan.Borderless[i]
                                : GetConfigValue("Screen" + (i + 1) + "Borderless", "True") == bool.TrueString;

                            ResolveScreenPrograms(screenLists[i], out List<string> programs, out List<string> args,
                                                  out List<bool> matchLargest, out List<int> keptIndexes);
                            int[] configuredSlots = plan != null && plan.Slots[i] != null
                                ? plan.Slots[i]
                                : SlotsFromConfig(GetConfigValue("Screen" + (i + 1) + "Slots", ""), originalLists[i].Count);
                            var slots = new int[programs.Count];
                            for (int k = 0; k < keptIndexes.Count && k < slots.Length; k++)
                            {
                                int index = keptIndexes[k];
                                if (index < 0 || index >= screenLists[i].Count)
                                    continue;
                                // DelistDuplicates may have dropped a row; map its surviving name
                                // back to the original slot position instead of shifting pins.
                                string key = screenLists[i][index];
                                int original = originalLists[i].FindIndex(
                                    n => string.Equals(n, key, StringComparison.OrdinalIgnoreCase));
                                slots[k] = original >= 0 && original < configuredSlots.Length
                                    ? configuredSlots[original] : -1;
                            }

                            int reserved = plan != null
                                ? plan.ReservedCells[i]
                                : ReservedCount(GetConfigValue("Screen" + (i + 1) + "SlotPins", ""),
                                                GetConfigValue("Screen" + (i + 1) + "ReservedCells", ""));
                            int[] empty = plan != null
                                ? plan.EmptyCells[i]
                                : ReservedArray(ParseReservedCellSet(
                                    GetConfigValue("Screen" + (i + 1) + "ReservedCells", "")));

                            assignments.Add(new ScreenAssignment(
                                programs,
                                autoFit,
                                plan != null ? plan.Rows[i] : GridValue(GetConfigValue("GridRows" + (i + 1), "1")),
                                plan != null ? plan.Cols[i] : GridValue(GetConfigValue("GridCols" + (i + 1), "1")),
                                slots,
                                args,
                                matchLargest,
                                borderless,
                                reserved,
                                empty));
                        }

                        if (assignments.Count > 0)
                            _windowMonitor.ManageProgramScreenPosition(assignments.ToArray());
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log($"OnMonitorPositionEvent error: {ex}");
                }

                // Sleep for the cycle, or wake early when a Borderless toggle (or shutdown) signals.
                _positionWake.WaitOne(keepScreenPosTimer);
            }
        }

        /// <summary>Live Borderless checkbox state for screen index 0..3 (kept up to date by the toggle handler).</summary>
        private bool GetBorderlessForScreen(int index)
        {
            switch (index)
            {
                case 0: return chkBorderless1.Checked;
                case 1: return chkBorderless2.Checked;
                default: return index == 2 ? chkBorderless3.Checked : chkBorderless4.Checked;
            }
        }        
        
        // ─────────────────────────────────────────────────────────────────────
        // Background thread: Display resolution hold (Phase D)
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Long-running loop that runs on <see cref="_displayRes"/>. Every
        /// <c>ResolutionCheckSeconds</c> it clamps any attached display running above the
        /// configured cap back to the largest advertised mode that fits.
        /// </summary>
        private void OnResolutionHoldEvent()
        {
            var manager = new DisplayResolutionManager();

            while (running)
            {
                try
                {
                    bool.TryParse(ConfigurationManager.AppSettings["EnableResolutionHold"], out bool enable);
                    int checkSeconds = int.TryParse(ConfigurationManager.AppSettings["ResolutionCheckSeconds"], out int s) && s > 0
                        ? s
                        : 10;

                    if (enable)
                    {
                        int maxW = int.TryParse(ConfigurationManager.AppSettings["ResolutionWidth"],  out int w) && w > 0 ? w : 1920;
                        int maxH = int.TryParse(ConfigurationManager.AppSettings["ResolutionHeight"], out int h) && h > 0 ? h : 1080;

                        int changed = manager.NormalizeDisplays(maxW, maxH);
                        SetStatus(lblResStatus, changed > 0
                            ? $"Clamped {changed} oversized display(s) to {maxW}x{maxH}"
                            : "All displays within limit");
                    }
                    else
                    {
                        SetStatus(lblResStatus, "Hold disabled");
                    }

                    Thread.Sleep(checkSeconds * 1000);
                }
                catch (Exception ex)
                {
                    Logger.Log($"ResolutionHold error: {ex.Message}");
                    Thread.Sleep(30 * 1000);
                }
            }
        }

        /// <summary>
        /// Marshals a status-string update to the configured label on the UI thread.
        /// </summary>
        private static void SetStatus(Label label, string text)
        {
            try
            {
                if (label.IsDisposed)
                    return;
                label.BeginInvoke(new Action(() => label.Text = text));
            }
            catch
            {
                // Label handle not created yet (form hidden) — nothing to update
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Configuration migration and persistence
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Writes a single key/value pair to the app.config file and refreshes the
        /// in-memory configuration section so subsequent reads see the updated value.
        /// </summary>
        private void SaveSettings(string key, string value)
        {
            Configuration config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);

            if (config.AppSettings.Settings[key] == null)
                config.AppSettings.Settings.Add(key, value);
            else
                config.AppSettings.Settings[key].Value = value;

            config.Save(ConfigurationSaveMode.Modified);
            ConfigurationManager.RefreshSection("appSettings");
        }

        /// <summary>
        /// Writes many keys in one config open/save/refreshed section. Save used to call SaveSettings
        /// for every field, which reopened and refreshed appSettings each time and made the
        /// UI stall; batching keeps the same on-disk format and refresh semantics.
        /// </summary>
        private void SaveSettingsBatch(IEnumerable<KeyValuePair<string, string>> values)
        {
            var items = values?.ToList() ?? new List<KeyValuePair<string, string>>();
            if (items.Count == 0)
                return;

            Configuration config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
            foreach (var pair in items)
            {
                if (string.IsNullOrEmpty(pair.Key))
                    continue;
                if (config.AppSettings.Settings[pair.Key] == null)
                    config.AppSettings.Settings.Add(pair.Key, pair.Value);
                else
                    config.AppSettings.Settings[pair.Key].Value = pair.Value;
            }
            config.Save(ConfigurationSaveMode.Modified);
            ConfigurationManager.RefreshSection("appSettings");
        }

        /// <summary>
        /// Copies the current app.config to <c>%AppData%\TileKeeper\</c> so that
        /// a ClickOnce update (which overwrites the exe directory) does not erase settings.
        /// </summary>
        private void BackupConfig()
        {
            if (Debugger.IsAttached)
                return;

            string configPath  = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None).FilePath;
            if (!System.IO.File.Exists(configPath))
            {
                Logger.Log($"BackupConfig skipped — no config file exists yet at {configPath}");
                return;
            }

            string backupPath  = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "TileKeeper",
                Path.GetFileName(configPath));

            Directory.CreateDirectory(Path.GetDirectoryName(backupPath));
            System.IO.File.Copy(configPath, backupPath, overwrite: true);
        }

        /// <summary>
        /// Restores the app.config from the <c>%AppData%\TileKeeper\</c> backup.
        /// </summary>
        private void RestoreConfig()
        {
            string configPath = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None).FilePath;
            string backupPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "TileKeeper",
                Path.GetFileName(configPath));

            if (System.IO.File.Exists(backupPath))
            {
                System.IO.File.Copy(backupPath, configPath, overwrite: true);
                ConfigurationManager.RefreshSection("appSettings");
            }
        }

        /// <summary>
        /// Restores the backed-up config when the version changes (ClickOnce update),
        /// then adds any keys new in this version so the app never sees a null key.
        /// </summary>
        private void MigrateConfigIfNeeded()
        {
            string currentVersion = (Debugger.IsAttached || !ApplicationDeployment.IsNetworkDeployed)
                ? Application.ProductVersion
                : ApplicationDeployment.CurrentDeployment.CurrentVersion.ToString();

            string previousVersion = Properties.Settings.Default.LastVersion;

            if (currentVersion != previousVersion)
            {
                RestoreConfig();
                Properties.Settings.Default.LastVersion = currentVersion;
                Properties.Settings.Default.Save();
            }

            // Always ensure required keys exist — not just on a version change — so a config
            // file that's missing or was deleted by hand self-heals on the very next launch
            // instead of only after a version bump. EnsureConfigKeys() is a cheap no-op once
            // all keys are already present.
            EnsureConfigKeys();

            this.Text = $"TileKeeper - Version {currentVersion}";
        }

        /// <summary>
        /// Ensures every key used by the current version of the application exists in the
        /// config file. Missing keys are added with an empty default value. The file itself is
        /// guaranteed to already exist by this point — see Program.Main.
        /// </summary>
        private void EnsureConfigKeys()
        {
            var requiredKeys = new List<string>
            {
                "ProgramName", "ProgramPath", "ProgramEnabled", "ProgramId", "CycleSeconds", "EnablePersistRun",
                "PositionProgramName", "PositionProgramMatchLargest", "PositionProgramEnabled", "PositionProgramId",
                "PositionMirrorSeeded",
                "Screen1", "Screen2", "Screen1AutoFit", "Screen2AutoFit",
                "Screen1Borderless", "Screen2Borderless",
                "GridRows1", "GridCols1", "GridRows2", "GridCols2",
                "Screen1Slots", "Screen2Slots",
                "Screen1SlotPins", "Screen2SlotPins",
                "Screen1ReservedCells", "Screen2ReservedCells",
                "CycleKeepScreen", "EnableCustomPosition",
                "CpuUsage", "MemUsage", "EnableMonitoring",
                "EnableResolutionHold", "ResolutionWidth", "ResolutionHeight", "ResolutionCheckSeconds",
                "EnableClickOnceIntranetZone", "IntranetZoneManagedHosts", "EnableTerminateProtection",
                "ClientSettingsProvider.ServiceUri", "ScreenIdsMigrated",
                // Screen3/Screen4 are always saved now (BtnSave_Click no longer gates them on
                // monitor detection), so their keys must always exist too, regardless of how
                // many monitors happen to be connected at startup.
                "Screen3", "Screen3AutoFit", "Screen3Borderless", "GridRows3", "GridCols3", "Screen3Slots", "Screen3SlotPins", "Screen3ReservedCells",
                "Screen4", "Screen4AutoFit", "Screen4Borderless", "GridRows4", "GridCols4", "Screen4Slots", "Screen4SlotPins", "Screen4ReservedCells"
            };

            Configuration config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
            bool changed = false;

            foreach (string key in requiredKeys)
            {
                if (config.AppSettings.Settings[key] == null)
                {
                    config.AppSettings.Settings.Add(key, "");
                    changed = true;
                }
            }

            if (changed)
            {
                config.Save(ConfigurationSaveMode.Modified);
                ConfigurationManager.RefreshSection("appSettings");
            }
        }

        /// <summary>
        /// Registers each configured ClickOnce host in the Windows Local Intranet Zone
        /// (HKCU) so that SmartScreen does not prompt when ClickOnce auto-updates launch.
        /// Does nothing unless the user enabled <c>EnableClickOnceIntranetZone</c>.
        /// </summary>
        private void ApplyIntranetZoneForClickOnceServers()
        {
            if (!IsClickOnceZoneEnabled())
                return;

            string programPaths = GetConfigValue("ProgramPath", "");
            var managed = new HashSet<string>(
                SplitConfigList(GetConfigValue(RemoteDesktopManager.ManagedHostsSettingKey, "")),
                StringComparer.OrdinalIgnoreCase);

            bool changed = false;
            foreach (string path in SplitProgramPaths(programPaths))
            {
                string host = RemoteDesktopManager.ExtractClickOnceHost(path);
                if (string.IsNullOrEmpty(host))
                    continue;

                if (RemoteDesktopManager.AddToIntranetZone(host) && managed.Add(host))
                    changed = true;
            }

            if (changed)
                SaveSettings(RemoteDesktopManager.ManagedHostsSettingKey, string.Join(",", managed));

            UpdateClickOnceZoneStatus();
        }

        /// <summary>True when the user enabled the opt-in Intranet Zone exemption.</summary>
        private bool IsClickOnceZoneEnabled()
        {
            return string.Equals(
                GetConfigValue(RemoteDesktopManager.EnableSettingKey, "False"),
                bool.TrueString,
                StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Removes the Intranet Zone entries this application may have created. Covers
        /// both the tracked host list and the currently configured ClickOnce hosts so a
        /// host removed from the program list can still be cleaned up. Public/user
        /// entries are refused by <see cref="RemoteDesktopManager.RemoveFromIntranetZone(string)"/>.
        /// </summary>
        private int RemoveManagedIntranetZoneEntries()
        {
            var hosts = new HashSet<string>(
                SplitConfigList(GetConfigValue(RemoteDesktopManager.ManagedHostsSettingKey, "")),
                StringComparer.OrdinalIgnoreCase);

            foreach (string path in SplitProgramPaths(GetConfigValue("ProgramPath", "")))
            {
                string host = RemoteDesktopManager.ExtractClickOnceHost(path);
                if (!string.IsNullOrEmpty(host))
                    hosts.Add(host);
            }

            int removed = RemoteDesktopManager.RemoveFromIntranetZone(hosts);
            SaveSettings(RemoteDesktopManager.ManagedHostsSettingKey, "");
            return removed;
        }

        /// <summary>Writes the one-line status under the registry checkbox.</summary>
        private void UpdateClickOnceZoneStatus()
        {
            if (chkEnableClickOnceZone.Checked)
            {
                lblClickOnceZoneStatus.Text = "On - private hosts only";
                lblClickOnceZoneStatus.ForeColor = Color.FromArgb(0, 120, 70);
            }
            else
            {
                lblClickOnceZoneStatus.Text = "Off (default) - registry is not changed";
                lblClickOnceZoneStatus.ForeColor = Color.FromArgb(90, 90, 90);
            }
        }

        private void ChkEnableClickOnceZone_CheckedChanged(object sender, EventArgs e)
        {
            UpdateClickOnceZoneStatus();
        }

        private void BtnCleanupClickOnceZone_Click(object sender, EventArgs e)
        {
            DialogResult answer = MessageBox.Show(
                this,
                "Remove the Intranet Zone entries that TileKeeper created for the configured ClickOnce servers?\r\n\r\n" +
                "Only private/intranet hosts from this configuration are removed; other entries are left untouched.",
                "Clear registry entries",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2);

            if (answer != DialogResult.Yes)
                return;

            int removed = RemoveManagedIntranetZoneEntries();
            UpdateClickOnceZoneStatus();
            MessageBox.Show(
                this,
                removed > 0
                    ? $"{removed} Intranet Zone entries removed."
                    : "No Intranet Zone entries needed to be removed.",
                "Clear registry entries",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        // ─────────────────────────────────────────────────────────────────────
        // Save button
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Persists all form fields to app.config, applies updated timers, and closes
        /// back to the tray. Rejects the save when a screen exceeds its grid capacity.
        /// </summary>
        private void BtnSave_Click(object sender, EventArgs e)
        {
            if (!SaveCurrentSettings())
                return;

            MessageBox.Show("Settings saved successfully!", "Saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
            this.Close(); // OnFormClosing sees a clean snapshot and only hides to the tray
        }

        /// <summary>
        /// Validates and writes every form value to app.config, refreshes the live snapshots
        /// and records the saved-state snapshot. Returns false without touching config when a
        /// grid cannot hold its programs. No dialog and no Close(), so the unsaved-changes
        /// prompt triggered by X can reuse the exact same save path.
        /// </summary>
        private bool SaveCurrentSettings()
        {
            var entries = GetProgramEntries();

            // Validate grid capacity before touching config
            List<string> screen1 = GetCheckedNamesInClickOrder(checkedScreen1);
            List<string> screen2 = GetCheckedNamesInClickOrder(checkedScreen2);
            int rows1 = GridValue(txtGridRows1.Text), cols1 = GridValue(txtGridCols1.Text);
            int rows2 = GridValue(txtGridRows2.Text), cols2 = GridValue(txtGridCols2.Text);

            // Screen3/Screen4 are saved unconditionally now — a program checked on a
            // disconnected screen must still persist when Save is clicked, not just once its
            // monitor is reconnected.
            List<string> screen3 = GetCheckedNamesInClickOrder(checkedScreen3);
            List<string> screen4 = GetCheckedNamesInClickOrder(checkedScreen4);

            if (!ValidateGrid("Screen 1", rows1, cols1, screen1.Count) ||
                !ValidateGrid("Screen 2", rows2, cols2, screen2.Count))
                return false;

            int rows3 = GridValue(txtGridRows3.Text), cols3 = GridValue(txtGridCols3.Text);
            if (!ValidateGrid("Screen 3", rows3, cols3, screen3.Count))
                return false;

            int rows4 = GridValue(txtGridRows4.Text), cols4 = GridValue(txtGridCols4.Text);
            if (!ValidateGrid("Screen 4", rows4, cols4, screen4.Count))
                return false;

            // One config open/save/refreshed section for the whole form. The old code called
            // SaveSettings per key (~40 times), reloading appSettings each time; that was the
            // main source of the long save/show cycle and visible repaint.
            var pending = new List<KeyValuePair<string, string>>();
            void Queue(string key, string value) =>
                pending.Add(new KeyValuePair<string, string>(key, value));

            // Programs list
            Queue("ProgramName", string.Join(",", entries.Select(x => x.Name)));
            Queue("ProgramPath", string.Join(",", entries.Select(x => x.Path)));
            Queue("ProgramEnabled", ProgramEnableFlags.Serialize(entries.Select(x => x.Enabled)));
            Queue("ProgramId", ProgramCatalog.JoinIds(entries));

            // Position-only programs list (added from the Position tab; never launched by Keep Running)
            var positionEntries = GetPositionEntries();
            Queue("PositionProgramName", string.Join(",", positionEntries.Select(x => x.Name)));
            Queue("PositionProgramMatchLargest", string.Join(",", positionEntries.Select(x => x.MatchLargestWindow ? "1" : "0")));
            Queue("PositionProgramEnabled", ProgramEnableFlags.Serialize(positionEntries.Select(x => x.Enabled)));
            Queue("PositionProgramId", ProgramCatalog.JoinIds(positionEntries));
            // The one-time mirror seed has run; a removed Arrange row must not be recreated on
            // the next load while its Keep Running row still exists.
            Queue("PositionMirrorSeeded", bool.TrueString);

            // Process-persistence settings
            Queue("CycleSeconds", txtCycle.Text);
            Queue("EnablePersistRun", chkEnablePersist.Checked.ToString());

            // Screen assignments + grid + slots (screen lists hold persistent row ids)
            Queue("ScreenIdsMigrated", "True");
            Queue("Screen1", string.Join(",", screen1));
            Queue("Screen2", string.Join(",", screen2));
            Queue("Screen1Slots", SaveScreenSlots(screen1, _screenSlot1));
            Queue("Screen2Slots", SaveScreenSlots(screen2, _screenSlot2));
            // Full pin map: keeps a cell reserved even while its row is unchecked.
            Queue("Screen1SlotPins", SaveSlotPins(checkedScreen1, _screenSlot1));
            Queue("Screen2SlotPins", SaveSlotPins(checkedScreen2, _screenSlot2));
            Queue("Screen1ReservedCells", SaveReservedCells(_screenReservedCells[0]));
            Queue("Screen2ReservedCells", SaveReservedCells(_screenReservedCells[1]));
            Queue("Screen1AutoFit",       chkAutoFit1.Checked.ToString());
            Queue("Screen1Borderless",    chkBorderless1.Checked.ToString());
            Queue("Screen2AutoFit",       chkAutoFit2.Checked.ToString());
            Queue("Screen2Borderless",    chkBorderless2.Checked.ToString());
            Queue("GridRows1", rows1.ToString());
            Queue("GridCols1", cols1.ToString());
            Queue("GridRows2", rows2.ToString());
            Queue("GridCols2", cols2.ToString());
            Queue("CycleKeepScreen",      txtCycleScreen.Text);
            Queue("EnableCustomPosition", chkEnableCustPos.Checked.ToString());

            Queue("Screen3", string.Join(",", screen3));
            Queue("Screen3Slots", SaveScreenSlots(screen3, _screenSlot3));
            Queue("Screen3SlotPins", SaveSlotPins(checkedScreen3, _screenSlot3));
            Queue("Screen3ReservedCells", SaveReservedCells(_screenReservedCells[2]));
            Queue("Screen3AutoFit",       chkAutoFit3.Checked.ToString());
            Queue("Screen3Borderless",    chkBorderless3.Checked.ToString());
            Queue("GridRows3", rows3.ToString());
            Queue("GridCols3", cols3.ToString());

            Queue("Screen4", string.Join(",", screen4));
            Queue("Screen4Slots", SaveScreenSlots(screen4, _screenSlot4));
            Queue("Screen4SlotPins", SaveSlotPins(checkedScreen4, _screenSlot4));
            Queue("Screen4ReservedCells", SaveReservedCells(_screenReservedCells[3]));
            Queue("Screen4AutoFit",       chkAutoFit4.Checked.ToString());
            Queue("Screen4Borderless",    chkBorderless4.Checked.ToString());
            Queue("GridRows4", rows4.ToString());
            Queue("GridCols4", cols4.ToString());

            // Display resolution hold
            Queue("EnableResolutionHold", chkEnableRes.Checked.ToString());
            Queue("ResolutionWidth",      txtResWidth.Text);
            Queue("ResolutionHeight",     txtResHeight.Text);
            Queue("ResolutionCheckSeconds", txtResCycle.Text);

            // Apply updated intervals to the running background threads immediately
            monitorTimer       = int.Parse(txtCycle.Text)       * 1000;
            keepScreenPosTimer = int.Parse(txtCycleScreen.Text) * 1000;

            // Registry / Intranet Zone (opt-in). Disabling removes only the entries
            // TileKeeper added, so the checkbox is a real on/off switch.
            Queue(RemoteDesktopManager.EnableSettingKey, chkEnableClickOnceZone.Checked.ToString());
            Queue("EnableTerminateProtection", chkEnableTerminateProtection.Checked.ToString());
            SaveSettingsBatch(pending);
            BackupConfig();
            if (chkEnableClickOnceZone.Checked)
            {
                ApplyIntranetZoneForClickOnceServers();
            }
            else
            {
                int removed = RemoveManagedIntranetZoneEntries();
                UpdateClickOnceZoneStatus();
                if (removed > 0)
                {
                    MessageBox.Show(
                        $"{removed} Intranet Zone entries created by TileKeeper were removed because the option was turned off.",
                        "Registry cleaned up",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
            }

            // The saved values are now the config of record. Republish the snapshot the
            // background loops read, and record the file stamp so reopening from the tray
            // shows this form as-is instead of rebuilding it from disk. The screen plan must be
            // republished too: Save used to refresh only the program lists, leaving the position
            // thread on the pre-save slots until the app was restarted.
            RefreshLiveSnapshot();
            PublishLiveScreenPlan();
            _positionWake.Set();
            RememberConfigStamp();
            RememberSavedConfig();
            return true;
        }

        /// <summary>
        /// Rejects a grid larger than 3×3 or a program count that cannot fit the grid.
        /// </summary>
        private bool ValidateGrid(string screenName, int rows, int cols, int count)
        {
            rows = Math.Max(1, Math.Min(MAX_GRID_DIM, rows));
            cols = Math.Max(1, Math.Min(MAX_GRID_DIM, cols));
            if (rows * cols > MAX_GRID_DIM * MAX_GRID_DIM)
            {
                MessageBox.Show($"{screenName}: grid is limited to {MAX_GRID_DIM}x{MAX_GRID_DIM} (9 cells).",
                    "Save rejected", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }

            int cells = rows * cols;
            if (count > cells)
            {
                MessageBox.Show($"{screenName}: {count} programs exceed the grid {rows}x{cols}.",
                    "Save rejected", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }

            return true;
        }        

        // ─────────────────────────────────────────────────────────────────────
        // Program list buttons (Add / Edit / Remove)
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>Opens a file picker and appends the chosen programs as new rows.</summary>
        private void BtnAdd_Click(object sender, EventArgs e)
        {
            using (var dlg = NewProgramDialog())
            {
                dlg.Multiselect = true;
                if (dlg.ShowDialog() != DialogResult.OK)
                    return;

                foreach (string fullPath in dlg.FileNames)
                    AddProgramRowFromFile(fullPath);
            }          

            RefreshLiveSnapshot();
            RebuildScreenCandidates();
        }

        /// <summary>
        /// Removes the selected Keep Running rows only. The mirrored Arrange rows keep their
        /// screen assignments: deleting a launch entry must not delete positioning settings.
        /// </summary>
        private void BtnRemove_Click(object sender, EventArgs e)
        {
            var selected = listPrograms.SelectedItems.Cast<ListViewItem>().ToList();
            if (selected.Count == 0)
                return;

            var removedIds = selected.Select(RowId)
                                     .Where(id => !string.IsNullOrWhiteSpace(id))
                                     .ToList();
            foreach (var item in selected)
                listPrograms.Items.Remove(item);

            _catalog.RemoveIds(removedIds);
            RefreshLiveSnapshot();
            RebuildScreenCandidates();
            _positionWake.Set();
        }

        // ─────────────────────────────────────────────────────────────────────
        // Position-only program list buttons (Add / Edit / Remove)
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>Opens a file picker and appends the chosen programs as position-only rows.</summary>
        private void BtnAddPosition_Click(object sender, EventArgs e)
        {
            using (var dlg = NewProgramDialog())
            {
                dlg.Multiselect = true;
                if (dlg.ShowDialog() != DialogResult.OK)
                    return;

                foreach (string fullPath in dlg.FileNames)
                    AddPositionRowFromFile(fullPath);
            }
            
            RefreshLiveSnapshot();
            RebuildScreenCandidates();
        }

        /// <summary>Removes the selected position-only rows and their screen assignments.</summary>
        private void BtnRemovePosition_Click(object sender, EventArgs e)
        {
            var selected = listPositionPrograms.SelectedItems.Cast<ListViewItem>().ToList();
            if (selected.Count == 0)
                return;

            // Persistent ids, not indexes: a mirrored row with the same name/args in the
            // Programs list keeps its own screen assignment when this row is removed.
            var removedIds = selected.Select(RowId)
                                     .Where(id => !string.IsNullOrWhiteSpace(id))
                                     .ToList();
            foreach (var item in selected)
                listPositionPrograms.Items.Remove(item);

            _catalog.RemoveIds(removedIds);
            ExpungeFromScreens(removedIds); // writes ScreenN/ScreenNSlots through immediately
            RefreshLiveSnapshot();
            RebuildScreenCandidates();
            _positionWake.Set();
            RememberSavedConfig(); // this removal is already persisted — not an unsaved edit
        }

        /// <summary>
        private void SetProgramListChecked(ListView list, ListViewItem item, bool value)
        {
            if (list == null || item == null)
                return;

            ItemCheckedEventHandler checkedHandler =
                ReferenceEquals(list, listPrograms) ? (ItemCheckedEventHandler)ListPrograms_ItemChecked :
                ReferenceEquals(list, listPositionPrograms) ? (ItemCheckedEventHandler)ListPositionPrograms_ItemChecked :
                null;

            list.ItemCheck -= ListView_CheckboxItemCheck;
            if (checkedHandler != null) list.ItemChecked -= checkedHandler;
            try
            {
                // Force handle creation while guard is detached so WinForms can sync
                // managed Checked states to the native control without being reverted.
                // ListView on an inactive TabPage may not have a handle yet; setting
                // Checked without a handle can be lost when the handle is later created.
                if (!list.IsHandleCreated && list.Parent != null)
                {
                    try { _ = list.Handle; }
                    catch { /* best effort */ }
                }
            }
            finally
            {
                // Reattach ItemChecked before the actual toggle so normal side effects
                // (color, snapshot, grid refresh, wake) run for the intended change.
                if (checkedHandler != null) list.ItemChecked += checkedHandler;
                list.ItemCheck += ListView_CheckboxItemCheck;
            }

            // Toggle with guards bypassed so programmatic set is not blocked.
            list.ItemCheck -= ListView_CheckboxItemCheck;
            try
            {
                if (item.Checked == value)
                {
                    // Still ensure side effects reflect the desired state.
                    if (item.Checked) item.Selected = true;
                    ApplyRowEnabledColor(item);
                }
                else
                {
                    item.Checked = value;
                }
            }
            finally
            {
                list.ItemCheck += ListView_CheckboxItemCheck;
            }

            // Ensure final state and side effects are applied even if no ItemChecked fired.
            if (item.Checked != value)
            {
                list.ItemCheck -= ListView_CheckboxItemCheck;
                try { item.Checked = value; }
                finally { list.ItemCheck += ListView_CheckboxItemCheck; }
            }
            if (item.Checked) item.Selected = true;
            ApplyRowEnabledColor(item);
        }

        /// <summary>
        /// Checkbox state in Keep Running / Programs to Position lists may only change from a
        /// direct checkbox-glyph click. Row selection, drag-select, marquee, keyboard, or any
        /// other interaction must not affect checkbox state.
        /// </summary>
        private void ListView_CheckboxItemCheck(object sender, ItemCheckEventArgs e)
        {
            var list = sender as ListView;
            if (list == null)
                return;
            if (_allowProgramListItemCheck)
                return;
            if (ReferenceEquals(_checkboxClickAuthorizedList, list))
            {
                _checkboxClickAuthorizedList = null;
                return;
            }

            e.NewValue = e.CurrentValue;
        }

        /// <summary>
        /// Only a direct click on the checkbox glyph (StateImage) may change checkbox state.
        /// Any other left press on row text, empty area, or during drag-select only affects
        /// selection; checkbox changes from those interactions are cancelled in ItemCheck.
        /// </summary>
        private void ListView_CheckboxMouseDown(object sender, MouseEventArgs e)
        {
            var list = sender as ListView;
            _checkboxClickAuthorizedList = null;
            if (list == null || e.Button != MouseButtons.Left || list.Columns.Count == 0)
                return;

            ListViewHitTestInfo hit = list.HitTest(e.Location);

            // Only a direct checkbox-glyph click may change checkbox state. Any other
            // interaction (row text, empty area, marquee/drag-select) must not affect checks.
            if (hit.Item == null || hit.Location != ListViewHitTestLocations.StateImage)
                return;

            _checkboxClickAuthorizedList = list;
            foreach (ListViewItem item in list.SelectedItems.Cast<ListViewItem>().ToList())
                if (item != hit.Item)
                    item.Selected = false;
            hit.Item.Selected = true;
        }

        /// <summary>Clears a stale mass selection and leaves only the rows a drop just added.</summary>
        private static void SelectOnlyNewRows(ListView list, int firstNewIndex)
        {
            if (list == null)
                return;
            foreach (ListViewItem item in list.Items)
                item.Selected = false;
            for (int i = firstNewIndex; i < list.Items.Count; i++)
                list.Items[i].Selected = true;
        }

        /// <summary>
        /// Selects a row when its Enable checkbox is checked, so Remove acts on that row
        /// without an extra click, and repaints the row's disabled state. Only checking
        /// selects — unchecking leaves selection alone.
        /// </summary>
        private void ListPrograms_ItemChecked(object sender, ItemCheckedEventArgs e)
        {
            if (e?.Item == null)
                return;
            if (e.Item.Checked)
                e.Item.Selected = true;
            ApplyRowEnabledColor(e.Item);
            RefreshLiveSnapshot();
        }

        /// <summary>
        /// Selects a row when its Enable checkbox is checked, so Remove acts on that row
        /// without an extra click, and repaints the row's disabled state. Only checking
        /// selects — unchecking leaves selection alone.
        /// </summary>
        private void ListPositionPrograms_ItemChecked(object sender, ItemCheckedEventArgs e)
        {
            if (e?.Item == null)
                return;
            if (e.Item.Checked)
                e.Item.Selected = true;
            ApplyRowEnabledColor(e.Item);
            RefreshLiveSnapshot();
            // Unchecking an Arrange row must stop moving that window on the next pass, not
            // only after Save. The resolver reads the refreshed live snapshot. The preview
            // must follow too: a disabled row keeps its cell but it is drawn empty.
            if (!_suppressLiveSnapshotRefresh && IsHandleCreated && !IsDisposed)
                RefreshAllGrids();
            _positionWake.Set();
        }

        /// <summary>Clicking the Match-largest column flips its checkbox glyph, not the Enable box.</summary>
        private void ListPositionPrograms_MouseClick(object sender, MouseEventArgs e)
        {
            ListViewHitTestInfo hit = listPositionPrograms.HitTest(e.Location);
            if (hit.Item == null || hit.SubItem == null)
                return;
            if (hit.Item.SubItems.IndexOf(hit.SubItem) != 2)
                return;

            hit.SubItem.Text = hit.SubItem.Text == MatchLargestOn ? MatchLargestOff : MatchLargestOn;
            hit.Item.Selected = true;
        }

        /// <summary>
        /// Resolves a picked file to a position-only row (name only — nothing is launched from here).
        /// Returns false (and skips silently) when the name is already in the list, so
        /// drag-dropping the same file twice can't create a duplicate row.
        /// </summary>
        private bool AddPositionRowFromFile(string fullPath)
        {
            var (name, _) = ResolveProgramFile(fullPath);
            if (IsSelf(name))
            {
                MessageBox.Show("This program cannot add itself to the list.", "Add",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            if (ListContainsName(listPositionPrograms, name))
                return false;

            ListViewItem item = null;
            _allowProgramListItemCheck = true;
            try
            {
                item = NewPositionRow(name, false, true, ProgramEntry.NewId());
                listPositionPrograms.Items.Add(item);
                SetProgramListChecked(listPositionPrograms, item, true);
            }
            finally { _allowProgramListItemCheck = false; }
            if (item != null) SetProgramListChecked(listPositionPrograms, item, true);
            return true;
        }

        /// <summary>
        /// Case-insensitive search for <paramref name="name"/> among <paramref name="list"/>'s row
        /// text (the row's identity — see AddProgramRowFromFile).
        /// </summary>
        private static bool ListContainsName(ListView list, string name)
        {
            foreach (ListViewItem item in list.Items)
                if (string.Equals(RowName(item), name, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        /// <summary>Accepts a file drag from Explorer/Desktop over a program list.</summary>
        private static void ProgramList_DragEnter(object sender, DragEventArgs e)
        {
            e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        }

        /// <summary>Drops Explorer files into the Programs list as new rows (same handling as Add).</summary>
        private void ListPrograms_DragDrop(object sender, DragEventArgs e)
        {
            if (!(e.Data.GetData(DataFormats.FileDrop) is string[] files))
                return;
            int before = listPrograms.Items.Count;
            foreach (string fullPath in files)
                AddProgramRowFromFile(fullPath);
            // A drop can leave every row selected; the next checkbox click would then toggle
            // them all. Keep only the newly added rows selected.
            SelectOnlyNewRows(listPrograms, before);

            RefreshLiveSnapshot();
            RebuildScreenCandidates();
        }

        /// <summary>Drops Explorer files into the Position list as new rows (same handling as Add).</summary>
        private void ListPositionPrograms_DragDrop(object sender, DragEventArgs e)
        {
            if (!(e.Data.GetData(DataFormats.FileDrop) is string[] files))
                return;
            int before = listPositionPrograms.Items.Count;
            foreach (string fullPath in files)
                AddPositionRowFromFile(fullPath);
            // Same as the Programs list: leave only the new rows selected so the next
            // checkbox click cannot toggle every row in the list.
            SelectOnlyNewRows(listPositionPrograms, before);

            RefreshLiveSnapshot();
            RebuildScreenCandidates();
        }        

        private OpenFileDialog NewProgramDialog()
        {
            return new OpenFileDialog
            {
                Filter = "Shortcut, Application Reference, Remote Desktop, VNC, Executable Files (*.lnk;*.appref-ms;*.rdp;*.vnc;*.exe)|*.lnk;*.appref-ms;*.rdp;*.vnc;*.exe|Shortcut (*.lnk)|*.lnk|Application Reference (*.appref-ms)|*.appref-ms|Remote Desktop (*.rdp)|*.rdp|VNC Connection (*.vnc)|*.vnc|Executable Files (*.exe)|*.exe|All Files (*.*)|*.*",
                Title  = "Select a Program"
            };
        }

        /// <summary>
        /// True when <paramref name="name"/>'s exe base name is this app itself (e.g. picked
        /// TileKeeper.exe by mistake). Adding/monitoring our own process would let the
        /// Keep Running watchdog and window-position keeper act on themselves, so it's rejected.
        /// </summary>
        private static bool IsSelf(string name)
        {
            ParseNameParts(name, out string baseName, out _);
            return string.Equals(baseName, Process.GetCurrentProcess().ProcessName, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Resolves a picked file to a Programs row. Returns false (and skips silently) when the name
        /// is already in the list, so drag-dropping or multi-selecting the same file twice can't
        /// create a duplicate row.
        /// </summary>
        private bool AddProgramRowFromFile(string fullPath)
        {
            var (name, path) = ResolveProgramFile(fullPath);
            if (IsSelf(name))
            {
                MessageBox.Show("This program cannot add itself to the list.", "Add",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            if (ListContainsName(listPrograms, name))
                return false;

            ListViewItem programItem = null;
            ListViewItem mirrorItem = null;
            _allowProgramListItemCheck = true;
            try
            {
                programItem = NewProgramRow(name, path, true, ProgramEntry.NewId());
                listPrograms.Items.Add(programItem);
                SetProgramListChecked(listPrograms, programItem, true);

                // The first Add in Keep Apps Running also makes the program available under
                // Arrange Windows. If the name already exists in Arrange, ensure it is enabled
                // so the freshly added Keep Running app is ready to be positioned; later edits
                // on either list remain independent.
                ListViewItem existingPos = null;
                foreach (ListViewItem it in listPositionPrograms.Items)
                    if (string.Equals(RowName(it), name, StringComparison.OrdinalIgnoreCase))
                    { existingPos = it; break; }
                if (existingPos == null)
                {
                    mirrorItem = NewPositionRow(name, false, true, ProgramEntry.NewId());
                    listPositionPrograms.Items.Add(mirrorItem);
                    SetProgramListChecked(listPositionPrograms, mirrorItem, true);
                }
                else
                {
                    mirrorItem = existingPos;
                    SetProgramListChecked(listPositionPrograms, existingPos, true);
                }
            }
            finally
            {
                _allowProgramListItemCheck = false;
            }
            if (programItem != null) SetProgramListChecked(listPrograms, programItem, true);
            if (mirrorItem != null) SetProgramListChecked(listPositionPrograms, mirrorItem, true);

            return true;
        }

        /// <summary>
        /// Resolves a picked file to the name/path pair stored in the list and config.
        /// </summary>
        private (string Name, string Path) ResolveProgramFile(string fullPath)
        {
            string ext = Path.GetExtension(fullPath).ToLower();

            if (ext == ".lnk")
            {
                string target = GetShortcutTargetPath(fullPath, out string args);
                args = args.Replace("\"", string.Empty);

                // ClickOnce shortcut → store the dfshim launch command directly
                if (target.Contains("iexplore.exe") && args.Contains("http") && args.Contains(".application"))
                    return (Path.GetFileNameWithoutExtension(args),
                            $"rundll32.exe dfshim.dll,ShOpenVerbApplication {args}");
                return (Path.GetFileName(target) + " " + args,
                        Path.GetDirectoryName(target));
            }

            if (ext == ".appref-ms")
            {
                // .appref-ms contains the deployment URL, ending at the '#' fragment separator
                string appRef = System.IO.File.ReadAllText(fullPath);
                string url    = appRef.Substring(appRef.IndexOf("http"), appRef.IndexOf("#") - appRef.IndexOf("http"));
                return (Path.GetFileNameWithoutExtension(new Uri(url).AbsolutePath),
                        $"rundll32.exe dfshim.dll,ShOpenVerbApplication {url}");
            }

            if (ext == ".rdp")
            {
                // mstsc.exe takes the .rdp path as its only argument. Embed the (quoted) path in the
                // stored Name rather than leaving a bare "mstsc.exe": Name is also the per-row
                // identity, so the path becomes BaseArgs — the command-line tail used to tell
                // concurrent mstsc sessions apart when auto-positioning and when auto-running.
                return ($"mstsc.exe \"{fullPath}\"", fullPath);
            }

            if (ext == ".vnc")
            {
                string exePath, argsTemplate;
                if (ResolveVncOpenCommand(out exePath, out argsTemplate))
                {
                    // argsTemplate is the viewer's own invocation syntax (e.g. TightVNC's
                    // -optionsfile="%1") with "%1" standing in for the file — substitute the
                    // real path so StartProgram launches it the same way Explorer would.
                    string args = argsTemplate.Replace("%1", fullPath);
                    return (Path.GetFileName(exePath) + " " + args, Path.GetDirectoryName(exePath));
                }
                // No VNC viewer registered for .vnc files — fall back to the file itself
                // (StartProgram/BuildFullPath won't know how to launch this without a real exe).
                return (Path.GetFileName(fullPath), Path.GetDirectoryName(fullPath));
            }

            return (Path.GetFileName(fullPath), Path.GetDirectoryName(fullPath));
        }

        /// <summary>
        /// Reads the Windows file association for <c>.vnc</c> files (HKCR\.vnc → ProgID →
        /// shell\open\command) and splits it into the viewer's exe path and its own argument
        /// template (still containing the literal "%1" placeholder). Works with whichever VNC
        /// client is installed (TightVNC, UltraVNC, RealVNC, ...) since each registers its own
        /// required syntax — e.g. TightVNC needs <c>-optionsfile="%1"</c>, not a bare path.
        /// Returns false if no viewer is associated or the exe no longer exists.
        /// </summary>
        private static bool ResolveVncOpenCommand(out string exePath, out string argsTemplate)
        {
            exePath = null;
            argsTemplate = null;
            try
            {
                using (var extKey = Microsoft.Win32.Registry.ClassesRoot.OpenSubKey(".vnc"))
                {
                    string progId = extKey?.GetValue("") as string;
                    if (string.IsNullOrEmpty(progId))
                        return false;

                    using (var cmdKey = Microsoft.Win32.Registry.ClassesRoot.OpenSubKey($@"{progId}\shell\open\command"))
                    {
                        string command = cmdKey?.GetValue("") as string;
                        if (string.IsNullOrEmpty(command))
                            return false;

                        // command looks like: "C:\...\tvnviewer.exe" -optionsfile="%1"
                        string rest;
                        if (command.StartsWith("\""))
                        {
                            int quoteEnd = command.IndexOf('"', 1);
                            if (quoteEnd < 0) return false;
                            exePath = command.Substring(1, quoteEnd - 1);
                            rest = command.Substring(quoteEnd + 1).Trim();
                        }
                        else
                        {
                            int sp = command.IndexOf(' ');
                            exePath = sp < 0 ? command : command.Substring(0, sp);
                            rest = sp < 0 ? "" : command.Substring(sp + 1).Trim();
                        }

                        if (!System.IO.File.Exists(exePath))
                            return false;

                        argsTemplate = rest;
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"ResolveVncOpenCommand failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Uses the Windows Script Host COM object to resolve a .lnk shortcut file
        /// to its actual target executable path and any command-line arguments.
        /// </summary>
        private string GetShortcutTargetPath(string shortcutFilePath, out string arguments)
        {
            WshShell shell     = new WshShell();
            IWshShortcut lnk   = (IWshShortcut)shell.CreateShortcut(shortcutFilePath);
            arguments          = lnk.Arguments;
            return lnk.TargetPath;
        }

        // ─────────────────────────────────────────────────────────────────────
        // System tab handlers
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>Opens credits without reloading or changing settings.</summary>
        private void OnAbout(object sender, EventArgs e)
        {
            bool settingsVisible = Visible && WindowState != FormWindowState.Minimized;
            using (var about = new AboutForm())
            {
                about.Icon = Icon;
                about.TopMost = TopMost;
                // Tray-only app: never give the modal credit page its own taskbar button.
                about.ShowInTaskbar = false;
                if (settingsVisible)
                {
                    about.StartPosition = FormStartPosition.CenterParent;
                    about.ShowDialog(this);
                }
                else
                {
                    about.ShowDialog();
                }
            }
        }

        /// <summary>
        /// Captures the primary display's resolution into the Width/Height fields.
        /// Reads the physical pixels from the display driver (EnumDisplaySettings) rather
        /// than <c>Screen.Bounds</c>, which is DPI-virtualized: at 125% scaling a
        /// 1920×1200 panel reports 1536×960 and saving that would clamp the monitor.
        /// </summary>
        private void BtnCaptureRes_Click(object sender, EventArgs e)
        {
            var manager = new DisplayResolutionManager();
            if (!manager.TryGetPrimaryMode(out int width, out int height))
            {
                lblResStatus.Text = "Could not read the primary display mode.";
                return;
            }

            txtResWidth.Text  = width.ToString();
            txtResHeight.Text = height.ToString();
            lblResStatus.Text = $"Captured {width} x {height} (physical pixels).";
        }

        // ─────────────────────────────────────────────────────────────────────
        // Tray icon and window behaviour
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>Path of the exe.config this process loaded.</summary>
        private static string ConfigFilePath()
        {
            try { return AppDomain.CurrentDomain.SetupInformation.ConfigurationFile; }
            catch { return null; }
        }

        /// <summary>Records the config file's last-write time after our own load or save.</summary>
        private void RememberConfigStamp()
        {
            string path = ConfigFilePath();
            try
            {
                _configStampUtc = path != null && System.IO.File.Exists(path)
                    ? System.IO.File.GetLastWriteTimeUtc(path)
                    : DateTime.MinValue;
            }
            catch
            {
                _configStampUtc = DateTime.MinValue;
            }
        }

        /// <summary>
        /// Builds one canonical string of everything BtnSave_Click writes to app.config.
        /// Keeping this beside the file stamp separates two different questions: the stamp
        /// detects an *external* edit when the tray window reopens, while this snapshot
        /// detects edits the user made in this session that have not been saved yet.
        /// </summary>
        private string CaptureConfigSnapshot()
        {
            if (_screenChecklists == null || _gridRowsFields == null || _autoFitChecks == null)
                return "";

            var entries = GetProgramEntries();
            var positionEntries = GetPositionEntries();
            var parts = new List<string>
            {
                string.Join(",", entries.Select(x => x.Name)),
                string.Join(",", entries.Select(x => x.Path)),
                ProgramEnableFlags.Serialize(entries.Select(x => x.Enabled)),
                ProgramCatalog.JoinIds(entries),
                string.Join(",", positionEntries.Select(x => x.Name)),
                string.Join(",", positionEntries.Select(x => x.MatchLargestWindow ? "1" : "0")),
                ProgramEnableFlags.Serialize(positionEntries.Select(x => x.Enabled)),
                ProgramCatalog.JoinIds(positionEntries),
                txtCycle.Text,
                chkEnablePersist.Checked.ToString(),
                txtCycleScreen.Text,
                chkEnableCustPos.Checked.ToString(),
                chkEnableRes.Checked.ToString(),
                txtResWidth.Text,
                txtResHeight.Text,
                txtResCycle.Text,
                chkEnableClickOnceZone.Checked.ToString(),
                chkEnableTerminateProtection.Checked.ToString()
            };

            for (int s = 0; s < 4; s++)
            {
                var names = GetCheckedNamesInClickOrder(_screenChecklists[s]);
                parts.Add(string.Join(",", names));
                parts.Add(SaveScreenSlots(names, ScreenSlotsFor(s)));
                parts.Add(SaveSlotPins(_screenChecklists[s], ScreenSlotsFor(s)));
                parts.Add(SaveReservedCells(ScreenReservedFor(s)));
                parts.Add(GridValue(_gridRowsFields[s].Text).ToString());
                parts.Add(GridValue(_gridColsFields[s].Text).ToString());
                parts.Add(_autoFitChecks[s].Checked.ToString());
                parts.Add(_borderlessChecks[s].Checked.ToString());
            }
            // Unit separator cannot occur in a path, id or checkbox value.
            return string.Join("\u001f", parts);
        }

        private void RememberSavedConfig() => _savedConfigSnapshot = CaptureConfigSnapshot();

        private bool HasUnsavedChanges() =>
            !IsDisposed && _screenChecklists != null
            && !string.Equals(CaptureConfigSnapshot(), _savedConfigSnapshot, StringComparison.Ordinal);

        /// <summary>
        /// Reloads the last saved configuration after the user chooses No on the unsaved-changes
        /// prompt. A plain Hide() was not enough: OnShow only reloads when the config file stamp
        /// changed, so No silently kept the discarded edits in memory AND in the live screen plan
        /// the position worker reads. Reload explicitly and republish so both agree with config.
        /// </summary>
        private void DiscardUnsavedChanges()
        {
            InitializeOrReload();
            RememberSavedConfig();
            PublishLiveScreenPlan();
            _positionWake.Set();
        }

        /// <summary>
        /// Warns before the form leaves the screen with unsaved edits. Yes writes them first,
        /// No discards them and returns to the last saved config, and Cancel keeps the window
        /// open. Returns false only for Cancel or a failed validation.
        /// </summary>
        private bool ConfirmUnsavedBeforeHide()
        {
            if (!HasUnsavedChanges())
                return true;

            DialogResult answer = MessageBox.Show(this,
                "You have unsaved configuration changes.\r\n\r\n" +
                "Save them before hiding TileKeeper to the tray?\r\n" +
                "No discards unsaved changes.",
                "Unsaved changes",
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button3);

            if (answer == DialogResult.Cancel)
                return false;
            if (answer == DialogResult.Yes)
                return SaveCurrentSettings();
            DiscardUnsavedChanges();
            return true;
        }

        /// <summary>True when an external edit changed the config after our last load/save.</summary>
        private bool ConfigChangedSinceLoad()
        {
            string path = ConfigFilePath();
            if (path == null)
                return false;

            try
            {
                if (!System.IO.File.Exists(path))
                    return true;
                return System.IO.File.GetLastWriteTimeUtc(path) != _configStampUtc;
            }
            catch
            {
                return true;
            }
        }

        /// <summary>
        /// Shows the settings form (called from the tray icon's double-click and the
        /// "Show" menu item). Rebuilds the controls only when the config file changed
        /// underneath us; an ordinary hide/show keeps unsaved edits and shows instantly.
        /// </summary>
        protected void OnShow(object sender, EventArgs e)
        {
            // Decide before any paint: a reload in front of the window is the slow redraw.
            bool needsReload = ConfigChangedSinceLoad();

            // Restoring from the tray must survive the transient Minimized state Show() can
            // re-apply: without this guard OnResize hid the form again, so a second double-click
            // (or a second Show) was needed. SetForegroundWindow then brings it above the app
            // the user was working in.
            _restoringFromTray = true;
            try
            {
                // Only the very first open (from the constructor's Minimized state) needs a
                // state change. Later opens come from Hide(), which deliberately leaves the
                // state Normal — setting Minimized→Normal here made Windows restore from its
                // taskbar origin and paint a zero-height client area (title bar only) first.
                if (this.WindowState == FormWindowState.Minimized)
                    this.WindowState = FormWindowState.Normal;

                // Re-assert the designer size before the handle paints. A collapsed rect may
                // still be remembered from an older build's minimise cycle; restoring it here
                // keeps one fully formed window for Show() to display in a single paint.
                if (!_restoreSize.IsEmpty && this.Size != _restoreSize)
                    this.Size = _restoreSize;

                // Position before Show(): showing first painted the form at the stale minimized
                // origin (bottom-left), then the move produced a visible corner→centre jump and
                // extra repaints. ShowInTaskbar stays false (set once in the constructor);
                // flipping it here recreated the handle on every tray open.
                PlaceRestoredWindow();
                this.Show();
                SetForegroundWindow(this.Handle);
            }
            finally
            {
                _restoringFromTray = false;
            }

            // Rebuild only after the window is visible, and only for a real external edit.
            // Unsaved rows and toggles therefore survive a trip to the tray.
            if (needsReload)
                BeginInvoke(new Action(InitializeOrReload));
        }

        /// <summary>
        /// Exits the program immediately. This build has no exit password.
        /// </summary>
        protected void OnExit(object sender, EventArgs e)
        {
            BackupConfig();
            running = false;          // Signal all background threads to stop
            _positionWake.Set();      // Wake the keeper loop so it exits without lingering
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            trayIcon.Dispose();
            Application.Exit();
        }

        /// <summary>
        /// Republishes the live plan and wakes the position keeper so a Borderless or AutoFit
        /// toggle applies on the next pass without Save. No dialog.
        /// </summary>
        private void ChkBorderless_CheckedChanged(object sender, EventArgs e)
        {
            PublishLiveScreenPlan();
            _positionWake.Set();
        }

        private void ChkScreenOption_CheckedChanged(object sender, EventArgs e)
        {
            PublishLiveScreenPlan();
            _positionWake.Set();
        }

        /// <summary>
        /// ShowInTaskbar is fixed at construction time. Toggling it here used to recreate the
        /// native window handle on every minimise/restore — the source of the tray flicker.
        /// The app stays tray-only; minimizing just hides the form.
        /// </summary>
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (!_restoringFromTray
                && this.WindowState == FormWindowState.Minimized && this.Visible)
            {
                // Minimising is the other way to leave the screen for the tray. If the user
                // cancels the unsaved-changes prompt, undo the minimise and keep the form up.
                if (!ConfirmUnsavedBeforeHide())
                {
                    this.WindowState = FormWindowState.Normal;
                    return;
                }
                this.Hide();
            }
        }

        /// <summary>
        /// Intercepts the window close button (X) and minimises to tray instead of closing.
        /// </summary>
        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                // X means "hide to tray", not "close". Warn first when the form holds edits
                // that Save has not written; Yes saves, No discards, Cancel keeps it open.
                e.Cancel = true;
                if (!ConfirmUnsavedBeforeHide())
                    return;

                // Hide() alone goes to the tray. Minimising first (and then re-applying a size)
                // corrupted the rect Windows keeps for restore: the next Show() came back as a
                // title bar and animated up from the bottom-left corner. Leaving state and size
                // untouched gives the next open one fully formed window to paint.
                this.Hide();
            }
        }        

        // ─────────────────────────────────────────────────────────────────────
        // Miscellaneous UI handlers
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Deletes every saved appSettings value and rewrites the shipped defaults in one pass.
        /// Used only by the irreversible Reset Programs &amp; Layout action.
        /// </summary>
        private void ResetConfigToDefaults()
        {
            Configuration config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
            foreach (string key in config.AppSettings.Settings.AllKeys.ToArray())
                config.AppSettings.Settings.Remove(key);
            config.Save(ConfigurationSaveMode.Full);
            ConfigurationManager.RefreshSection("appSettings");

            // EnsureConfigKeys recreates every key; write safe non-empty values for the numeric
            // and boolean fields so the form can reload without a parse exception.
            var defaults = new[]
            {
                new KeyValuePair<string, string>("ProgramName", ""),
                new KeyValuePair<string, string>("ProgramPath", ""),
                new KeyValuePair<string, string>("ProgramEnabled", ""),
                new KeyValuePair<string, string>("ProgramId", ""),
                new KeyValuePair<string, string>("CycleSeconds", "15"),
                new KeyValuePair<string, string>("EnablePersistRun", "False"),
                new KeyValuePair<string, string>("PositionProgramName", ""),
                new KeyValuePair<string, string>("PositionProgramMatchLargest", ""),
                new KeyValuePair<string, string>("PositionProgramEnabled", ""),
                new KeyValuePair<string, string>("PositionProgramId", ""),
                new KeyValuePair<string, string>("PositionMirrorSeeded", "False"),
                new KeyValuePair<string, string>("ScreenIdsMigrated", "True"),
                new KeyValuePair<string, string>("CycleKeepScreen", "30"),
                new KeyValuePair<string, string>("EnableCustomPosition", "False"),
                new KeyValuePair<string, string>("EnableResolutionHold", "False"),
                new KeyValuePair<string, string>("ResolutionWidth", "1920"),
                new KeyValuePair<string, string>("ResolutionHeight", "1080"),
                new KeyValuePair<string, string>("ResolutionCheckSeconds", "10"),
                new KeyValuePair<string, string>("EnableClickOnceIntranetZone", "False"),
                new KeyValuePair<string, string>("IntranetZoneManagedHosts", ""),
                new KeyValuePair<string, string>("EnableTerminateProtection", "True"),
                new KeyValuePair<string, string>("ClientSettingsProvider.ServiceUri", "")
            };
            for (int s = 1; s <= 4; s++)
            {
                string n = s.ToString();
                defaults = defaults.Concat(new[]
                {
                    new KeyValuePair<string, string>("Screen" + n, ""),
                    new KeyValuePair<string, string>("Screen" + n + "Slots", ""),
                    new KeyValuePair<string, string>("Screen" + n + "SlotPins", ""),
                    new KeyValuePair<string, string>("Screen" + n + "ReservedCells", ""),
                    new KeyValuePair<string, string>("Screen" + n + "AutoFit", "False"),
                    new KeyValuePair<string, string>("Screen" + n + "Borderless", "True"),
                    new KeyValuePair<string, string>("GridRows" + n, "1"),
                    new KeyValuePair<string, string>("GridCols" + n, "1")
                }).ToArray();
            }
            foreach (var pair in defaults)
                config.AppSettings.Settings.Add(pair.Key, pair.Value);
            config.Save(ConfigurationSaveMode.Full);
            ConfigurationManager.RefreshSection("appSettings");
            RememberConfigStamp();
        }

        /// <summary>
        /// Irreversibly deletes all saved settings and resets the form to defaults.
        /// A confirmation dialog with a No default protects against an accidental click.
        /// </summary>
        private void BtnClear_Click(object sender, EventArgs e)
        {
            DialogResult confirm = MessageBox.Show(
                "This will permanently delete and reset all saved settings to defaults.\n" +
                "This cannot be undone. Continue?",
                "Reset Programs & Layout",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);

            if (confirm != DialogResult.Yes)
                return;

            ResetConfigToDefaults();

            // The AppData copy is restored on version changes, so it must be overwritten too.
            BackupConfig();

            // Reload everything from the clean config: lists, screen assignments, grid shape,
            // intervals and toggles. RefreshAllGrids at the end republishes the live plan.
            InitializeOrReload();
        }

        /// <summary>Prevents non-digit characters from being typed into numeric input fields.</summary>
        private void NumberOnly_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar))
                e.Handled = true;
        }

        /// <summary>
        /// Grid shape input: only digits 1-3, centred. The companion buttons call
        /// <see cref="ChangeGridField"/> so the value can never leave the 1..3 range,
        /// whether typed, pasted, or stepped.
        /// </summary>
        private void WireGridField(TextBox box)
        {
            if (box == null)
                return;
            box.TextAlign = HorizontalAlignment.Center;
            box.KeyPress += GridField_KeyPress;
            box.TextChanged += GridField_Clamp;
        }

        private void GridField_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (char.IsControl(e.KeyChar))
                return;
            e.Handled = e.KeyChar < '1' || e.KeyChar > '3';
        }

        private void GridField_Clamp(object sender, EventArgs e)
        {
            var box = sender as TextBox;
            if (box == null)
                return;

            int value = GridValue(box.Text);
            string normalized = value.ToString();
            if (!string.Equals(box.Text, normalized, StringComparison.Ordinal))
                box.Text = normalized;
        }

        private void ChangeGridField(TextBox field, int delta)
        {
            if (field == null)
                return;
            int current = GridValue(field.Text);
            int next = Math.Max(1, Math.Min(MAX_GRID_DIM, current + delta));
            string text = next.ToString();
            if (!string.Equals(field.Text, text, StringComparison.Ordinal))
                field.Text = text; // TextChanged refreshes the preview and publishes the plan
        }

        /// <summary>Narrows a grid field to one centred digit and puts it between the steppers.</summary>
        private static void PlaceGridField(TextBox field, int y)
        {
            if (field == null)
                return;
            field.Location = new Point(183, y);
            field.Size = new Size(18, 20);
            field.TextAlign = HorizontalAlignment.Center;
        }

        /// <summary>
        /// Adds a compact -/+ button beside a grid field. Controls are created in code so all
        /// four Screen groups get identical spacing without four near-duplicate designer blocks.
        /// </summary>
        private Button AddGridSpinButton(Control parent, TextBox field, int delta, int x, int y)
        {
            var button = new Button
            {
                Text = delta < 0 ? "-" : "+",
                Size = new Size(16, 20),
                Location = new Point(x, y),
                TabStop = false,
                FlatStyle = FlatStyle.Flat
            };
            button.Font = new Font(Font.FontFamily, 8f, FontStyle.Bold);
            button.Click += (s, e) => ChangeGridField(field, delta);
            parent.Controls.Add(button);
            return button;
        }
    }
}