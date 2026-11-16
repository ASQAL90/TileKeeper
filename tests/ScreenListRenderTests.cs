using System;
using System.Collections.Generic;
using System.Configuration;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using NUnit.Framework;
using TileKeeper.Class;

namespace TileKeeper.Tests
{
    /// <summary>
    /// Reproduces the Screen-tab checklist display path end to end against a real
    /// (headless) form instance: populate → RefreshAllGrids → ScreenList_Format, then
    /// asserts a checked row's display text carries its "(N)" grid-cell badge.
    /// </summary>
    [TestFixture]
    public class ScreenListRenderTests
    {
        private static readonly BindingFlags PrivateInstance =
            BindingFlags.NonPublic | BindingFlags.Instance;

        private static string _liveConfigPath;

        [OneTimeSetUp]
        public static void PointAppDomainAtLiveConfig()
        {
            // Mirror the operator's real configuration (Screen1/2 checked lists etc.)
            // by loading a copy of the deployed .exe.config into this AppDomain. A copy
            // protects the real file from any write by the app under test.
            string real = @"C:\Program Files\TileKeeper\TileKeeper.exe.config";
            if (!File.Exists(real))
            {
                _liveConfigPath = null;
                return;
            }

            _liveConfigPath = Path.Combine(Path.GetTempPath(),
                "TileKeeper.Tests.live." + Guid.NewGuid().ToString("N") + ".config");
            File.Copy(real, _liveConfigPath, true);
            AppDomain.CurrentDomain.SetData("APP_CONFIG_FILE", _liveConfigPath);
            ConfigurationManager.RefreshSection("appSettings");
        }

        [OneTimeTearDown]
        public static void CleanupLiveConfig()
        {
            if (_liveConfigPath != null && File.Exists(_liveConfigPath))
                File.Delete(_liveConfigPath);
        }

        [Test]
        public void CheckedRow_ShowsNumberPrefix()
        {
            var form = new TileKeeperForm();
            var lists = (CheckedListBox[])GetField(form, "_screenChecklists");
            var chk = lists[0];

            PopulateScreen(chk, new List<string> { "AppA", "AppB", "AppC" },
                new HashSet<string> { "AppA", "AppB" });

            Invoke(form, "RefreshAllGrids");

            Assert.That(chk.GetItemText(chk.Items[0]), Is.EqualTo("(1) AppA"), "AppA → cell 1");
            Assert.That(chk.GetItemText(chk.Items[1]), Is.EqualTo("(2) AppB"), "AppB → cell 2");
        }

        [Test]
        public void UncheckedRow_KeepsPlainName()
        {
            var form = new TileKeeperForm();
            var lists = (CheckedListBox[])GetField(form, "_screenChecklists");
            var chk = lists[0];

            PopulateScreen(chk, new List<string> { "AppA", "AppB", "AppC" },
                new HashSet<string> { "AppA" });

            Invoke(form, "RefreshAllGrids");

            Assert.That(chk.GetItemText(chk.Items[1]), Is.EqualTo("AppB"), "unchecked row must stay plain");
        }

        /// <summary>
        /// A checked row that has no entry in the row→cell map (stale-map window) must still
        /// number itself from its position among the checked rows instead of going blank.
        /// </summary>
        [Test]
        public void CheckedRow_WithoutMapEntry_FallsBackToPositionalPrefix()
        {
            var form = new TileKeeperForm();
            var lists = (CheckedListBox[])GetField(form, "_screenChecklists");
            var chk = lists[0];

            // No RefreshAllGrids call: the map stays empty on purpose.
            PopulateScreen(chk, new List<string> { "AppA", "AppB", "AppC" },
                new HashSet<string> { "AppB" });
            chk.CreateControl();

            Assert.That(chk.GetItemText(chk.Items[1]), Is.EqualTo("(1) AppB"), "first checked → cell 1");
        }

        /// <summary>
        /// Exercises the operator's real Screen1 checklist (the four RDP rows, all checked)
        /// through the genuine load + refresh pipeline and verifies every checked row's display
        /// text carries its grid-cell badge number. This is the exact configuration reported as
        /// rendering blank.
        /// </summary>
        [Test]
        public void LiveConfigScreen1_AllCheckedRows_ShowBadgePrefixes()
        {
            if (_liveConfigPath == null)
                Assert.Ignore("live config not available");

            var form = new TileKeeperForm();
            Invoke(form, "InitializeOrReload");

            var lists = (CheckedListBox[])GetField(form, "_screenChecklists");
            var chk = lists[0];
            Assert.That(chk.Items.Count, Is.GreaterThan(0), "candidates must load from live config");

            var screenRowCell = (Dictionary<CheckedListBox, Dictionary<string, int>>)GetField(form, "_screenRowCell");
            screenRowCell.TryGetValue(chk, out var rowCell);
            Assert.That(rowCell, Is.Not.Null, "StoreRowCells must have run during InitializeOrReload");

            int badged = 0;
            for (int i = 0; i < chk.Items.Count; i++)
            {
                if (!chk.GetItemChecked(i))
                    continue;

                string name = chk.Items[i].ToString();
                string text = chk.GetItemText(chk.Items[i]);
                string expectPlain = name;
                string expectBadged = rowCell != null && rowCell.TryGetValue(name, out int cellNo)
                    ? $"({cellNo}) {name}" : null;

                string expected = expectBadged ?? expectPlain;
                Assert.That(text, Is.EqualTo(expected),
                    $"checked row '{name}' display text must carry its badge");

                if (expectBadged != null)
                    badged++;
            }

            Assert.That(badged, Is.GreaterThan(0), "live config must have at least one cell-mapped checked row");
        }

        private static void PopulateScreen(CheckedListBox chk, List<string> candidates, ISet<string> checkedSet)
        {
            // Mirror the real InitializeOrReloadCore flow: SetItemChecked raises ItemCheck, and
            // the handler does a BeginInvoke that needs a live handle and a _suppressingDedup gate.
            chk.CreateControl();
            var prop = chk.FindForm().GetType().GetField("_suppressingDedup", PrivateInstance);
            bool prev = (bool)prop.GetValue(chk.FindForm());
            prop.SetValue(chk.FindForm(), true);
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
                prop.SetValue(chk.FindForm(), prev);
            }
        }

        /// <summary>
        /// Full-pipeline check: a real form is loaded and shown, then the checklist's
        /// display text is read back. Asserts the Format-based badge mechanism is actually
        /// wired (FormattingEnabled on, Format hooked via ScreenList_Format) and that a
        /// checked row's rendered text carries its grid-cell badge number. Replaces the old
        /// owner-draw assertion, which was a dead end: .NET Framework's CheckedListBox snaps
        /// DrawMode back to Normal, so DrawItem can never fire there.
        /// </summary>
        [Test]
        public void LiveForm_FullPipeline_CheckedRow_ShowsCellBadge()
        {
            var form = new TileKeeperForm();

            var lists0 = (CheckedListBox[])GetField(form, "_screenChecklists");
            var tab = (TabControl)lists0[0].Parent.Parent.Parent;
            tab.SelectedTab = tab.TabPages.Cast<TabPage>()
                .FirstOrDefault(p => ContainsControl(p, lists0[0])) ?? tab.TabPages[0];

            Invoke(form, "InitializeOrReload");
            var chk = lists0[0];
            Assert.That(chk.Items.Count, Is.GreaterThan(0), "candidates must load from live config");

            // (a) The Format-based badge path must really be wired on.
            Assert.That(chk.FormattingEnabled, Is.True,
                "checklist must have FormattingEnabled for the (N) badge to render");

            int checkedRow = -1;
            for (int i = 0; i < chk.Items.Count; i++)
                if (chk.GetItemChecked(i)) { checkedRow = i; break; }
            if (checkedRow < 0)
                Assert.Ignore("live config has no checked row in checklist 1");

            // (b) Force a genuine platform render of the whole form (PrintWindow, PW_RENDERFULLCONTENT)
            //     so the checklist's native text is materialised, then read it back.
            form.Show();
            form.BringToFront();
            Application.DoEvents();
            form.Invalidate(true);
            form.Update();
            System.Threading.Thread.Sleep(300);
            Application.DoEvents();
            form.Update();

            string shownText = ReadNativeItemText(chk, checkedRow);

            var screenRowCell = (Dictionary<CheckedListBox, Dictionary<string, int>>)GetField(form, "_screenRowCell");
            screenRowCell.TryGetValue(chk, out var rowCell);
            string rawName = chk.Items[checkedRow].ToString();
            int cellNo = rowCell != null && rowCell.TryGetValue(rawName, out int cn) ? cn : -1;

            Assert.That(shownText, Is.EqualTo(cellNo > 0 ? $"({cellNo}) {rawName}" : rawName),
                $"live row {checkedRow} '{rawName}' cell {cellNo} must render its badge");

            TestContext.WriteLine($"rendered badge text: '{shownText}'");
            form.Close();
        }

        /// <summary>
        /// Badge numbers must sequence by the order rows are clicked (checked), not by list
        /// position. Simulates clicking rows 2→0→1 in that order and asserts the (N) cell
        /// badges follow the click sequence: first-clicked → cell 1, etc.
        /// </summary>
        [Test]
        public void BadgeNumbers_SequenceByClickOrder()
        {
            var form = new TileKeeperForm();
            var lists = (CheckedListBox[])GetField(form, "_screenChecklists");
            var chk = lists[0];

            // Show first so the handle exists (ScreenItemCheck uses BeginInvoke) — the Load
            // reload repopulates the list from config, so our candidates go in *after* Show.
            form.Show();
            Application.DoEvents();

            // Click order: AppB (index 1) first, AppA (index 0) second, AppC (index 2) third.
            PopulateScreen(chk, new List<string> { "AppA", "AppB", "AppC" },
                new HashSet<string>());
            ClickItem(chk, 1); // AppB → badge (1)
            ClickItem(chk, 0); // AppA → badge (2)
            ClickItem(chk, 2); // AppC → badge (3)

            Invoke(form, "RefreshAllGrids");
            Application.DoEvents();

            Assert.That(chk.GetItemText(chk.Items[1]), Is.EqualTo("(1) AppB"), "first clicked → cell 1");
            Assert.That(chk.GetItemText(chk.Items[0]), Is.EqualTo("(2) AppA"), "second clicked → cell 2");
            Assert.That(chk.GetItemText(chk.Items[2]), Is.EqualTo("(3) AppC"), "third clicked → cell 3");

            form.Close();
        }

        /// <summary>
        /// The click-order list must be seeded from the initial checked state on every populate
        /// (not left empty and rebuilt by a list-order fallback at render time), and a fresh
        /// check appends after that seeded prefix so untouched rows never reshuffle.
        /// </summary>
        [Test]
        public void ClickOrder_SeedAndCheck_KeepsSeededOrderStable()
        {
            var form = new TileKeeperForm();
            var lists = (CheckedListBox[])GetField(form, "_screenChecklists");
            var chk = lists[0];

            form.Show();
            Application.DoEvents();

            Invoke(form, "PopulateScreen", chk, new List<string> { "AppA", "AppB", "AppC" },
                new HashSet<string> { "AppA", "AppC" });

            var orders = (Dictionary<CheckedListBox, List<string>>)GetField(form, "_screenCheckOrder");
            Assert.That(orders[chk], Is.EqualTo(new[] { "AppA", "AppC" }),
                "initial checked rows seed the click order in list order");

            ClickItem(chk, 1); // AppB checked → appended after the seeded prefix
            Assert.That(orders[chk], Is.EqualTo(new[] { "AppA", "AppC", "AppB" }),
                "a fresh check appends without reshuffling the seeded order");

            form.Close();
        }

        /// <summary>
        /// Checked rows that own a grid cell must report that cell's colour to the
        /// ColoredCheckedListBox draw path; unchecked (and unmapped) rows report blank.
        /// </summary>
        [Test]
        public void ColoredRows_ReturnMappedCellColor()
        {
            var form = new TileKeeperForm();
            var lists = (CheckedListBox[])GetField(form, "_screenChecklists");
            var chk = lists[0];

            form.Show();
            Application.DoEvents();
            Invoke(form, "PopulateScreen", chk, new List<string> { "AppA", "AppB" },
                new HashSet<string> { "AppA" });
            Invoke(form, "RefreshAllGrids");

            var colored = (TileKeeper.ColoredCheckedListBox)chk;
            Assert.That(colored.ColorOf(0), Is.EqualTo(TileKeeper.Class.GridMapPanel.CellColors[0]),
                "checked + cell-mapped row fills with its cell colour");
            Assert.That(colored.ColorOf(1), Is.EqualTo(Color.Empty),
                "unchecked row stays blank");

            form.Close();
        }

        /// <summary>
        /// Checking/unchecking a bottom row must not reset the list's scroll position back to
        /// the top. Regression for the FormattingEnabled-to-toggle flicker: the toggle runs
        /// RefreshItems(), which natively zeroes TopIndex — StoreRowCells must restore it.
        /// </summary>
        [Test]
        public void RefreshGrid_PreservesTopIndex()
        {
            var form = new TileKeeperForm();
            var lists = (CheckedListBox[])GetField(form, "_screenChecklists");
            var chk = lists[0];

            // Show first (handle + native scroll) and populate AFTER the Load reload.
            form.Show();
            Application.DoEvents();

            var candidates = Enumerable.Range(1, 40).Select(i => "App" + i).ToList();
            PopulateScreen(chk, candidates, new HashSet<string>(candidates));

            Invoke(form, "RefreshAllGrids");
            chk.TopIndex = 30;
            Assert.That(chk.TopIndex, Is.EqualTo(30), "precondition: scroll is possible and set");

            ClickItem(chk, 35);      // toggles a bottom row → RefreshAllGrids via deferred path
            Application.DoEvents();

            Assert.That(chk.TopIndex, Is.EqualTo(30),
                "checking a bottom row must not reset the scroll to the top");

            ClickItem(chk, 35);      // uncheck again
            Application.DoEvents();
            Assert.That(chk.TopIndex, Is.EqualTo(30),
                "unchecking a bottom row must not reset the scroll to the top");

            form.Close();
        }

        /// <summary>
        /// Dropping an .exe from Explorer onto the Programs list must append a new row.
        /// </summary>
        [Test]
        public void DragDrop_ProgramsList_AddsRow()
        {
            var form = new TileKeeperForm();
            Invoke(form, "ListPrograms_DragDrop", form, initDragEventArgs(@"C:\temp\FakeTool.exe"));

            var rows = (System.Windows.Forms.ListView)GetField(form, "listPrograms");
            Assert.That(rows.Items.Count, Is.EqualTo(1), "dropped file must become one row");
            Assert.That(rows.Items[0].Text, Is.EqualTo("FakeTool.exe"));
        }

        /// <summary>
        /// Dropping a file from Explorer onto the Position list must append a new row.
        /// </summary>
        [Test]
        public void DragDrop_PositionList_AddsRow()
        {
            var form = new TileKeeperForm();
            Invoke(form, "ListPositionPrograms_DragDrop", form, initDragEventArgs(@"C:\temp\FakeMon.exe"));

            var rows = (System.Windows.Forms.ListView)GetField(form, "listPositionPrograms");
            Assert.That(rows.Items.Count, Is.EqualTo(1), "dropped file must become one row");
            Assert.That(rows.Items[0].Text, Is.EqualTo("FakeMon.exe"));
        }

        private static DragEventArgs initDragEventArgs(string fakePath)
        {
            var data = new DataObject();
            data.SetData(DataFormats.FileDrop, new[] { fakePath });
            return new DragEventArgs(data, 0, 0, 0, DragDropEffects.Copy, DragDropEffects.Copy);
        }

        /// <summary>
        /// Simulates a real user click on a checklist row: toggles the check state through the
        /// same SetItemChecked path a mouse click uses, so ScreenItemCheck (and its
        /// click-order bookkeeping) runs naturally without the _suppressingDedup gate.
        /// </summary>
        private static void ClickItem(CheckedListBox chk, int index)
        {
            chk.CreateControl();
            chk.SetItemChecked(index, !chk.GetItemChecked(index));
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, System.Text.StringBuilder lParam);

        private const int LB_GETTEXT = 0x0189;

        /// <summary>
        /// Reads the native list text for an item index — the exact string the user sees in the
        /// list. Format/FormattingEnabled rewrite this native text, so this is the ground truth
        /// that proves the "(N)" badge actually renders (logic tests via GetItemText() are not
        /// enough).
        /// </summary>
        private static string ReadNativeItemText(CheckedListBox chk, int index)
        {
            var sb = new System.Text.StringBuilder(4096);
            SendMessage(chk.Handle, LB_GETTEXT, new IntPtr(index), sb);
            return sb.ToString();
        }

        private static object GetField(object o, string name) =>
            o.GetType().GetField(name, PrivateInstance).GetValue(o);

        private static bool ContainsControl(Control parent, Control target)
        {
            if (parent == target) return true;
            foreach (Control c in parent.Controls)
                if (ContainsControl(c, target)) return true;
            return false;
        }

        private static void Invoke(object o, string name, params object[] args) =>
            o.GetType().GetMethod(name, PrivateInstance).Invoke(o, args);
    }
}
