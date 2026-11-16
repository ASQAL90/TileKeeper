using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;

namespace TileKeeper.Class
{
    /// <summary>
    /// Holds the effective resolution of one attached display: its device name and its
    /// current bounds (position on the desktop + pixel size).
    /// </summary>
    public class DisplayState
    {
        public string   DeviceName;
        public Rectangle Bounds;

        public int Width  => Bounds.Width;
        public int Height => Bounds.Height;
    }

    /// <summary>
    /// Enforces an upper resolution cap on every attached display (NQVM-safe hold).
    ///
    /// Only oversized displays are clamped — a display already running at or below the
    /// cap is left untouched, so an operator can still voluntarily drop below the cap.
    /// Applying uses <c>CDS_UPDATEREGISTRY|CDS_RESET</c> so the clamped mode survives a
    /// reboot (and NQVM restarts) instead of reverting on the next display settings flush.
    /// </summary>
    public class DisplayResolutionManager
    {
        // ── ChangeDisplaySettingsEx flags / return values ─────────────────────
        private const int CDS_UPDATEREGISTRY = 0x0001; // Persist to registry
        private const int CDS_RESET          = 0x40000000; // Settings changed globally
        private const int DISP_CHANGE_SUCCESSFUL = 0;

        // ── EnumDisplayDevices flags ──────────────────────────────────────────
        private const uint DISPLAY_DEVICE_ATTACHED_TO_DESKTOP = 0x1;

        // ── EnumDisplaySettings mode numbers ──────────────────────────────────
        private const uint ENUM_CURRENT_SETTINGS = 0xFFFFFFFF; // == -1

        // ── DEVMODE.dmFields bits ─────────────────────────────────────────────
        private const uint DM_POSITION          = 0x00000020;
        private const uint DM_PELSWIDTH         = 0x00080000;
        private const uint DM_PELSHEIGHT        = 0x00100000;
        private const uint DM_DISPLAYFREQUENCY  = 0x00040000;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool EnumDisplayDevices(
            string lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool EnumDisplaySettings(string lpszDeviceName, uint iModeNum, ref DEVMODE lpDevMode);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int ChangeDisplaySettingsEx(
            string lpszDeviceName, ref DEVMODE lpDevMode, IntPtr hwnd, uint dwflags, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DISPLAY_DEVICE
        {
            public uint cb;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]   public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]  public string DeviceString;
            public uint StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]  public string DeviceID;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]  public string DeviceKey;
        }

        // DEVMODEW — sequential layout (display branch of the union only, which is 16 bytes).
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DEVMODE
        {
            private const int CCHDEVICENAME = 32;
            private const int CCHFORMNAME   = 32;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCHDEVICENAME)] public string dmDeviceName;
            public ushort dmSpecVersion;
            public ushort dmDriverVersion;
            public ushort dmSize;
            public ushort dmDriverExtra;
            public uint   dmFields;
            // union (16 bytes on the display branch): POINTL dmPosition + dmDisplayOrientation + dmDisplayFixedOutput
            public int    dmPositionX;
            public int    dmPositionY;
            public uint   dmDisplayOrientation;
            public uint   dmDisplayFixedOutput;
            public short  dmColor;
            public short  dmDuplex;
            public short  dmYResolution;
            public short  dmTTOption;
            public short  dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCHFORMNAME)] public string dmFormName;
            public ushort dmLogPixels;
            public uint   dmBitsPerPel;
            public uint   dmPelsWidth;
            public uint   dmPelsHeight;
            public uint   dmDisplayFlags;
            public uint   dmDisplayFrequency;
        }

        /// <summary>
        /// Enumerates all displays attached to the desktop and returns their current bounds.
        /// </summary>
        public List<DisplayState> GetCurrentStates()
        {
            var result = new List<DisplayState>();

            uint deviceIndex = 0;
            var dd = new DISPLAY_DEVICE { cb = (uint)Marshal.SizeOf(typeof(DISPLAY_DEVICE)) };

            while (EnumDisplayDevices(null, deviceIndex++, ref dd, 0))
            {
                if ((dd.StateFlags & DISPLAY_DEVICE_ATTACHED_TO_DESKTOP) != 0)
                {
                    DEVMODE? dm = GetCurrentMode(dd.DeviceName);
                    if (dm != null)
                    {
                        result.Add(new DisplayState
                        {
                            DeviceName = dd.DeviceName,
                            Bounds = new Rectangle(dm.Value.dmPositionX, dm.Value.dmPositionY,
                                (int)dm.Value.dmPelsWidth, (int)dm.Value.dmPelsHeight)
                        });
                    }
                }

                dd = new DISPLAY_DEVICE { cb = (uint)Marshal.SizeOf(typeof(DISPLAY_DEVICE)) };
            }

            return result;
        }

        /// <summary>
        /// Clamps every attached display whose current resolution exceeds
        /// <paramref name="maxWidth"/>×<paramref name="maxHeight"/> down to the largest
        /// advertised mode that fits inside the cap. Returns how many displays were changed.
        /// </summary>
        public int NormalizeDisplays(int maxWidth, int maxHeight)
        {
            if (maxWidth <= 0 || maxHeight <= 0)
                return 0;

            int changed = 0;
            foreach (DisplayState state in GetCurrentStates())
            {
                // Only oversized displays are clamped — never fight the operator downward
                if (state.Width <= maxWidth && state.Height <= maxHeight)
                    continue;

                (int w, int h) target = FindCapMode(state.DeviceName, maxWidth, maxHeight);
                if (target.w <= 0 || target.h <= 0)
                    continue; // Display offers no compliant mode — leave it alone

                DEVMODE? dm = GetCurrentMode(state.DeviceName);
                if (dm == null)
                    continue;

                DEVMODE mode = dm.Value;
                mode.dmPelsWidth  = (uint)target.w;
                mode.dmPelsHeight = (uint)target.h;
                mode.dmFields |= DM_POSITION | DM_PELSWIDTH | DM_PELSHEIGHT | DM_DISPLAYFREQUENCY;

                if (ChangeDisplaySettingsEx(state.DeviceName, ref mode, IntPtr.Zero,
                        CDS_UPDATEREGISTRY | CDS_RESET, IntPtr.Zero) == DISP_CHANGE_SUCCESSFUL)
                    changed++;
            }

            return changed;
        }

        /// <summary>
        /// Returns the largest advertised mode (by area) of <paramref name="deviceName"/>
        /// that fits within <paramref name="maxWidth"/>×<paramref name="maxHeight"/>.
        /// Returns (0,0) when no compliant mode exists.
        ///
        /// The advertised mode table of a device does not change when its current resolution
        /// is clamped, so it is enumerated once per device and reused across the (every-10s)
        /// hold-loop ticks. Changing the display's resolution mid-tick does not invalidate it.
        /// </summary>
        private (int w, int h) FindCapMode(string deviceName, int maxWidth, int maxHeight)
        {
            if (!_deviceModes.TryGetValue(deviceName, out var modes))
            {
                modes = new List<(int w, int h)>();
                for (uint modeIndex = 0; ; modeIndex++)
                {
                    var dm = new DEVMODE { dmSize = (ushort)Marshal.SizeOf(typeof(DEVMODE)) };
                    if (!EnumDisplaySettings(deviceName, modeIndex, ref dm))
                        break; // No more modes
                    modes.Add(((int)dm.dmPelsWidth, (int)dm.dmPelsHeight));
                }
                _deviceModes[deviceName] = modes;
            }

            int bestW = 0, bestH = 0, bestArea = -1;
            foreach (var (w, h) in modes)
            {
                if (w <= 0 || h <= 0 || w > maxWidth || h > maxHeight)
                    continue;

                int area = w * h;
                if (area > bestArea)
                {
                    bestArea = area;
                    bestW = w;
                    bestH = h;
                }
            }

            return (bestW, bestH);
        }

        // Mode tables are static per physical display for the life of the app; one
        // enumeration per deviceName keeps the 10s hold loop from re-hitting P/Invoke.
        private readonly Dictionary<string, List<(int w, int h)>> _deviceModes =
            new Dictionary<string, List<(int w, int h)>>(StringComparer.OrdinalIgnoreCase);

        private DEVMODE? GetCurrentMode(string deviceName)
        {
            var dm = new DEVMODE { dmSize = (ushort)Marshal.SizeOf(typeof(DEVMODE)) };
            return EnumDisplaySettings(deviceName, ENUM_CURRENT_SETTINGS, ref dm) ? (DEVMODE?)dm : null;
        }
    }
}