using System;
using System.IO;

namespace TileKeeper.Class
{
    /// <summary>
    /// Thread-safe file logger used by background threads instead of MessageBox.Show.
    ///
    /// Background threads must never call MessageBox.Show directly. A dialog with no
    /// parent window appears behind all other windows on Windows, so the user never
    /// sees it — but the thread is blocked waiting for a click that never comes.
    /// Over time, multiple blocked threads accumulate and the monitoring features stop.
    ///
    /// Log file location: %AppData%\TileKeeper\error.log
    /// </summary>
    internal static class Logger
    {
        private static readonly string LogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TileKeeper",
            "error.log");

        private static readonly object _lock = new object();

        /// <summary>
        /// Appends a timestamped line to the log file.
        /// Silently discards the message if the write itself fails (e.g. disk full).
        /// </summary>
        public static void Log(string message)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath));
                lock (_lock)
                {
                    File.AppendAllText(LogPath,
                        $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
                }
            }
            catch
            {
                // Never throw from the logger itself
            }
        }
    }
}
