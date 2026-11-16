using System;
using System.Collections.Generic;
using System.Diagnostics;
using NUnit.Framework;
using TileKeeper.Class;

namespace TileKeeper.Tests
{
    /// <summary>In-memory <see cref="IWin32Api"/> for exercising WindowMonitor without real windows.</summary>
    internal class MockWin32Api : IWin32Api
    {
        public List<(IntPtr Handle, int ProcessId)> TopLevelWindows = new List<(IntPtr, int)>();
        public int EnumerateTopLevelWindowsCount;
        public int GetProcessWindowHandlesCount;
        public Dictionary<IntPtr, RECT> Rects = new Dictionary<IntPtr, RECT>();
        public HashSet<IntPtr> Visible = new HashSet<IntPtr>();
        public List<IntPtr> SetWindowPosHandles = new List<IntPtr>();

        public string GetWindowClassName(IntPtr hWnd) => "";
        public bool IsWindowVisible(IntPtr hWnd) => Visible.Contains(hWnd);
        public bool IsIconic(IntPtr hWnd) => false;
        public IntPtr GetParent(IntPtr hWnd) => IntPtr.Zero;
        public void GetWindowRect(IntPtr hWnd, out RECT lpRect) =>
            lpRect = Rects.TryGetValue(hWnd, out RECT r) ? r : new RECT();
        public uint GetWindowLong(IntPtr hWnd, int nIndex) => 0;
        public bool SetWindowLong(IntPtr hWnd, int nIndex, uint dwNewLong) => true;
        public bool ShowWindow(IntPtr hWnd, int nCmdShow) => true;
        public bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags)
        {
            SetWindowPosHandles.Add(hWnd);
            return true;
        }
        public bool MoveWindow(IntPtr hWnd, int x, int y, int w, int h, bool repaint) => true;
        public IntPtr GetTopWindow(IntPtr hWnd) => IntPtr.Zero;
        public string GetProcessCommandLine(int pid) => null;
        public bool SetForegroundWindow(IntPtr hWnd) => true;
        public bool RedrawWindow(IntPtr hWnd, uint flags) => true;
        public int DwmSetWindowAttribute(IntPtr hWnd, int attribute, ref int value, int valueSize) => 0;
        public bool IsWindow(IntPtr hWnd) => true;

        List<IntPtr> IWin32Api.GetProcessWindowHandles(int processId)
        {
            GetProcessWindowHandlesCount++;
            return new List<IntPtr>();
        }

        List<(IntPtr, int)> IWin32Api.EnumerateTopLevelWindows()
        {
            EnumerateTopLevelWindowsCount++;
            return new List<(IntPtr, int)>(TopLevelWindows);
        }
    }

    /// <summary>
    /// P1-5: MatchLargest window resolution must use ONE top-level enumeration per monitor
    /// tick, never a fresh EnumWindows per process.
    /// </summary>
    [TestFixture]
    public class WindowMonitorMatchLargestTests
    {
        [Test]
        public void BucketOverload_PicksLargestVisible_WithoutPerPidPass()
        {
            var mock = new MockWin32Api();
            var small = new IntPtr(1);
            var big = new IntPtr(2);
            foreach (var h in new[] { small, big })
                mock.Visible.Add(h);
            mock.Rects[small] = new RECT { Right = 100, Bottom = 100 }; // 10,000 px²
            mock.Rects[big]   = new RECT { Right = 300, Bottom = 200 }; // 60,000 px²

            var monitor = new WindowMonitor(mock);
            var bucket = new Dictionary<int, List<IntPtr>> { [42] = new List<IntPtr> { big, small } };

            IntPtr picked = monitor.ResolveLargestVisibleWindow(42, bucket);

            Assert.That(picked, Is.EqualTo(big));
            Assert.That(mock.GetProcessWindowHandlesCount, Is.EqualTo(0), "bucket must bypass the per-pid enumeration");
            Assert.That(mock.EnumerateTopLevelWindowsCount, Is.EqualTo(0));
        }

        [Test]
        public void ManageProgramScreenPosition_EnumeratesTopLevelWindows_OncePerTick()
        {
            var mock = new MockWin32Api();
            var win = new IntPtr(0xABC);
            int currentPid = Process.GetCurrentProcess().Id;
            mock.TopLevelWindows.Add((win, currentPid));
            mock.Visible.Add(win);
            mock.Rects[win] = new RECT { Right = 200, Bottom = 150 };

            var assignments = new[]
            {
                new ScreenAssignment(
                    new List<string> { Process.GetCurrentProcess().ProcessName },
                    true, 1, 1, new[] { -1 },
                    args: new List<string> { "" },
                    matchLargest: new List<bool> { true })
            };

            new WindowMonitor(mock).ManageProgramScreenPosition(assignments);

            Assert.That(mock.EnumerateTopLevelWindowsCount, Is.EqualTo(1), "exactly one EnumWindows pass per tick");
            Assert.That(mock.GetProcessWindowHandlesCount, Is.EqualTo(0), "no per-process EnumWindows pass");
            Assert.That(mock.SetWindowPosHandles, Does.Contain(win), "the bucket-resolved window still drives positioning");
        }
    }
}
