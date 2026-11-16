using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace TileKeeper.Class
{
    /// <summary>
    /// Mirrors the Win32 RECT structure used by user32.dll window functions.
    /// All coordinates are in screen pixels relative to the virtual screen origin.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    /// <summary>
    /// Abstracts the subset of user32.dll functions used by <see cref="WindowMonitor"/>.
    /// Keeping these behind an interface lets unit tests inject a mock instead of
    /// hitting real Win32 calls, which require live windows and elevated permissions.
    /// </summary>
    public interface IWin32Api
    {
        /// <summary>
        /// Enumerates the top-level window handles owned by the process with id
        /// <paramref name="processId"/>. Native implementation walks EnumWindows and
        /// filters by GetWindowThreadProcessId; the mock returns a preloaded list.
        /// </summary>
        List<IntPtr> GetProcessWindowHandles(int processId);

        /// <summary>
        /// Enumerates every top-level window in ONE pass, returning (handle, owning
        /// process id) pairs. The native implementation walks EnumWindows once and reads
        /// GetWindowThreadProcessId per handle; callers bucket the results by pid instead
        /// of running a full EnumWindows per process. The mock returns a preloaded list.
        /// </summary>
        List<(IntPtr Handle, int ProcessId)> EnumerateTopLevelWindows();

        /// <summary>Returns the registered Win32 window class name of <paramref name="hWnd"/> ("" on failure).</summary>
        string GetWindowClassName(IntPtr hWnd);

        /// <summary>Returns true if the window is visible on screen (not hidden or minimized to tray).</summary>
        bool IsWindowVisible(IntPtr hWnd);

        /// <summary>Returns true if the window is currently minimized (iconic).</summary>
        bool IsIconic(IntPtr hWnd);

        /// <summary>Returns true if the handle still refers to a valid (not destroyed) window.</summary>
        bool IsWindow(IntPtr hWnd);

        /// <summary>Returns the owner/message parent of the window, or IntPtr.Zero if it is a top-level window.</summary>
        IntPtr GetParent(IntPtr hWnd);

        /// <summary>Fills <paramref name="lpRect"/> with the window's bounding rectangle in screen coordinates.</summary>
        void GetWindowRect(IntPtr hWnd, out RECT lpRect);

        /// <summary>
        /// Reads a window attribute. Pass <c>GWL_STYLE (-16)</c> to get the style flags
        /// (e.g. WS_MAXIMIZEBOX, WS_CAPTION).
        /// </summary>
        uint GetWindowLong(IntPtr hWnd, int nIndex);

        /// <summary>Writes a window attribute. Returns false on failure.</summary>
        bool SetWindowLong(IntPtr hWnd, int nIndex, uint dwNewLong);

        /// <summary>
        /// Controls how a window is displayed.
        /// Common values: SW_SHOW (5) = show normal, SW_RESTORE (9) = un-minimize/un-maximize.
        /// </summary>
        bool ShowWindow(IntPtr hWnd, int nCmdShow);

        /// <summary>
        /// Moves and resizes a window. <paramref name="hWndInsertAfter"/> sets Z-order;
        /// pass <see cref="IntPtr.Zero"/> with <c>SWP_NOZORDER</c> to leave Z-order unchanged.
        /// </summary>
        bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

        /// <summary>
        /// Moves and resizes a window to the same rect as <see cref="SetWindowPos"/>, used as a
        /// fallback when SetWindowPos is refused (e.g. error 183) for a particular window class.
        /// </summary>
        bool MoveWindow(IntPtr hWnd, int x, int y, int w, int h, bool repaint);

        /// <summary>Returns the topmost window (desktop Z-order head), or <see cref="IntPtr.Zero"/> if none.</summary>
        IntPtr GetTopWindow(IntPtr hWnd);

        /// <summary>
        /// Returns the full command line of the process with id <paramref name="pid"/>,
        /// or null on any failure (WMI unavailable, permission denied, process exited).
        /// Callers treat null as "args unknown" and fall back to name-only matching.
        /// </summary>
        string GetProcessCommandLine(int pid);

        /// <summary>Brings the window to the foreground and gives it keyboard focus.</summary>
        bool SetForegroundWindow(IntPtr hWnd);

        /// <summary>
        /// Forces the window (plus its frame and child windows) to repaint immediately.
        /// Clears the stale surface ghost left behind when WS_CAPTION changes while the
        /// window stays in place. <paramref name="flags"/> is a combination of RDW_* bits.
        /// </summary>
        bool RedrawWindow(IntPtr hWnd, uint flags);

        /// <summary>
        /// Sets a Desktop Window Manager attribute on <paramref name="hWnd"/> (e.g. the
        /// non-client rendering policy used to suppress the drop shadow on border-mode
        /// windows). <paramref name="attribute"/>, <paramref name="value"/> and
        /// <paramref name="valueSize"/> mirror dwmapi's DwmSetWindowAttribute.
        /// </summary>
        int DwmSetWindowAttribute(IntPtr hWnd, int attribute, ref int value, int valueSize);
    }
}
