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
                    _localApp = Environment.GetEnvironmentVariable("SIDEMON_TEST_DIR") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AssemblyName);

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
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.AppendLine(ex.ToString());
            Exception inner = ex.InnerException;
            while(inner != null)
            {
                sb.AppendLine("Inner Exception:");
                sb.AppendLine(inner.ToString());
                inner = inner.InnerException;
            }
            Write(sb.ToString().TrimEnd());
        }

        private static bool _startupLogged = false;

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

                string file = Paths.ErrorLogFile;
                if (File.Exists(file))
                {
                    var fileInfo = new FileInfo(file);
                    if (fileInfo.Length > 1024 * 1024) // 1MB
                    {
                        var content = File.ReadAllText(file);
                        File.WriteAllText(file, content.Substring(content.Length / 2));
                    }
                }

                if (!_startupLogged)
                {
                    _startupLogged = true;
                    try
                    {
                        int monitorCount = 0;
                        try { monitorCount = SidebarDiagnostics.Windows.Monitor.GetMonitors().Count(); } catch {}
                        string diag = $"[Diagnostic] OS={Environment.OSVersion} .NET={Environment.Version} Culture={System.Globalization.CultureInfo.CurrentCulture.Name} Monitors={monitorCount}";
                        File.AppendAllText(file, string.Format(CultureInfo.InvariantCulture, "[{0:yyyy-MM-dd HH:mm:ss}] {1}{2}", DateTime.Now, diag, Environment.NewLine));
                    }
                    catch { }
                }

                File.AppendAllText(file, string.Format(CultureInfo.InvariantCulture, "[{0:yyyy-MM-dd HH:mm:ss}] {1}{2}{2}", DateTime.Now, message, Environment.NewLine));
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

        // installer asset of the release (null when the API did not list one, e.g. rate-limit fallback)
        public string AssetUrl { get; set; }

        public long AssetSize { get; set; }

        public string AssetSha256 { get; set; }
    }

    // Downloads the release installer and starts it, so a click on the update notification
    // ends in the setup running without the user having to find and open the file.
    public static class UpdateInstaller
    {
        private const string ALLOWEDPREFIX = "https://github.com/konguspng/sidemon/releases/download/";

        private static readonly HttpClient _download = CreateClient();

        private static HttpClient CreateClient()
        {
            // default handler follows the github.com -> objects.githubusercontent.com redirect
            HttpClient c = new HttpClient() { Timeout = TimeSpan.FromMinutes(10) };
            c.DefaultRequestHeaders.UserAgent.ParseAdd("SideMon/" + Assembly.GetExecutingAssembly().GetName().Version.ToString(3));
            return c;
        }

        public static bool CanInstall(UpdateInfo update)
        {
            return update != null
                && !string.IsNullOrEmpty(update.AssetUrl)
                && update.AssetUrl.StartsWith(ALLOWEDPREFIX, StringComparison.Ordinal)
                && update.AssetSize > 0
                && !string.IsNullOrEmpty(update.AssetSha256);
        }

        // Returns the path of the verified installer, or throws. progress receives 0..100.
        public static async System.Threading.Tasks.Task<string> DownloadAsync(UpdateInfo update, IProgress<int> progress)
        {
            if (!CanInstall(update))
            {
                throw new InvalidOperationException("No verifiable installer asset");
            }

            string dir = Path.Combine(Path.GetTempPath(), "SideMonUpdate");
            Directory.CreateDirectory(dir);

            string name = Path.GetFileName(new Uri(update.AssetUrl).LocalPath);
            if (string.IsNullOrEmpty(name) || !name.EndsWith("-Setup.exe", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Unexpected asset name");
            }

            string finalPath = Path.Combine(dir, name);
            string partPath = finalPath + ".part";

            try
            {
                using (var response = await _download.GetAsync(update.AssetUrl, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();

                    using (var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (var output = new FileStream(partPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        byte[] buffer = new byte[81920];
                        long total = 0;
                        int last = -1;
                        int read;

                        while ((read = await input.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0)
                        {
                            await output.WriteAsync(buffer, 0, read).ConfigureAwait(false);
                            total += read;

                            int pct = (int)(total * 100 / update.AssetSize);
                            if (pct != last && progress != null)
                            {
                                last = pct;
                                progress.Report(Math.Min(pct, 100));
                            }
                        }
                    }
                }

                FileInfo info = new FileInfo(partPath);
                if (info.Length != update.AssetSize)
                {
                    throw new InvalidDataException("Size mismatch");
                }

                string hash;
                using (var sha = System.Security.Cryptography.SHA256.Create())
                using (var fs = File.OpenRead(partPath))
                {
                    hash = BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "");
                }

                if (!string.Equals(hash, update.AssetSha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("Checksum mismatch");
                }

                if (File.Exists(finalPath))
                {
                    File.Delete(finalPath);
                }

                File.Move(partPath, finalPath);

                return finalPath;
            }
            catch
            {
                try { if (File.Exists(partPath)) File.Delete(partPath); } catch { }
                throw;
            }
        }

        // Starts the setup in progress-only silent mode (no wizard pages, no prompts) and tells it
        // to relaunch SideMon when done (the normal "Launch SideMon" checkbox is skipped when silent).
        public static void Launch(string installerPath)
        {
            ProcessStartInfo psi = new ProcessStartInfo()
            {
                FileName = installerPath,
                Arguments = "/SILENT /SUPPRESSMSGBOXES /NORESTART /SP- /AUTOUPDATE=1",
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(installerPath)
            };

            Process.Start(psi);
        }
    }

    public static class UpdateCheck
    {
        private const string API = "https://api.github.com/repos/konguspng/sidemon/releases/latest";
        public const string DOWNLOADPAGE = "https://github.com/konguspng/sidemon/releases/latest";
        
        public static bool LastFailed = false;
        private static DateTime _lastErrorLog = DateTime.MinValue;

        private static readonly HttpClient _client;
        
        static UpdateCheck()
        {
            var handler = new HttpClientHandler { AllowAutoRedirect = false };
            _client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
            Version v = Assembly.GetExecutingAssembly().GetName().Version;
            _client.DefaultRequestHeaders.UserAgent.ParseAdd("SideMon/" + v.ToString(3));
        }

        public static string ParseTagFromLocation(string location)
        {
            if (string.IsNullOrEmpty(location)) return null;
            int idx = location.LastIndexOf("/tag/");
            if (idx >= 0)
            {
                return location.Substring(idx + 5);
            }
            return null;
        }

        public static bool IsNewer(string tag, Version current)
        {
            if (string.IsNullOrEmpty(tag)) return false;
            if (!Version.TryParse(tag.TrimStart('v', 'V'), out Version latest)) return false;
            return new Version(latest.Major, latest.Minor, Math.Max(latest.Build, 0)) > new Version(current.Major, current.Minor, Math.Max(current.Build, 0));
        }

        public static async System.Threading.Tasks.Task<UpdateInfo> CheckAsync()
        {
            LastFailed = false;
            try
            {
                string tag = null;
                string url = DOWNLOADPAGE;
                string assetUrl = null;
                long assetSize = 0;
                string assetSha = null;

                using (var request = new HttpRequestMessage(HttpMethod.Get, API))
                using (var response = await _client.SendAsync(request).ConfigureAwait(false))
                {
                    if (response.IsSuccessStatusCode)
                    {
                        string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        using (var doc = System.Text.Json.JsonDocument.Parse(json))
                        {
                            tag = doc.RootElement.GetProperty("tag_name").GetString();
                            if (doc.RootElement.TryGetProperty("html_url", out var urlElement))
                            {
                                url = urlElement.GetString();
                            }

                            if (doc.RootElement.TryGetProperty("assets", out var assets) && assets.ValueKind == System.Text.Json.JsonValueKind.Array)
                            {
                                foreach (var asset in assets.EnumerateArray())
                                {
                                    string aname = asset.TryGetProperty("name", out var n) ? n.GetString() : null;
                                    if (aname == null || !aname.EndsWith("-Setup.exe", StringComparison.OrdinalIgnoreCase)) continue;

                                    assetUrl = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
                                    assetSize = asset.TryGetProperty("size", out var s) && s.ValueKind == System.Text.Json.JsonValueKind.Number ? s.GetInt64() : 0;
                                    string digest = asset.TryGetProperty("digest", out var d) && d.ValueKind == System.Text.Json.JsonValueKind.String ? d.GetString() : null;
                                    if (digest != null && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                                    {
                                        assetSha = digest.Substring(7);
                                    }
                                    break;
                                }
                            }
                        }
                    }
                    else if (response.StatusCode == System.Net.HttpStatusCode.Forbidden || response.StatusCode == (System.Net.HttpStatusCode)429)
                    {
                        using (var fallbackRequest = new HttpRequestMessage(HttpMethod.Get, DOWNLOADPAGE))
                        using (var fallbackResponse = await _client.SendAsync(fallbackRequest).ConfigureAwait(false))
                        {
                            if (fallbackResponse.StatusCode == System.Net.HttpStatusCode.Found || fallbackResponse.StatusCode == System.Net.HttpStatusCode.Redirect)
                            {
                                tag = ParseTagFromLocation(fallbackResponse.Headers.Location?.ToString());
                            }
                            else
                            {
                                throw new Exception("Fallback HTTP " + (int)fallbackResponse.StatusCode);
                            }
                        }
                    }
                    else
                    {
                        throw new Exception("API HTTP " + (int)response.StatusCode);
                    }
                }

                Version current = Assembly.GetExecutingAssembly().GetName().Version;
                if (IsNewer(tag, current))
                {
                    Version.TryParse(tag.TrimStart('v', 'V'), out Version parsedTag);
                    return new UpdateInfo()
                    {
                        Version = parsedTag.ToString(),
                        URL = url,
                        AssetUrl = assetUrl,
                        AssetSize = assetSize,
                        AssetSha256 = assetSha
                    };
                }
            }
            catch (Exception ex)
            {
                LastFailed = true;
                if (DateTime.Now.Subtract(_lastErrorLog).TotalDays >= 1)
                {
                    _lastErrorLog = DateTime.Now;
                    ErrorLog.Write(new Exception("Update check failed: " + ex.GetType().Name + " - " + ex.Message));
                }
            }

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

        public static bool DisableStartupTask()
        {
            // exit code 1 with "cannot find the file" just means it was already gone
            RunSchTasks("/delete /f /tn \"" + Constants.Generic.TASKNAME + "\"");

            return !StartupTaskExists();
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

                    // "task does not exist" is the normal answer of a /query or /delete when
                    // the startup task is simply not there: not an error worth logging
                    bool _missing = (args.StartsWith("/query", StringComparison.Ordinal) || args.StartsWith("/delete", StringComparison.Ordinal))
                        && _error != null && _error.IndexOf("cannot find", StringComparison.OrdinalIgnoreCase) >= 0;

                    if (_missing)
                    {
                        return null;
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

        private static bool _languageOverridden;

        public static void SetCurrent(bool init)
        {
            _cachedName = null;
            _cached = null;

            Resources.Culture = CultureInfo;

            Thread.CurrentThread.CurrentCulture = CultureInfo;
            Thread.CurrentThread.CurrentUICulture = CultureInfo;

            CultureInfo.DefaultThreadCurrentCulture = CultureInfo;
            CultureInfo.DefaultThreadCurrentUICulture = CultureInfo;

            if (init && !_languageOverridden)
            {
                _languageOverridden = true;
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
                return new string[] { "en", "da", "de", "fr", "ja", "nl", "zh", "it", "ru", "fi", "es", "ar", "tr" };
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

                if (!string.Equals(culture, _cachedName, StringComparison.Ordinal) || _cached == null)
                {
                    _cachedName = culture;
                    CultureInfo defaultCulture = Default ?? CultureInfo.InstalledUICulture ?? CultureInfo.CurrentUICulture;
                    _cached = string.Equals(culture, DEFAULT, StringComparison.Ordinal)
                        ? defaultCulture
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
