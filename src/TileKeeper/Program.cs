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
        private const string RelaunchedEnvVar = "PT_CONFIG_RELAUNCHED";

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
        /// A named <see cref="Mutex"/> prevents more than one instance from running at
        /// the same time. If a second instance is launched, it shows an error and exits
        /// without creating a second tray icon or starting the background threads.
        ///
        /// The DACL is set as the very first action so there is no window between process
        /// start and protection being active. This prevents a user from killing the process
        /// in the brief moment before <c>Form1_Load</c> runs.
        /// </summary>
        [STAThread]
        static void Main()
        {
            // If the exe.config is missing (deleted by hand, fresh install, etc.), recreate it
            // and relaunch as a brand-new process — before the Mutex or the form exist. The
            // .NET configuration system caches itself permanently once anything in this process
            // has touched a missing config file; rewriting the file to disk mid-process does not
            // recover it (every later ConfigurationManager.AppSettings read keeps throwing
            // ConfigurationErrorsException). A cold process is the only reliable fix. The
            // relaunched child is marked via an environment variable so a second failure (e.g.
            // no write permission) falls through to running with in-memory defaults instead of
            // relaunching forever.
            if (RecreateConfigAndRelaunchIfMissing())
                return;

            // Lock down the process immediately — before any form or thread is created, but
            // only when the user has not opted out in app.config. Deny PROCESS_TERMINATE for
            // standard users so "End Task" in Task Manager is blocked by default. This works
            // without elevation: the process owner always has WRITE_DAC on their own process.
            // A missing, empty, or invalid value keeps the historic protected behaviour.
            if (IsTerminateProtectionEnabled())
                ProcessProtector.DenyTerminateForStandardUsers();
            else
                Logger.Log("Process protection disabled in config; End Task remains available.");

            // "UniqueAppNameMutex" is a system-wide name; createdNew is true only for the first instance
            bool createdNew;
            using (Mutex mutex = new Mutex(true, "UniqueAppNameMutex", out createdNew))
            {
                if (createdNew)
                {
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    Application.Run(new TileKeeperForm());
                }
                else
                {
                    MessageBox.Show("Application is already running", "Error",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
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
        /// Returns true (and has already relaunched a replacement process) when the exe.config
        /// was missing and needed a cold-process restart to take effect. Returns false when the
        /// config already existed, or when recreating/relaunching failed — in the latter case
        /// the caller should continue starting up normally with in-memory defaults.
        /// </summary>
        private static bool RecreateConfigAndRelaunchIfMissing()
        {
            string configPath = Application.ExecutablePath + ".config";
            if (File.Exists(configPath))
                return false;

            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(RelaunchedEnvVar)))
            {
                // Already relaunched once and the config is still missing — don't loop forever.
                Logger.Log("Config still missing after relaunch; continuing with in-memory defaults.");
                return false;
            }

            try
            {
                File.WriteAllText(configPath, MinimalConfigSkeleton);
            }
            catch (Exception ex)
            {
                Logger.Log($"Could not recreate missing config at {configPath}: {ex.Message}");
                MessageBox.Show(
                    "Could not create the configuration file. The program will start with default settings.\n\n" + ex.Message,
                    "Configuration Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            try
            {
                var startInfo = new ProcessStartInfo(Application.ExecutablePath)
                {
                    UseShellExecute = false
                };
                startInfo.EnvironmentVariables[RelaunchedEnvVar] = "1";
                Process.Start(startInfo);
            }
            catch (Exception ex)
            {
                Logger.Log($"Could not relaunch after recreating config: {ex.Message}");
                return false;
            }

            return true;
        }
    }
}
