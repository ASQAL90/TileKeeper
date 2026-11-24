using TileKeeper.Class;
using System;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace TileKeeper
{
    /// <summary>
    /// Application entry point.
    /// </summary>
    static class Program
    {
        private const string MinimalConfigSkeleton =
@"<?xml version=""1.0"" encoding=""utf-8""?>
<configuration>
  <configSections>
    <sectionGroup name=""userSettings"" type=""System.Configuration.UserSettingsGroup, System, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089"">
      <section name=""TileKeeper.Properties.Settings"" type=""System.Configuration.ClientSettingsSection, System, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089"" allowExeDefinition=""MachineToLocalUser"" requirePermission=""false""/>
    </sectionGroup>
  </configSections>
  <appSettings>
    <add key=""EnableTerminateProtection"" value=""True""/>
  </appSettings>
  <startup>
    <supportedRuntime version=""v4.0"" sku="".NETFramework,Version=v4.8""/>
  </startup>
  <userSettings>
    <TileKeeper.Properties.Settings>
      <setting name=""LastVersion"" serializeAs=""String"">
        <value/>
      </setting>
    </TileKeeper.Properties.Settings>
  </userSettings>
</configuration>
";

        /// <summary>
        /// Creates the main form and starts the WinForms message loop.
        ///
        /// A named <see cref="Mutex"/> is acquired first so a second instance never performs
        /// config repair or starts a replacement process. If a second instance is launched, it
        /// shows an error and exits without creating a second tray icon or background threads.
        ///
        /// The DACL is set before any form or thread is created, so there is no window in which
        /// a user could kill the process before <c>Form1_Load</c> runs.
        /// </summary>
        [STAThread]
        static void Main()
        {
            // "UniqueAppNameMutex" is session-wide; createdNew is true only for the first
            // instance. Hold it for the whole process lifetime (disposed at the end of Main).
            bool createdNew;
            using (Mutex mutex = new Mutex(true, "UniqueAppNameMutex", out createdNew))
            {
                if (!createdNew)
                {
                    MessageBox.Show("Application is already running", "Error",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                // Repair a missing config in-process. Nothing has read ConfigurationManager yet,
                // so writing the file now lets the first later AppSettings read succeed. The old
                // Process.Start() relaunch could spawn a replacement on every launch (and, if the
                // config was on a read-only path, kept doing so) — it is intentionally removed.
                EnsureConfigFileExists();

                // Lock down the process before any form or thread is created, unless the user
                // opted out in app.config. A missing/invalid value keeps the protected behaviour.
                if (IsTerminateProtectionEnabled())
                    ProcessProtector.DenyTerminateForStandardUsers();
                else
                    Logger.Log("Process protection disabled in config; End Task remains available.");

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new TileKeeperForm());
            }
        }

        /// <summary>
        /// Returns whether the process should deny End Task for standard users. Missing,
        /// empty, or unparseable config values keep the built-in protected behaviour so
        /// upgrading an older install never silently weakens the kiosk guard.
        /// </summary>
        private static bool IsTerminateProtectionEnabled()
        {
            string value = ConfigurationManager.AppSettings["EnableTerminateProtection"];
            return !bool.TryParse(value, out bool enabled) || enabled;
        }

        /// <summary>
        /// Recreates a missing exe.config in the current process. No new process is started:
        /// the configuration system reads the file lazily on first use, and this runs before
        /// any AppSettings access, so a plain write plus RefreshSection is sufficient. Removing
        /// the old relaunch also removes the source of repeated instance launches.
        /// </summary>
        private static void EnsureConfigFileExists()
        {
            string configPath = Application.ExecutablePath + ".config";
            if (File.Exists(configPath))
                return;

            try
            {
                File.WriteAllText(configPath, MinimalConfigSkeleton);
                ConfigurationManager.RefreshSection("appSettings");
                Logger.Log($"Recreated missing config at {configPath}.");
            }
            catch (Exception ex)
            {
                Logger.Log($"Could not recreate missing config at {configPath}: {ex.Message}");
                MessageBox.Show(
                    "Could not create the configuration file. The program will start with default settings.\n\n" + ex.Message,
                    "Configuration Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}
