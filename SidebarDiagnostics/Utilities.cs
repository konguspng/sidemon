using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Windows;
using System.Windows.Markup;
using SidebarDiagnostics.Framework;

namespace SidebarDiagnostics.Utilities
{
    public static class Paths
    {
        private const string SETTINGS = "settings.json";
        private const string CHANGELOG = "ChangeLog.json";
        private const string ERRORLOG = "error.log";

        public static string ChangeLog
        {
            get
            {
                return Path.Combine(CurrentDirectory, CHANGELOG);
            }
        }

        public static string CurrentDirectory
        {
            get
            {
                return AppContext.BaseDirectory;
            }
        }

        private static string _assemblyName { get; set; } = null;

        public static string AssemblyName
        {
            get
            {
                if (_assemblyName == null)
                {
                    _assemblyName = Assembly.GetExecutingAssembly().GetName().Name;
                }

                return _assemblyName;
            }
        }

        public static string ExePath
        {
            get
            {
                return Environment.ProcessPath ?? Path.Combine(CurrentDirectory, AssemblyName + ".exe");
            }
        }

        private static string _settingsFile { get; set; } = null;

        public static string SettingsFile
        {
            get
            {
                if (_settingsFile == null)
                {
                    _settingsFile = Path.Combine(LocalApp, SETTINGS);
                }

                return _settingsFile;
            }
        }

        public static string ErrorLogFile
        {
            get
            {
                return Path.Combine(LocalApp, ERRORLOG);
            }
        }

        private static string _localApp { get; set; } = null;

        public static string LocalApp
        {
            get
            {
                if (_localApp == null)
                {
                    _localApp = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AssemblyName);

                    // migrate settings saved before the app was renamed from SidebarDiagnostics
                    if (!File.Exists(Path.Combine(_localApp, SETTINGS)))
                    {
                        string _oldSettings = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SidebarDiagnostics", SETTINGS);

                        if (File.Exists(_oldSettings))
                        {
                            try
                            {
                                Directory.CreateDirectory(_localApp);
                                File.Copy(_oldSettings, Path.Combine(_localApp, SETTINGS));
                            }
                            catch { }
                        }
                    }
                }

                return _localApp;
            }
        }
    }

    public static class ErrorLog
    {
        public static void Write(Exception ex)
        {
            Write(ex.ToString());
        }

        // plain-text trace line, for pinning down silent/no-op code paths that
        // aren't exceptions (e.g. a prompt that should have appeared but didn't)
        public static void Write(string message)
        {
            try
            {
                if (!Directory.Exists(Paths.LocalApp))
                {
                    Directory.CreateDirectory(Paths.LocalApp);
                }

                File.AppendAllText(Paths.ErrorLogFile, string.Format("[{0:u}] {1}{2}{2}", DateTime.Now, message, Environment.NewLine));
            }
            catch
            {
                // never let logging kill the app
            }
        }
    }

    // LibreHardwareMonitor 0.9.6+ reads CPU/GPU sensors through the PawnIO driver
    // (HVCI-compatible, unlike the old WinRing0). Without it, clocks/temps/voltages
    // read as "No Value". The app runs elevated, so the setup can run silently.
    public static class PawnIO
    {
        private const string SETUPURL = "https://github.com/namazso/PawnIO.Setup/releases/latest/download/PawnIO_setup.exe";

        public static bool IsInstalled
        {
            get
            {
                string _lib = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PawnIO", "PawnIOLib.dll");

                return File.Exists(_lib);
            }
        }

        public static bool Install()
        {
            return InstallAsync().GetAwaiter().GetResult();
        }

        public static async System.Threading.Tasks.Task<bool> InstallAsync()
        {
            // unique name per attempt: a stale/locked leftover from a prior run (still
            // being scanned by AV, or its installer process still exiting) must not
            // block a fresh download from writing to the same path
            string _setup = Path.Combine(Path.GetTempPath(), $"PawnIO_setup_{Guid.NewGuid():N}.exe");

            try
            {
                using (HttpClient _client = new HttpClient() { Timeout = TimeSpan.FromMinutes(2) })
                {
                    byte[] _bytes = await _client.GetByteArrayAsync(SETUPURL).ConfigureAwait(false);
                    await File.WriteAllBytesAsync(_setup, _bytes).ConfigureAwait(false);
                }

                if (!Authenticode.Verify(_setup))
                {
                    return false;
                }

                // defense in depth: pin the expected publisher on top of chain validation
                string _subject = new X509Certificate2(X509Certificate.CreateFromSignedFile(_setup)).Subject;

                if (_subject.IndexOf("namazso", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    return false;
                }

                // PawnIOSetup.exe has its own CLI (not NSIS): -install -silent, not /S
                using (Process _process = Process.Start(new ProcessStartInfo(_setup, "-install -silent") { UseShellExecute = false }))
                using (var _cts = new System.Threading.CancellationTokenSource(TimeSpan.FromMinutes(2)))
                {
                    try
                    {
                        await _process.WaitForExitAsync(_cts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        // installer hung; don't block the app forever waiting on it
                    }
                }

                return IsInstalled;
            }
            catch (Exception e)
            {
                ErrorLog.Write(e);
                return false;
            }
            finally
            {
                try
                {
                    File.Delete(_setup);
                }
                catch { }
            }
        }
    }

    // full WinVerifyTrust check: signature integrity + chain to a trusted root
    internal static class Authenticode
    {
        private static readonly Guid WINTRUST_ACTION_GENERIC_VERIFY_V2 = new Guid("00aac56b-cd44-11d0-8cc2-00c04fc295ee");

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WINTRUST_FILE_INFO
        {
            public uint cbStruct;
            public string pcwszFilePath;
            public IntPtr hFile;
            public IntPtr pgKnownSubject;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WINTRUST_DATA
        {
            public uint cbStruct;
            public IntPtr pPolicyCallbackData;
            public IntPtr pSIPClientData;
            public uint dwUIChoice;
            public uint fdwRevocationChecks;
            public uint dwUnionChoice;
            public IntPtr pFile;
            public uint dwStateAction;
            public IntPtr hWVTStateData;
            public IntPtr pwszURLReference;
            public uint dwProvFlags;
            public uint dwUIContext;
            public IntPtr pSignatureSettings;
        }

        private const uint WTD_UI_NONE = 2;
        private const uint WTD_REVOKE_NONE = 0;
        private const uint WTD_CHOICE_FILE = 1;
        private const uint WTD_STATEACTION_VERIFY = 1;
        private const uint WTD_STATEACTION_CLOSE = 2;

        [DllImport("wintrust.dll", CharSet = CharSet.Unicode)]
        private static extern int WinVerifyTrust(IntPtr hwnd, [MarshalAs(UnmanagedType.LPStruct)] Guid pgActionID, ref WINTRUST_DATA pWVTData);

        public static bool Verify(string filePath)
        {
            WINTRUST_FILE_INFO _fileInfo = new WINTRUST_FILE_INFO()
            {
                cbStruct = (uint)Marshal.SizeOf<WINTRUST_FILE_INFO>(),
                pcwszFilePath = filePath
            };

            IntPtr _pFile = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_FILE_INFO>());

            try
            {
                Marshal.StructureToPtr(_fileInfo, _pFile, false);

                WINTRUST_DATA _data = new WINTRUST_DATA()
                {
                    cbStruct = (uint)Marshal.SizeOf<WINTRUST_DATA>(),
                    dwUIChoice = WTD_UI_NONE,
                    fdwRevocationChecks = WTD_REVOKE_NONE,
                    dwUnionChoice = WTD_CHOICE_FILE,
                    pFile = _pFile,
                    dwStateAction = WTD_STATEACTION_VERIFY
                };

                int _result = WinVerifyTrust(IntPtr.Zero, WINTRUST_ACTION_GENERIC_VERIFY_V2, ref _data);

                _data.dwStateAction = WTD_STATEACTION_CLOSE;
                WinVerifyTrust(IntPtr.Zero, WINTRUST_ACTION_GENERIC_VERIFY_V2, ref _data);

                return _result == 0;
            }
            finally
            {
                Marshal.FreeHGlobal(_pFile);
            }
        }
    }

    public class UpdateInfo
    {
        public string Version { get; set; }

        public string URL { get; set; }
    }

    public static class UpdateCheck
    {
        private const string API = "https://api.github.com/repos/konguspng/sidemon/releases/latest";

        public const string DOWNLOADPAGE = "https://github.com/konguspng/sidemon/releases/latest";

        public static async System.Threading.Tasks.Task<UpdateInfo> CheckAsync()
        {
            try
            {
                using (HttpClient _client = new HttpClient())
                {
                    _client.Timeout = TimeSpan.FromSeconds(15);
                    _client.DefaultRequestHeaders.UserAgent.ParseAdd("SideMon");

                    string _json = await _client.GetStringAsync(API).ConfigureAwait(false);

                    using (System.Text.Json.JsonDocument _doc = System.Text.Json.JsonDocument.Parse(_json))
                    {
                        string _tag = _doc.RootElement.GetProperty("tag_name").GetString();

                        Version _latest;

                        if (!Version.TryParse(_tag.TrimStart('v', 'V'), out _latest))
                        {
                            return null;
                        }

                        Version _current = Assembly.GetExecutingAssembly().GetName().Version;

                        if (new Version(_latest.Major, _latest.Minor, Math.Max(_latest.Build, 0)) > new Version(_current.Major, _current.Minor, Math.Max(_current.Build, 0)))
                        {
                            System.Text.Json.JsonElement _url;

                            return new UpdateInfo()
                            {
                                Version = _latest.ToString(),
                                URL = _doc.RootElement.TryGetProperty("html_url", out _url) ? _url.GetString() : DOWNLOADPAGE
                            };
                        }
                    }
                }
            }
            catch { } // no network or rate-limited; check again next launch

            return null;
        }
    }

    // Run-at-startup uses a Task Scheduler logon task (via schtasks.exe) because the app
    // runs elevated; a plain HKCU Run entry cannot launch an elevated process silently.
    public static class Startup
    {
        public static bool StartupTaskExists()
        {
            string _xml = RunSchTasks("/query /xml /tn \"" + Constants.Generic.TASKNAME + "\"");

            if (_xml == null)
            {
                return false;
            }

            return _xml.IndexOf(Paths.ExePath, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static bool EnableStartupTask(string exePath = null)
        {
            try
            {
                string _exe = exePath ?? Paths.ExePath;

                string _xmlFile = Path.Combine(Path.GetTempPath(), "SidebarDiagnosticsTask.xml");

                File.WriteAllText(_xmlFile, string.Format(TASKXML, System.Security.SecurityElement.Escape(_exe)));

                // /RL is invalid together with /XML (schtasks rejects the combination
                // outright); the XML's own Principal/RunLevel already sets HighestAvailable
                bool _ok = RunSchTasks("/create /f /tn \"" + Constants.Generic.TASKNAME + "\" /xml \"" + _xmlFile + "\"") != null;

                File.Delete(_xmlFile);

                return _ok;
            }
            catch (Exception e)
            {
                ErrorLog.Write(e);
                return false;
            }
        }

        public static void DisableStartupTask()
        {
            RunSchTasks("/delete /f /tn \"" + Constants.Generic.TASKNAME + "\"");
        }

        private static string RunSchTasks(string args)
        {
            try
            {
                using (Process _process = Process.Start(new ProcessStartInfo("schtasks.exe", args)
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }))
                {
                    string _output = _process.StandardOutput.ReadToEnd();
                    string _error = _process.StandardError.ReadToEnd();
                    _process.WaitForExit(10000);

                    if (_process.ExitCode == 0)
                    {
                        return _output;
                    }

                    // surface the real reason instead of failing silently
                    ErrorLog.Write(new Exception(string.Format("schtasks {0} failed (exit {1}): {2}", args, _process.ExitCode, _error)));

                    return null;
                }
            }
            catch (Exception e)
            {
                ErrorLog.Write(e);
                return null;
            }
        }

        private const string TASKXML = @"<?xml version=""1.0"" encoding=""UTF-16""?>
<Task version=""1.2"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <Triggers>
    <LogonTrigger>
      <Enabled>true</Enabled>
    </LogonTrigger>
  </Triggers>
  <Principals>
    <Principal id=""Author"">
      <RunLevel>HighestAvailable</RunLevel>
    </Principal>
  </Principals>
  <Settings>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
    <AllowHardTerminate>false</AllowHardTerminate>
    <StartWhenAvailable>true</StartWhenAvailable>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
  </Settings>
  <Actions Context=""Author"">
    <Exec>
      <Command>{0}</Command>
    </Exec>
  </Actions>
</Task>";
    }

    public static class Culture
    {
        public const string DEFAULT = "Default";

        public static void SetDefault()
        {
            Default = Thread.CurrentThread.CurrentUICulture;
        }

        public static void SetCurrent(bool init)
        {
            Resources.Culture = CultureInfo;

            Thread.CurrentThread.CurrentCulture = CultureInfo;
            Thread.CurrentThread.CurrentUICulture = CultureInfo;

            if (init)
            {
                FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement), new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.Name)));
            }
        }

        public static CultureItem[] GetAll()
        {
            return new CultureItem[1] { new CultureItem() { Value = DEFAULT, Text = Resources.SettingsLanguageDefault } }.Concat(CultureInfo.GetCultures(CultureTypes.SpecificCultures).Where(c => Languages.Contains(c.TwoLetterISOLanguageName)).OrderBy(c => c.DisplayName).Select(c => new CultureItem() { Value = c.Name, Text = c.DisplayName })).ToArray();
        }

        public static string[] Languages
        {
            get
            {
                return new string[11] { "en", "da", "de", "fr", "ja", "nl", "zh", "it", "ru", "fi", "es" };
            }
        }

        public static CultureInfo Default { get; private set; }

        private static string _cachedName { get; set; }

        private static CultureInfo _cached { get; set; }

        public static CultureInfo CultureInfo
        {
            get
            {
                string culture = Framework.Settings.Instance.Culture;

                if (!string.Equals(culture, _cachedName, StringComparison.Ordinal))
                {
                    _cachedName = culture;
                    _cached = string.Equals(culture, DEFAULT, StringComparison.Ordinal)
                        ? Default
                        : new CultureInfo(culture);
                }

                return _cached;
            }
        }
    }

    public class CultureItem
    {
        public string Value { get; set; }

        public string Text { get; set; }
    }
}
