using System;
using System.Collections.Generic;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;

namespace TileKeeper.Class
{
    /// <summary>
    /// Production implementation of <see cref="IWin32Api"/> that delegates directly
    /// to the real user32.dll P/Invoke functions.
    /// This class is used at runtime; tests inject <c>MockWin32Api</c> instead.
    /// </summary>
    public class Win32ApiImpl : IWin32Api
    {
        // ── P/Invoke declarations ─────────────────────────────────────────────
        // Each entry maps one-to-one to a user32.dll export.
        // They are private because callers should only go through IWin32Api.

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern uint GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern bool SetWindowLong(IntPtr hWnd, int nIndex, uint dwNewLong);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern IntPtr GetParent(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool MoveWindow(IntPtr hWnd, int x, int y, int w, int h, bool repaint);

        [DllImport("user32.dll")]
        private static extern IntPtr GetTopWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool RedrawWindow(IntPtr hWnd, IntPtr lprcUpdate, IntPtr hrgnUpdate, uint flags);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

        // ── IWin32Api forwarding ──────────────────────────────────────────────
        // Explicit interface implementation keeps these hidden from callers who
        // hold a concrete Win32ApiImpl reference — they must go through IWin32Api.

        List<IntPtr> IWin32Api.GetProcessWindowHandles(int processId)
        {
            var results = new List<IntPtr>();
            EnumWindows(delegate (IntPtr hWnd, IntPtr lParam)
            {
                uint pid;
                GetWindowThreadProcessId(hWnd, out pid);
                if (pid == processId)
                    results.Add(hWnd);
                return true;
            }, IntPtr.Zero);
            return results;
        }

        List<(IntPtr, int)> IWin32Api.EnumerateTopLevelWindows()
        {
            var results = new List<(IntPtr, int)>();
            EnumWindows(delegate (IntPtr hWnd, IntPtr lParam)
            {
                uint pid;
                GetWindowThreadProcessId(hWnd, out pid);
                results.Add((hWnd, (int)pid));
                return true;
            }, IntPtr.Zero);
            return results;
        }

        string IWin32Api.GetWindowClassName(IntPtr hWnd)
        {
            var sb = new StringBuilder(256);
            GetClassName(hWnd, sb, sb.Capacity);
            return sb.ToString();
        }

        bool IWin32Api.IsWindowVisible(IntPtr hWnd) => IsWindowVisible(hWnd);

        bool IWin32Api.IsIconic(IntPtr hWnd) => IsIconic(hWnd);

        bool IWin32Api.IsWindow(IntPtr hWnd) => IsWindow(hWnd);

        void IWin32Api.GetWindowRect(IntPtr hWnd, out RECT lpRect) => GetWindowRect(hWnd, out lpRect);

        uint IWin32Api.GetWindowLong(IntPtr hWnd, int nIndex) => GetWindowLong(hWnd, nIndex);

        bool IWin32Api.SetWindowLong(IntPtr hWnd, int nIndex, uint dwNewLong) => SetWindowLong(hWnd, nIndex, dwNewLong);

        bool IWin32Api.ShowWindow(IntPtr hWnd, int nCmdShow) => ShowWindow(hWnd, nCmdShow);

        IntPtr IWin32Api.GetParent(IntPtr hWnd) => GetParent(hWnd);

        bool IWin32Api.SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags)
            => SetWindowPos(hWnd, hWndInsertAfter, x, y, cx, cy, uFlags);

        bool IWin32Api.MoveWindow(IntPtr hWnd, int x, int y, int w, int h, bool repaint)
            => MoveWindow(hWnd, x, y, w, h, repaint);

        IntPtr IWin32Api.GetTopWindow(IntPtr hWnd) => GetTopWindow(hWnd);

        string IWin32Api.GetProcessCommandLine(int pid)
        {
            // Command lines are immutable for a process's lifetime, so callers that poll
            // every monitor tick would otherwise re-run the (expensive) WMI query each time.
            // Key the cache on (pid, start time) — StartTime is unreadable for some
            // protected processes, and there we fall back to an uncached query so the
            // result is exactly what the pre-cache code returned.
            long startTicks;
            try
            {
                using (var process = System.Diagnostics.Process.GetProcessById(pid))
                    startTicks = process.StartTime.ToUniversalTime().Ticks;
            }
            catch
            {
                return QueryCommandLine(pid);
            }

            return CommandLineCache.GetOrFetch(pid, startTicks, () => QueryCommandLine(pid));
        }

        // Static so the cache outlives the per-tick WindowMonitor/Win32ApiImpl instances.
        private static readonly ProcessCommandLineCache CommandLineCache = new ProcessCommandLineCache();

        /// <summary>
        /// Runs the actual WMI query. Win32_Process is the cheapest reliable command-line
        /// source on this tree; NT-API is fragile cross-bitness. Wrapped so it can never
        /// throw into the monitor loop — null signals the caller to fall back to name-only.
        /// </summary>
        private static string QueryCommandLine(int pid)
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher(
                    "SELECT CommandLine FROM Win32_Process WHERE ProcessId=" + pid))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        using (obj)
                        {
                            object v = obj["CommandLine"];
                            return v as string;
                        }
                    }
                }
            }
            catch
            {
                // WMI unavailable — null signals the caller to fall back to name-only
            }
            return null;
        }

        bool IWin32Api.SetForegroundWindow(IntPtr hWnd) => SetForegroundWindow(hWnd);

        bool IWin32Api.RedrawWindow(IntPtr hWnd, uint flags) => RedrawWindow(hWnd, IntPtr.Zero, IntPtr.Zero, flags);

        int IWin32Api.DwmSetWindowAttribute(IntPtr hWnd, int attribute, ref int value, int valueSize)
            => DwmSetWindowAttribute(hWnd, attribute, ref value, valueSize);
    }
}
