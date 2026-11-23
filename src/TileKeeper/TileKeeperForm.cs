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

        // ── Tray icon ─────────────────────────────────────────────────────────
        private readonly NotifyIcon      trayIcon;
        private readonly ContextMenuStrip trayMenu;

        // ── Timers (in milliseconds, set from config and used by background threads) ──
        private int monitorTimer;       // How often to check if programs are still running
        private int keepScreenPosTimer; // How often to check and fix window positions

        // ── State ─────────────────────────────────────────────────────────────
        private Size originalSize; // Saved when the form is shown so it can be restored on re-minimize
        private bool running = true; // Set to false on exit to stop all background threads cleanly

        /// <summary>
        /// Manual grid-cell pins, keyed by process name (case-insensitive).
        /// Value = zero-based cell index; absence or -1 = auto-fill in list order.
        /// </summary>
        private readonly Dictionary<string, int> _screenSlot1 = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _screenSlot2 = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _screenSlot3 = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _screenSlot4 = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        private readonly IWin32Api _win32Api = new Win32ApiImpl(); // reused by the watchdog for args-aware "is running" checks

        private bool _suppressingDedup;
// Per-screen (index-aligned with _screenChecklists): process names grayed out there.
        // Rebuilt wholesale by RecomputeDedupGrayed; never mutated incrementally.
        private readonly HashSet<string>[] _dedupGrayed;
        private System.Windows.Forms.CheckedListBox[] _screenChecklists;

        /// <summary>
        /// Per-screen map of program name → 1-based cell number, refreshed whenever the
        /// grid model is rebuilt. Drives the colour/number badge drawn on each checklist
        /// row so it matches the corresponding <see cref="Class.GridMapPanel"/> tile.
        /// Programs with no cell (grid too small) are absent.
        /// </summary>
        private readonly Dictionary<CheckedListBox, Dictionary<string, int>> _screenRowCell =
            new Dictionary<CheckedListBox, Dictionary<string, int>>();

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
            listPrograms.Columns.Add("Enabled", 62);
            listPrograms.Columns.Add("Program", 165);
            listPrograms.Columns.Add("Path / Command", 222);
            listPrograms.FullRowSelect = true;

            // Position-only list. Its native checkbox is the per-row Enable switch for
            // positioning; Match Largest Window moved to an explicit clickable column so
            // the two switches can never be confused.
            listPositionPrograms.View = View.Details;
            listPositionPrograms.CheckBoxes = true;
            listPositionPrograms.Columns.Add("Enabled", 62);
            listPositionPrograms.Columns.Add("Program", 225);
            listPositionPrograms.Columns.Add("Match largest", 145);
            listPositionPrograms.FullRowSelect = true;
            // Checking a row's box (Checked) and selecting it (Selected/blue highlight, what
            // Edit/Remove/Up/Down require) are independent WinForms ListView states — checking
            // a box never selects the row on its own. Sync check -> select so a checked row is
            // immediately actionable without an extra click.
            listPositionPrograms.ItemChecked += ListPositionPrograms_ItemChecked;
            listPositionPrograms.MouseClick += ListPositionPrograms_MouseClick;

            // Double-buffer the form and the list/checklist controls that get rebuilt
            // wholesale on every tray show/reload — cuts the flicker from that rebuild.
            this.DoubleBuffered = true;
            EnableDoubleBuffer(listPrograms);
            EnableDoubleBuffer(listPositionPrograms);
            EnableDoubleBuffer(checkedScreen1);
            EnableDoubleBuffer(checkedScreen2);
            EnableDoubleBuffer(checkedScreen3);
            EnableDoubleBuffer(checkedScreen4);



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
            trayIcon.MouseClick += TrayIcon_MouseClick;

            // Start minimised and hidden from the taskbar — app lives in the tray
            this.WindowState    = FormWindowState.Minimized;
            this.ShowInTaskbar  = false;
            this.TopMost        = true; // stay above other windows while shown; X/minimize still work
            this.FormClosing   += new FormClosingEventHandler(OnFormClosing);

            // Restrict numeric input fields to digits only
            WireNumeric(txtCycle);            
            WireNumeric(txtCycleScreen);
            WireNumeric(txtGridRows1);
            WireNumeric(txtGridCols1);
            WireNumeric(txtGridRows2);
            WireNumeric(txtGridCols2);
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
            txtGridRows1.TextChanged += delegate { RefreshAllGrids(); };
            txtGridCols1.TextChanged += delegate { RefreshAllGrids(); };
            txtGridRows2.TextChanged += delegate { RefreshAllGrids(); };
            txtGridCols2.TextChanged += delegate { RefreshAllGrids(); };

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
                Location = new Point(9, 292),
                Size = new Size(453, 112),
                TabIndex = 6,
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
                Location = new Point(147, 45),
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
            if (!IsHandleCreated || IsDisposed)
                return;
            BeginInvoke(new Action(() =>
            {
                ApplyScreenSectionEnableGate();
                RefreshAllGrids();
            }));
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
        /// Grays out the grid/tiling controls of screens whose monitor is not currently
        /// detected (re-reads Screen.AllScreens each call) — tiling is meaningless without an
        /// active monitor. The program checklist itself (Chk) is deliberately left enabled at
        /// all times: a program checked on a disconnected screen must stay reassignable/
        /// removable without needing that monitor plugged back in. The GroupBox (Label) is
        /// never disabled either — WinForms disables all of a container's children when the
        /// container itself is disabled, which would silently override Chk.Enabled = true.
        /// </summary>
        private void ApplyScreenSectionEnableGate()
        {
            var sections = new[]
            {
                new { AutoFit = chkAutoFit1, Borderless = chkBorderless1, Rows = txtGridRows1, Cols = txtGridCols1, Map = gridMap1 },
                new { AutoFit = chkAutoFit2, Borderless = chkBorderless2, Rows = txtGridRows2, Cols = txtGridCols2, Map = gridMap2 },
                new { AutoFit = chkAutoFit3, Borderless = chkBorderless3, Rows = txtGridRows3, Cols = txtGridCols3, Map = gridMap3 },
                new { AutoFit = chkAutoFit4, Borderless = chkBorderless4, Rows = txtGridRows4, Cols = txtGridCols4, Map = gridMap4 }
            };
            for (int s = 0; s < sections.Length; s++)
            {
                bool on = s < Screen.AllScreens.Length;
                sections[s].AutoFit.Enabled = on;
                sections[s].Borderless.Enabled = on;
                sections[s].Rows.Enabled = on;
                sections[s].Cols.Enabled = on;
                sections[s].Map.Enabled = on;
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
            this.SuspendLayout();
            try
            {
                InitializeOrReloadCore();
            }
            finally
            {
                this.ResumeLayout(true);
            }
        }

        private void InitializeOrReloadCore()
        {
            MoveToPrimaryScreen();

            RebuildProgramList(LoadEntriesFromConfig());
            RebuildPositionProgramList(LoadPositionEntriesFromConfig());

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

            // Load the per-screen borderless (frame-stripping) toggles; default ON
            // (matches the historic behavior of stripping chrome from tiled windows).
            chkBorderless1.Checked = GetConfigValue("Screen1Borderless", "True") == bool.TrueString;
            chkBorderless2.Checked = GetConfigValue("Screen2Borderless", "True") == bool.TrueString;
            chkBorderless3.Checked = GetConfigValue("Screen3Borderless", "True") == bool.TrueString;
            chkBorderless4.Checked = GetConfigValue("Screen4Borderless", "True") == bool.TrueString;

            // Load grid shape (0 = auto)
            txtGridRows1.Text = GetConfigValue("GridRows1", "0");
            txtGridCols1.Text = GetConfigValue("GridCols1", "0");
            txtGridRows2.Text = GetConfigValue("GridRows2", "0");
            txtGridCols2.Text = GetConfigValue("GridCols2", "0");

            // Populate the per-screen checked lists and manual cell pins. Wrapped in
            // _suppressingDedup so the SetItemChecked calls inside PopulateScreen never trigger
            // ScreenItemCheck's own dedup logic — gray state is recomputed fresh afterward.
            _suppressingDedup = true;
            try
            {
                var screen1 = SplitConfigList(GetConfigValue("Screen1", ""));
                var screen2 = SplitConfigList(GetConfigValue("Screen2", ""));
                LoadScreenSlots(_screenSlot1, screen1, GetConfigValue("Screen1Slots", ""));
                LoadScreenSlots(_screenSlot2, screen2, GetConfigValue("Screen2Slots", ""));
                PopulateScreen(checkedScreen1, BuildCandidates(), new HashSet<string>(screen1, StringComparer.OrdinalIgnoreCase));
                PopulateScreen(checkedScreen2, BuildCandidates(), new HashSet<string>(screen2, StringComparer.OrdinalIgnoreCase));

                // Screen3/Screen4 lists are always populated, same as Screen1/2 — they're only
                // grayed out (ApplyScreenSectionEnableGate) when the monitor isn't detected,
                // never emptied.
                var screen3 = SplitConfigList(GetConfigValue("Screen3", ""));
                LoadScreenSlots(_screenSlot3, screen3, GetConfigValue("Screen3Slots", ""));
                PopulateScreen(checkedScreen3, BuildCandidates(), new HashSet<string>(screen3, StringComparer.OrdinalIgnoreCase));

                var screen4 = SplitConfigList(GetConfigValue("Screen4", ""));
                LoadScreenSlots(_screenSlot4, screen4, GetConfigValue("Screen4Slots", ""));
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

            // Convert the config values (seconds) to milliseconds for Thread.Sleep
            monitorTimer       = int.Parse(txtCycle.Text)       * 1000;
            keepScreenPosTimer = int.Parse(txtCycleScreen.Text) * 1000;

            RefreshAllGrids();
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
            MoveToPrimaryScreen();
        }

        private void MoveToPrimaryScreen()
        {
            Screen primary          = Screen.PrimaryScreen;
            Rectangle primaryBounds = primary.Bounds;

            bool outsidePrimary =
                this.Location.X < primaryBounds.X   ||
                this.Location.X >= primaryBounds.Right ||
                this.Location.Y < primaryBounds.Y   ||
                this.Location.Y >= primaryBounds.Bottom;

            if (outsidePrimary)
                this.Location = new Point(primaryBounds.X, primaryBounds.Y);
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

        /// <summary>One row of the Programs tab: the launch name and its path/command.</summary>
        private sealed class ProgramEntry
        {
            public string Name;     // e.g. "notepad.exe -args", "App", "mstsc.exe"
            public string Path;     // directory, rdp file, or full dfshim command
            public string BaseName; // process-name used to match a running window
            public string BaseArgs; // command-line args from Name (part after first space); "" when none
            public string Key;      // identity used by the Position tab; = BaseName unless BaseName repeats
                                     // in the same list, in which case it becomes "BaseName (BaseArgs)" so two
                                     // shortcuts to the same exe with different args stay distinguishable.
            public bool MatchLargestWindow; // true = position by largest visible top-level window
                                             // instead of MainWindowHandle (see WindowMonitor.MatchLargest)
            public bool PositionOnly;       // true = came from the Position tab's own list, not the
                                             // Programs tab — never launched/relaunched by Keep Running.
            public bool Enabled;            // per-list switch: relaunch (Programs tab) or positioning
                                             // (Position tab). Missing config values default to true.

            public ProgramEntry(string name, string path, bool matchLargestWindow = false, bool positionOnly = false, bool enabled = true)
            {
                Name = name ?? "";
                Path = path ?? "";
                ParseNameParts(Name, out BaseName, out BaseArgs);
                Key = BaseName; // refined by AssignIdentityKeys once the full list is known
                MatchLargestWindow = matchLargestWindow;
                PositionOnly = positionOnly;
                Enabled = enabled;
            }
        }

        /// <summary>Splits a raw Programs-tab "Name" (e.g. "app.exe arg1 arg2") into exe base name and args.</summary>
        internal static void ParseNameParts(string raw, out string baseName, out string baseArgs)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                baseName = "";
                baseArgs = "";
                return;
            }

            int sp = raw.IndexOf(' ');
            baseName = Path.GetFileNameWithoutExtension(sp < 0 ? raw : raw.Substring(0, sp));
            baseArgs = sp < 0 ? "" : raw.Substring(sp + 1).Trim();
        }

        /// <summary>
        /// Sets each entry's <see cref="ProgramEntry.Key"/>: entries with a unique BaseName in
        /// <paramref name="entries"/> keep Key == BaseName (unchanged from historic behavior);
        /// entries sharing a BaseName get a "BaseName (BaseArgs)" disambiguated Key instead.
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

        private static string RowName(ListViewItem item) =>
            item.SubItems.Count > 1 ? item.SubItems[1].Text : item.Text;

        private static string RowPath(ListViewItem item) =>
            item.SubItems.Count > 2 ? item.SubItems[2].Text : "";

        private static bool RowMatchLargest(ListViewItem item) =>
            item.SubItems.Count > 2 && item.SubItems[2].Text == MatchLargestOn;

        private static ListViewItem NewProgramRow(string name, string path, bool enabled) =>
            new ListViewItem(new[] { "", name, path }) { Checked = enabled };

        private static ListViewItem NewPositionRow(string name, bool matchLargest, bool enabled) =>
            new ListViewItem(new[] { "", name, matchLargest ? MatchLargestOn : MatchLargestOff }) { Checked = enabled };

        /// <summary>Builds the program entries currently shown in the ListView.</summary>
        private List<ProgramEntry> GetProgramEntries()
        {
            var list = new List<ProgramEntry>();
            foreach (ListViewItem item in listPrograms.Items)
                list.Add(new ProgramEntry(RowName(item), RowPath(item), enabled: item.Checked));
            AssignIdentityKeys(list);
            return list;
        }

        private void RebuildProgramList(List<ProgramEntry> entries)
        {
            listPrograms.BeginUpdate();
            try
            {
                listPrograms.Items.Clear();
                foreach (var e in entries)
                    listPrograms.Items.Add(NewProgramRow(e.Name, e.Path, e.Enabled));
            }
            finally
            {
                listPrograms.EndUpdate();
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

            for (int i = 0; i < names.Length && i < paths.Length; i++)
                result.Add(new ProgramEntry(names[i], paths[i], enabled: enabled[i]));
            AssignIdentityKeys(result);
            return result;
        }

        /// <summary>Builds the position-only program entries currently shown in the Position tab's list.</summary>
        private List<ProgramEntry> GetPositionEntries()
        {
            var list = new List<ProgramEntry>();
            foreach (ListViewItem item in listPositionPrograms.Items)
                list.Add(new ProgramEntry(RowName(item), "", matchLargestWindow: RowMatchLargest(item),
                                          positionOnly: true, enabled: item.Checked));
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

            for (int i = 0; i < names.Length; i++)
            {
                bool matchLargest = i < matches.Length && matches[i].Trim() == "1";
                result.Add(new ProgramEntry(names[i], "", matchLargestWindow: matchLargest,
                                            positionOnly: true, enabled: enabled[i]));
            }
            AssignIdentityKeys(result);
            return result;
        }

        private void RebuildPositionProgramList(List<ProgramEntry> entries)
        {
            listPositionPrograms.BeginUpdate();
            try
            {
                listPositionPrograms.Items.Clear();
                foreach (var e in entries)
                    listPositionPrograms.Items.Add(NewPositionRow(e.Name, e.MatchLargestWindow, e.Enabled));
            }
            finally
            {
                listPositionPrograms.EndUpdate();
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

        // ─────────────────────────────────────────────────────────────────────
        // Screen assignment model (checked list + grid slots)
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>Candidate process names for all screen lists (deduplicated union, Programs + Position-only).</summary>
        private List<string> BuildCandidates()
        {
            var candidates = new List<string>();
            foreach (var e in GetAllPositionableEntries())
                if (!string.IsNullOrWhiteSpace(e.Key) && !candidates.Contains(e.Key, StringComparer.OrdinalIgnoreCase))
                    candidates.Add(e.Key);

            // Keep names that only exist in config so nothing silently disappears
            for (int s = 0; s < 4; s++)
                foreach (var n in SplitConfigList(GetConfigValue("Screen" + (s + 1), "")))
                    if (!candidates.Contains(n, StringComparer.OrdinalIgnoreCase))
                        candidates.Add(n);

            return candidates;
        }

        /// <summary>Rebuilds all four checklist items, preserving current checks (called after program list edits).</summary>
        private void RebuildScreenCandidates()
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
        }

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
            var seed = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in tracked)
                if (seen.Add(name) && IsCheckedName(chk, name))
                    seed.Add(name);
            foreach (var name in GetCheckedNames(chk))
                if (seen.Add(name))
                    seed.Add(name);
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
        /// click-order list is the sole source of truth — it is seeded by
        /// <see cref="SeedClickOrder"/> on every populate (load and mid-session rebuilds) and
        /// extended by <see cref="ScreenItemCheck"/> — so no list-order fallback is needed.
        /// </summary>
        private List<string> GetCheckedNamesInClickOrder(CheckedListBox chk)
        {
            var result = new List<string>();
            if (_screenCheckOrder.TryGetValue(chk, out List<string> order))
                foreach (var name in order)
                    if (IsCheckedName(chk, name))
                        result.Add(name);
            return result;
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

        private static int[] SlotsFromConfig(string csv, int count)
        {
            if (string.IsNullOrWhiteSpace(csv))
                return null;
            var parts = csv.Split(',');
            var arr = new int[count];
            for (int i = 0; i < count; i++)
                arr[i] = (i < parts.Length && int.TryParse(parts[i].Trim(), out int v)) ? v : -1;
            return arr;
        }

        private static int GridOrZero(string text) =>
            int.TryParse(text, out int v) && v > 0 ? v : 0;

        private void RefreshAllGrids()
        {
            ApplyScreenSectionEnableGate();
            RefreshGrid(checkedScreen1, gridMap1, _screenSlot1, GridOrZero(txtGridRows1.Text), GridOrZero(txtGridCols1.Text));
            RefreshGrid(checkedScreen2, gridMap2, _screenSlot2, GridOrZero(txtGridRows2.Text), GridOrZero(txtGridCols2.Text));
            RefreshGrid(checkedScreen3, gridMap3, _screenSlot3, GridOrZero(txtGridRows3.Text), GridOrZero(txtGridCols3.Text));
            RefreshGrid(checkedScreen4, gridMap4, _screenSlot4, GridOrZero(txtGridRows4.Text), GridOrZero(txtGridCols4.Text));
        }

        // ─────────────────────────────────────────────────────────────────────
        // Grid cell picker (click a cell → pick which program is pinned to it)
        // ─────────────────────────────────────────────────────────────────────

        private void GridMap1_CellClicked(int cell) =>
            ShowCellPickerFor(checkedScreen1, gridMap1, _screenSlot1,
                GridOrZero(txtGridRows1.Text), GridOrZero(txtGridCols1.Text), cell);

        private void GridMap2_CellClicked(int cell) =>
            ShowCellPickerFor(checkedScreen2, gridMap2, _screenSlot2,
                GridOrZero(txtGridRows2.Text), GridOrZero(txtGridCols2.Text), cell);

        private void GridMap3_CellClicked(int cell) =>
            ShowCellPickerFor(checkedScreen3, gridMap3, _screenSlot3,
                GridOrZero(txtGridRows3.Text), GridOrZero(txtGridCols3.Text), cell);

        private void GridMap4_CellClicked(int cell) =>
            ShowCellPickerFor(checkedScreen4, gridMap4, _screenSlot4,
                GridOrZero(txtGridRows4.Text), GridOrZero(txtGridCols4.Text), cell);

        private void ShowCellPickerFor(CheckedListBox chk, GridMapPanel map, Dictionary<string, int> slots, int rows, int cols, int cell)
        {
            var names = GetCheckedNamesInClickOrder(chk);
            if (names.Count == 0)
                return;

            WindowMonitor.ResolveLayout(names, rows, cols, SlotsArray(names, slots), out _, out int[] cells);

            var menu = new ContextMenuStrip();

            menu.Items.Add("Auto-fill (clear pin)").Click += (s, e) =>
            {
                for (int i = 0; i < cells.Length; i++)
                    if (cells[i] == cell)
                        slots.Remove(names[i]);
                RefreshGrid(chk, map, slots, rows, cols);
            };

            menu.Items.Add(new ToolStripSeparator());

            foreach (var name in names)
            {
                bool occupies = cells
                    .Select((c, i) => (c, name_i: names[i]))
                    .Any(t => t.c == cell && string.Equals(t.name_i, name, StringComparison.OrdinalIgnoreCase));

                var item = menu.Items.Add((occupies ? "• " : "") + name);
                item.Click += (s, e) =>
                {
                    slots.Remove(name); // un-pin its previous cell
                    slots[name] = cell; // pin to the clicked cell
                    foreach (var kv in new List<KeyValuePair<string, int>>(slots))
                        if (kv.Value == cell && !string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase))
                            slots.Remove(kv.Key); // whoever owned this cell becomes auto-fill
                    RefreshGrid(chk, map, slots, rows, cols);
                };
            }

            menu.Show(Cursor.Position);
        }

        private void RefreshGrid(CheckedListBox chk, GridMapPanel map, Dictionary<string, int> slots, int rows, int cols)
        {
            var names = GetCheckedNamesInClickOrder(chk);
            map.SetModel(names, SlotsArray(names, slots), rows, cols);
            StoreRowCells(chk, names, slots, rows, cols);
        }

        /// <summary>
        /// Recomputes each program's cell and stores name → 1-based cell for the checklist
        /// badge. Uses the same <see cref="WindowMonitor.ResolveLayout"/> the grid panel
        /// paints with, so the badge number/colour always matches its tile.
        /// </summary>
        private void StoreRowCells(CheckedListBox chk, List<string> names, Dictionary<string, int> slots, int rows, int cols)
        {
            WindowMonitor.ResolveLayout(names, rows, cols, SlotsArray(names, slots), out _, out int[] cells);

            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < cells.Length && i < names.Count; i++)
                if (cells[i] >= 0)
                    map[names[i]] = cells[i] + 1;

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
        /// Handles ItemCheck on any screen's checklist. Checks the identity key
        /// (name, BaseArgs) — reuses the <see cref="ArgsForKey"/> seam used by the
        /// runtime assignment loop — and unchecks matching items on other screens so a
        /// program is never checked on two screens simultaneously. Gray state itself is not
        /// mutated here; it's always recomputed from scratch afterward (see
        /// <see cref="RecomputeDedupGrayed"/>) so it can never drift from actual checked state.
        /// </summary>
        private void ScreenItemCheck(object sender, ItemCheckEventArgs e)
        {
            if (_suppressingDedup)
                return;

            var source = (CheckedListBox)sender;
            string name = source.Items[e.Index].ToString();

            // Grayed by another screen — not checkable
            if (IsGrayedOnScreen(ScreenIndexOf(source), name))
            {
                e.NewValue = e.CurrentValue;
                return;
            }

            if (e.NewValue == CheckState.Checked)
            {
                // Identity key: (name, BaseArgs) — reuses the ArgsForKey seam
                string args = ArgsForKey(name);

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
                            if (!string.Equals(chk.Items[i].ToString(), name, StringComparison.OrdinalIgnoreCase))
                                continue;
                            // Compare BaseArgs via the same ArgsForKey lookup the runtime assignment loop uses
                            if (!string.Equals(ArgsForKey(chk.Items[i].ToString()), args, StringComparison.OrdinalIgnoreCase))
                                continue;

                            chk.SetItemChecked(i, false);
                        }
                    }
                }
                finally
                {
                    _suppressingDedup = false;
                }
            }

            // Maintain the per-screen click order: the newly (un)checked name leaves every
            // list's order (covers the dedup auto-unchecks above), then a fresh check is
            // appended to the source list so auto-fill badge numbers sequence by click order.
            for (int s = 0; s < _screenChecklists.Length; s++)
                if (_screenCheckOrder.TryGetValue(_screenChecklists[s], out List<string> order))
                    order.RemoveAll(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));

            if (e.NewValue == CheckState.Checked)
                _screenCheckOrder[source].Add(name);

            // Deferred so it runs after this event finishes and the CheckedListBox has
            // actually committed e.NewValue to the source item.
            BeginInvoke(new Action(() =>
            {
                RecomputeDedupGrayed();
                RefreshAllGrids();
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
            Dictionary<string, string> argsByKey = BuildKeyArgsLookup();
            for (int s = 0; s < _screenChecklists.Length; s++)
            {
                var grayed = _dedupGrayed[s] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var chk = _screenChecklists[s];
                for (int i = 0; i < chk.Items.Count; i++)
                {
                    string name = chk.Items[i].ToString();
                    string args = ArgsForKey(argsByKey, name);
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
                            if (!string.Equals(other.Items[j].ToString(), name, StringComparison.OrdinalIgnoreCase))
                                continue;
                            if (!string.Equals(ArgsForKey(argsByKey, other.Items[j].ToString()), args, StringComparison.OrdinalIgnoreCase))
                                continue;
                            checkedElsewhere = true;
                            break;
                        }
                    }

                    if (checkedElsewhere)
                        grayed.Add(name);
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
            string name = e.ListItem as string;
            if (name == null)
                return;

            bool gray = IsGrayedOnScreen(ScreenIndexOf(chk), name);

            int cellNo = -1;
            bool hasCell = !gray
                && _screenRowCell.TryGetValue(chk, out var cellMap)
                && cellMap.TryGetValue(name, out cellNo);

            // Belt-and-braces: if the row→cell map is momentarily stale (e.g. the checklist was
            // just repopulated and RefreshAllGrids has not run yet), a checked row must still
            // number itself — derived from its position among the checked rows — so it never
            // renders without its cell badge. The normal path is untouched and keeps slot pins.
            if (!hasCell && !gray)
            {
                int index = chk.Items.IndexOf(name);
                if (index >= 0 && chk.GetItemChecked(index))
                {
                    cellNo = IndexOfChecked(chk, index) + 1;
                    hasCell = true;
                }
            }

            e.Value = hasCell ? $"({cellNo}) {name}" : name;
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
                    // Re-read config every cycle so changes saved while running take effect immediately
                    string[] programName = GetConfigValue("ProgramName", "").Split(',');
                    string[] programPath = SplitProgramPaths(GetConfigValue("ProgramPath", ""));

                    bool.TryParse(ConfigurationManager.AppSettings["EnablePersistRun"], out bool enablePersistRun);
                    bool.TryParse(ConfigurationManager.AppSettings["EnableMonitoring"],   out bool enableMonitoring);

                    bool[] programEnabled = ProgramEnableFlags.Parse(
                        GetConfigValue("ProgramEnabled", ""), programName.Length);

                    if (enablePersistRun && programName.Length > 0 && programPath.Length > 0)
                    {
                        var tasks = new List<Task>();

                        for (int i = 0; i < programName.Length; i++)
                        {
                            int index = i; // Capture loop variable for the async lambda

                            tasks.Add(Task.Run(async () =>
                            {
                                try
                                {
                                    // A disabled row is temporarily excluded from relaunching.
                                    // Its config entry stays intact, so re-checking Enable resumes it.
                                    if (!programEnabled[index] || index >= programPath.Length)
                                        return;

                                    string fullProgramPath = BuildFullPath(programName[index], programPath[index]);
                                    if (string.IsNullOrEmpty(fullProgramPath))
                                        return;

                                    ParseNameParts(programName[index], out string baseName, out string baseArgs);
                                    Process[] runningProcs = Process.GetProcessesByName(baseName);

                                    bool isRunning = string.IsNullOrEmpty(baseArgs)
                                        ? runningProcs.Length > 0 // historic name-only check, unchanged
                                        : runningProcs.Any(p =>
                                        {
                                            string cmdLine = _win32Api.GetProcessCommandLine(p.Id);
                                            // WMI unavailable — fall back to name-only match (same
                                            // degradation WindowMonitor already accepts)
                                            return string.IsNullOrWhiteSpace(cmdLine)
                                                || WindowMonitor.CommandLineMatchesArgs(cmdLine, baseArgs);
                                        });

                                    if (!isRunning)
                                    {
                                        StartProgram(fullProgramPath);

                                        // Wait for the process to appear before re-checking
                                        await Task.Delay(5000);
                                        runningProcs = Process.GetProcessesByName(baseName);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"Error in monitoring program: {ex.Message}");
                                }
                            }));
                        }

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
        /// Launches a program given its full path, optionally with arguments.
        /// </summary>
        private void StartProgram(string fullProgramPath)
        {
            int exeEnd = fullProgramPath.LastIndexOf(".exe", StringComparison.OrdinalIgnoreCase) + 4;

            string programPath   = fullProgramPath.Substring(0, exeEnd);
            string arguments     = fullProgramPath.Substring(exeEnd).Trim();
            string workingDir    = Path.GetDirectoryName(programPath);

            try
            {
                Process.Start(new ProcessStartInfo
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
            }
        }        

        // ─────────────────────────────────────────────────────────────────────
        // Background thread 2: Screen-position keeper
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>Looks up a checked screen-list Key's configured args ("" when none/unmatched).</summary>
        private string ArgsForKey(string key)
        {
            foreach (var e in LoadAllPositionableEntriesFromConfig())
                if (string.Equals(e.Key, key, StringComparison.OrdinalIgnoreCase))
                    return e.BaseArgs;
            return "";
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
            foreach (var e in LoadAllPositionableEntriesFromConfig())
                if (!string.IsNullOrWhiteSpace(e.Key) && !byKey.ContainsKey(e.Key))
                    byKey[e.Key] = e.BaseArgs;
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
            var byKey = new Dictionary<string, ProgramEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in LoadAllPositionableEntriesFromConfig())
                if (!string.IsNullOrWhiteSpace(e.Key))
                    byKey[e.Key] = e;

            programs = new List<string>();
            args = new List<string>();
            matchLargest = new List<bool>();
            keptIndexes = new List<int>();
            for (int i = 0; i < keys.Count; i++)
            {
                string k = keys[i];

                // The Position tab's Enable checkbox only affects position-only rows.
                // Launch entries keep their own Enable switch for the Keep Running watchdog.
                if (byKey.TryGetValue(k, out ProgramEntry e) && e.PositionOnly && !e.Enabled)
                    continue;

                keptIndexes.Add(i);
                if (e != null)
                {
                    programs.Add(e.BaseName);
                    args.Add(e.BaseArgs);
                    matchLargest.Add(e.MatchLargestWindow);
                }
                else
                {
                    programs.Add(k);
                    args.Add("");
                    matchLargest.Add(false);
                }
            }
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
                        // Keep one slot per detected monitor (up to 4) even when a screen's list
                        // is empty, so ScreenN config always binds to the Nth detected monitor.
                        // Compacting empty lists here shifts every later screen to a lower monitor index.
                        var screenLists = new List<List<string>>();
                        for (int s = 0; s < 4 && s < Screen.AllScreens.Length; s++)
                            screenLists.Add(SplitConfigList(GetConfigValue("Screen" + (s + 1), "")));
                        DelistDuplicates(screenLists);

                        var assignments = new List<ScreenAssignment>();
                        for (int i = 0; i < screenLists.Count; i++)
                        {
                            bool.TryParse(ConfigurationManager.AppSettings["Screen" + (i + 1) + "AutoFit"], out bool autoFit);
                            // Live checkbox state, not saved config — a Borderless toggle takes
                            // effect on the next pass without needing Save (Save still persists it).
                            bool borderless = GetBorderlessForScreen(i);
                            ResolveScreenPrograms(screenLists[i], out List<string> programs, out List<string> args,
                                                  out List<bool> matchLargest, out List<int> keptIndexes);
                            int[] configuredSlots = SlotsFromConfig(
                                GetConfigValue("Screen" + (i + 1) + "Slots", ""), screenLists[i].Count);
                            var slots = new int[programs.Count];
                            for (int k = 0; k < keptIndexes.Count; k++)
                                slots[k] = keptIndexes[k] < configuredSlots.Length ? configuredSlots[keptIndexes[k]] : -1;

                            assignments.Add(new ScreenAssignment(
                                programs,
                                autoFit,
                                GridOrZero(GetConfigValue("GridRows" + (i + 1), "0")),
                                GridOrZero(GetConfigValue("GridCols" + (i + 1), "0")),
                                slots,
                                args,
                                matchLargest,
                                borderless));
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
                "ProgramName", "ProgramPath", "ProgramEnabled", "CycleSeconds", "EnablePersistRun",
                "PositionProgramName", "PositionProgramMatchLargest", "PositionProgramEnabled",
                "Screen1", "Screen2", "Screen1AutoFit", "Screen2AutoFit",
                "Screen1Borderless", "Screen2Borderless",
                "GridRows1", "GridCols1", "GridRows2", "GridCols2",
                "Screen1Slots", "Screen2Slots",
                "CycleKeepScreen", "EnableCustomPosition",
                "CpuUsage", "MemUsage", "EnableMonitoring",
                "EnableResolutionHold", "ResolutionWidth", "ResolutionHeight", "ResolutionCheckSeconds",
                "EnableClickOnceIntranetZone", "IntranetZoneManagedHosts",
                "ClientSettingsProvider.ServiceUri",
                // Screen3/Screen4 are always saved now (BtnSave_Click no longer gates them on
                // monitor detection), so their keys must always exist too, regardless of how
                // many monitors happen to be connected at startup.
                "Screen3", "Screen3AutoFit", "Screen3Borderless", "GridRows3", "GridCols3", "Screen3Slots",
                "Screen4", "Screen4AutoFit", "Screen4Borderless", "GridRows4", "GridCols4", "Screen4Slots"
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
            var entries = GetProgramEntries();

            // Validate grid capacity before touching config
            List<string> screen1 = GetCheckedNamesInClickOrder(checkedScreen1);
            List<string> screen2 = GetCheckedNamesInClickOrder(checkedScreen2);
            int rows1 = GridOrZero(txtGridRows1.Text), cols1 = GridOrZero(txtGridCols1.Text);
            int rows2 = GridOrZero(txtGridRows2.Text), cols2 = GridOrZero(txtGridCols2.Text);

            // Screen3/Screen4 are saved unconditionally now — a program checked on a
            // disconnected screen must still persist when Save is clicked, not just once its
            // monitor is reconnected.
            List<string> screen3 = GetCheckedNamesInClickOrder(checkedScreen3);
            List<string> screen4 = GetCheckedNamesInClickOrder(checkedScreen4);

            if (!ValidateGrid("Screen 1", rows1, cols1, screen1.Count) ||
                !ValidateGrid("Screen 2", rows2, cols2, screen2.Count))
                return;

            int rows3 = GridOrZero(txtGridRows3.Text), cols3 = GridOrZero(txtGridCols3.Text);
            if (!ValidateGrid("Screen 3", rows3, cols3, screen3.Count))
                return;

            int rows4 = GridOrZero(txtGridRows4.Text), cols4 = GridOrZero(txtGridCols4.Text);
            if (!ValidateGrid("Screen 4", rows4, cols4, screen4.Count))
                return;

            // Programs list
            SaveSettings("ProgramName", string.Join(",", entries.Select(x => x.Name)));
            SaveSettings("ProgramPath", string.Join(",", entries.Select(x => x.Path)));
            SaveSettings("ProgramEnabled", ProgramEnableFlags.Serialize(entries.Select(x => x.Enabled)));

            // Position-only programs list (added from the Position tab; never launched by Keep Running)
            var positionEntries = GetPositionEntries();
            SaveSettings("PositionProgramName", string.Join(",", positionEntries.Select(x => x.Name)));
            SaveSettings("PositionProgramMatchLargest", string.Join(",", positionEntries.Select(x => x.MatchLargestWindow ? "1" : "0")));
            SaveSettings("PositionProgramEnabled", ProgramEnableFlags.Serialize(positionEntries.Select(x => x.Enabled)));

            // Process-persistence settings
            SaveSettings("CycleSeconds", txtCycle.Text);
            SaveSettings("EnablePersistRun", chkEnablePersist.Checked.ToString());            

            // Screen assignments + grid + slots
            SaveSettings("Screen1", string.Join(",", screen1));
            SaveSettings("Screen2", string.Join(",", screen2));
            SaveSettings("Screen1Slots", SaveScreenSlots(screen1, _screenSlot1));
            SaveSettings("Screen2Slots", SaveScreenSlots(screen2, _screenSlot2));
            SaveSettings("Screen1AutoFit",       chkAutoFit1.Checked.ToString());
            SaveSettings("Screen1Borderless",    chkBorderless1.Checked.ToString());
            SaveSettings("Screen2AutoFit",       chkAutoFit2.Checked.ToString());
            SaveSettings("Screen2Borderless",    chkBorderless2.Checked.ToString());
            SaveSettings("GridRows1", rows1.ToString());
            SaveSettings("GridCols1", cols1.ToString());
            SaveSettings("GridRows2", rows2.ToString());
            SaveSettings("GridCols2", cols2.ToString());
            SaveSettings("CycleKeepScreen",      txtCycleScreen.Text);
            SaveSettings("EnableCustomPosition", chkEnableCustPos.Checked.ToString());

            SaveSettings("Screen3", string.Join(",", screen3));
            SaveSettings("Screen3Slots", SaveScreenSlots(screen3, _screenSlot3));
            SaveSettings("Screen3AutoFit",       chkAutoFit3.Checked.ToString());
            SaveSettings("Screen3Borderless",    chkBorderless3.Checked.ToString());
            SaveSettings("GridRows3", rows3.ToString());
            SaveSettings("GridCols3", cols3.ToString());

            SaveSettings("Screen4", string.Join(",", screen4));
            SaveSettings("Screen4Slots", SaveScreenSlots(screen4, _screenSlot4));
            SaveSettings("Screen4AutoFit",       chkAutoFit4.Checked.ToString());
            SaveSettings("Screen4Borderless",    chkBorderless4.Checked.ToString());
            SaveSettings("GridRows4", rows4.ToString());
            SaveSettings("GridCols4", cols4.ToString());
            
            // Display resolution hold
            SaveSettings("EnableResolutionHold", chkEnableRes.Checked.ToString());
            SaveSettings("ResolutionWidth",      txtResWidth.Text);
            SaveSettings("ResolutionHeight",     txtResHeight.Text);
            SaveSettings("ResolutionCheckSeconds", txtResCycle.Text);

            // Apply updated intervals to the running background threads immediately
            monitorTimer       = int.Parse(txtCycle.Text)       * 1000;
            keepScreenPosTimer = int.Parse(txtCycleScreen.Text) * 1000;

            // Registry / Intranet Zone (opt-in). Disabling removes only the entries
            // TileKeeper added, so the checkbox is a real on/off switch.
            SaveSettings(RemoteDesktopManager.EnableSettingKey, chkEnableClickOnceZone.Checked.ToString());
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

            MessageBox.Show("Settings saved successfully!", "Saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
            this.Close(); // Triggers OnFormClosing → minimises to tray
        }

        /// <summary>
        /// Rejects a grid larger than 3×3 or a program count that cannot fit the grid.
        /// </summary>
        private bool ValidateGrid(string screenName, int rows, int cols, int count)
        {
            if (rows > MAX_GRID_DIM || cols > MAX_GRID_DIM || (rows > 0 && cols > 0 && rows * cols > MAX_GRID_DIM * MAX_GRID_DIM))
            {
                MessageBox.Show($"{screenName}: grid is limited to {MAX_GRID_DIM}x{MAX_GRID_DIM} (9 cells).",
                    "Save rejected", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }

            int cells = rows > 0 && cols > 0 ? rows * cols : MAX_GRID_DIM * MAX_GRID_DIM;
            if (count > cells)
            {
                string shape = rows > 0 && cols > 0 ? $"{rows}x{cols}" : $"auto ({cells} cells)";
                MessageBox.Show($"{screenName}: {count} programs exceed the grid {shape}.",
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

            RebuildScreenCandidates();
        }

        /// <summary>Re-picks the file backing the selected row (one program).</summary>
        private void BtnEdit_Click(object sender, EventArgs e)
        {
            if (listPrograms.SelectedItems.Count == 0)
            {
                MessageBox.Show("Select a program row to edit.", "Edit", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var item = listPrograms.SelectedItems[0];

            using (var dlg = NewProgramDialog())
            {
                if (dlg.ShowDialog() != DialogResult.OK)
                    return;

                string fullPath = dlg.FileName;
                var (name, path) = ResolveProgramFile(fullPath);
                if (IsSelf(name))
                {
                    MessageBox.Show("This program cannot add itself to the list.", "Edit",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                if (ListContainsName(listPrograms, name, item))
                    return; // silent — renaming onto an existing row is skipped like Add

                item.SubItems[1].Text = name;
                item.SubItems[2].Text = path;
            }

            RebuildScreenCandidates();
        }

        /// <summary>Removes the selected program rows.</summary>
        private void BtnRemove_Click(object sender, EventArgs e)
        {
            foreach (ListViewItem item in listPrograms.SelectedItems.Cast<ListViewItem>().ToList())
                listPrograms.Items.Remove(item);

            RebuildScreenCandidates();
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
            
            RebuildScreenCandidates();
        }

        /// <summary>Re-picks the file backing the selected position-only row.</summary>
        private void BtnEditPosition_Click(object sender, EventArgs e)
        {
            if (listPositionPrograms.SelectedItems.Count == 0)
            {
                MessageBox.Show("Select a program row to edit.", "Edit", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var item = listPositionPrograms.SelectedItems[0];

            using (var dlg = NewProgramDialog())
            {
                if (dlg.ShowDialog() != DialogResult.OK)
                    return;

                var (name, _) = ResolveProgramFile(dlg.FileName);
                if (IsSelf(name))
                {
                    MessageBox.Show("This program cannot add itself to the list.", "Edit",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                if (ListContainsName(listPositionPrograms, name, item))
                    return; // silent — renaming onto an existing row is skipped like Add

                item.SubItems[1].Text = name;
            }

            RebuildScreenCandidates();
        }

        /// <summary>Removes the selected position-only rows.</summary>
        private void BtnRemovePosition_Click(object sender, EventArgs e)
        {
            foreach (ListViewItem item in listPositionPrograms.SelectedItems.Cast<ListViewItem>().ToList())
                listPositionPrograms.Items.Remove(item);

            RebuildScreenCandidates();
        }

        /// <summary>
        /// Selects a row when its Enable checkbox is checked, so Edit/Remove act on that row
        /// without an extra click. Only checking selects — unchecking leaves selection alone.
        /// </summary>
        private void ListPositionPrograms_ItemChecked(object sender, ItemCheckedEventArgs e)
        {
            if (e.Item.Checked)
                e.Item.Selected = true;
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
            if (ListContainsName(listPositionPrograms, name, null))
                return false;

            listPositionPrograms.Items.Add(NewPositionRow(name, false, true));
            return true;
        }

        /// <summary>
        /// Case-insensitive search for <paramref name="name"/> among <paramref name="list"/>'s row
        /// text (the row's identity — see AddProgramRowFromFile). <paramref name="ignore"/> excludes
        /// the row being edited so renaming a row onto itself isn't flagged as a duplicate.
        /// </summary>
        private static bool ListContainsName(ListView list, string name, ListViewItem ignore)
        {
            foreach (ListViewItem item in list.Items)
                if (item != ignore && string.Equals(RowName(item), name, StringComparison.OrdinalIgnoreCase))
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
            foreach (string fullPath in files)
                AddProgramRowFromFile(fullPath);

            RebuildScreenCandidates();
        }

        /// <summary>Drops Explorer files into the Position list as new rows (same handling as Add).</summary>
        private void ListPositionPrograms_DragDrop(object sender, DragEventArgs e)
        {
            if (!(e.Data.GetData(DataFormats.FileDrop) is string[] files))
                return;
            foreach (string fullPath in files)
                AddPositionRowFromFile(fullPath);

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
            if (ListContainsName(listPrograms, name, null))
                return false;

            listPrograms.Items.Add(NewProgramRow(name, path, true));
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
                about.ShowInTaskbar = !settingsVisible;
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
        /// </summary>
        private void BtnCaptureRes_Click(object sender, EventArgs e)
        {
            Rectangle bounds = Screen.PrimaryScreen.Bounds;
            txtResWidth.Text  = bounds.Width.ToString();
            txtResHeight.Text = bounds.Height.ToString();
            lblResStatus.Text = "Captured primary display resolution.";
        }

        // ─────────────────────────────────────────────────────────────────────
        // Tray icon and window behaviour
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Shows the settings form (called from the tray "Show" menu item).
        /// Re-initialises all controls from config.
        /// </summary>
        protected void OnShow(object sender, EventArgs e)
        {
            InitializeOrReload();
            this.Show();
            this.WindowState  = FormWindowState.Normal;
            originalSize      = this.Size;
            this.ShowInTaskbar = true;
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
        /// Wakes the position-keeper loop so a Borderless toggle is applied without waiting
        /// for the current cycle (the keeper reads the live checkbox state each pass). No dialog.
        /// </summary>
        private void ChkBorderless_CheckedChanged(object sender, EventArgs e)
        {
            _positionWake.Set();
        }

        /// <summary>
        /// Hides the form from the taskbar when the user minimises it, keeping
        /// it accessible only via the tray icon.
        /// </summary>
        protected override void OnResize(EventArgs e)
        {
            if (this.WindowState == FormWindowState.Minimized)
                this.ShowInTaskbar = false;

            base.OnResize(e);
        }

        /// <summary>
        /// Intercepts the window close button (X) and minimises to tray instead of closing.
        /// </summary>
        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel           = true;
                this.WindowState   = FormWindowState.Minimized;
                this.ShowInTaskbar = false;
                this.Size          = originalSize;
            }
        }        

        // ─────────────────────────────────────────────────────────────────────
        // Miscellaneous UI handlers
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Resets the editable program lists, screen assignments, and layout options to their
        /// defaults in the current form. It does not delete the saved configuration until Save
        /// is clicked, and it leaves cycle intervals, display hold, and update settings alone.
        /// </summary>
        private void BtnClear_Click(object sender, EventArgs e)
        {
            listPrograms.Items.Clear();
            listPositionPrograms.Items.Clear();
            checkedScreen1.Items.Clear();
            checkedScreen2.Items.Clear();
            checkedScreen3.Items.Clear();
            checkedScreen4.Items.Clear();
            _screenSlot1.Clear();
            _screenSlot2.Clear();
            _screenSlot3.Clear();
            _screenSlot4.Clear();
            chkEnablePersist.Checked   = false;
            chkAutoFit1.Checked        = false;
            chkAutoFit2.Checked        = false;
            chkBorderless1.Checked     = false;
            chkBorderless2.Checked     = false;
            chkBorderless3.Checked     = false;
            chkBorderless4.Checked     = false;
            chkEnableCustPos.Checked   = false;            
            txtGridRows1.Text = txtGridCols1.Text = "0";
            txtGridRows2.Text = txtGridCols2.Text = "0";
            txtGridRows3.Text = txtGridCols3.Text = "0";
            txtGridRows4.Text = txtGridCols4.Text = "0";
            RefreshAllGrids();
        }

        /// <summary>Shows the context menu on right-click; left-click is handled by the OS default (show window).</summary>
        private void TrayIcon_MouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
                trayMenu.Show(Cursor.Position);
        }

        /// <summary>Prevents non-digit characters from being typed into numeric input fields.</summary>
        private void NumberOnly_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar))
                e.Handled = true;
        }
    }
}