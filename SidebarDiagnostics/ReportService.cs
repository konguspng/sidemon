using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using SidebarDiagnostics.Framework;

namespace SidebarDiagnostics
{
    public class ReportResult
    {
        public bool Success { get; set; }
        public string Id { get; set; }
        public string FriendlyMessage { get; set; }
    }

    public static class ReportService
    {
        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        
        // This is for testing. Overridden during local fake server tests.
        internal static string ReportUrl { get; set; } = "https://pishonstudio.com/sidemon/report.php";

        private static string GetSecret()
        {
#if HAS_REPORT_SECRET
            return ReportSecret.Value;
#else
            return "";
#endif
        }

        public static string Scrub(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            string scrubbed = text;

            // 1. Windows user profile path
            scrubbed = Regex.Replace(scrubbed, @"C:\\Users\\[^\\]+", @"C:\Users\<user>", RegexOptions.IgnoreCase);
            scrubbed = Regex.Replace(scrubbed, @"C:/Users/[^/]+", @"C:/Users/<user>", RegexOptions.IgnoreCase);

            // 2. Environment.UserName and Environment.MachineName
            string userName = Environment.UserName;
            if (!string.IsNullOrEmpty(userName))
            {
                scrubbed = Regex.Replace(scrubbed, Regex.Escape(userName), "<user>", RegexOptions.IgnoreCase);
            }
            
            string machineName = Environment.MachineName;
            if (!string.IsNullOrEmpty(machineName))
            {
                scrubbed = Regex.Replace(scrubbed, Regex.Escape(machineName), "<machine>", RegexOptions.IgnoreCase);
            }

            // 3. Custom paths
            foreach (var p in SidebarDiagnostics.Monitoring.UsageRegistry.Providers)
            {
                if (!string.IsNullOrWhiteSpace(p.CustomPath))
                {
                    scrubbed = scrubbed.Replace(p.CustomPath, "<custom-path>");
                }
            }

            // 4. Tokens
            scrubbed = Regex.Replace(scrubbed, @"(github_pat_|ghp_|gho_|sk-|sk-ant-)[a-zA-Z0-9_\-]+", "<token>");
            scrubbed = Regex.Replace(scrubbed, @"\bBearer\s+[a-zA-Z0-9_\-\.]+", "Bearer <token>");
            scrubbed = Regex.Replace(scrubbed, @"eyJ[a-zA-Z0-9_\-]+\.[a-zA-Z0-9_\-]+\.[a-zA-Z0-9_\-]+", "<jwt>");

            // 5. Hex/Base64 blobs >= 32 chars next to token/key/secret/password/authorization
            scrubbed = Regex.Replace(scrubbed, @"(?i)(token|key|secret|password|authorization)[^\w]{0,10}[a-zA-Z0-9+/=_-]{32,}", "$1 <token>");

            // 6. Emails
            scrubbed = Regex.Replace(scrubbed, @"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}", "<email>");

            // 7. IPv4
            scrubbed = Regex.Replace(scrubbed, @"\b(?:\d{1,3}\.){3}\d{1,3}\b", "<ip>");

            return scrubbed;
        }

        public static string BuildReport()
        {
            string version = Assembly.GetExecutingAssembly().GetName().Version.ToString();
            string os = Environment.OSVersion.VersionString;
            string dotNetVersion = Environment.Version.ToString();

            string report = $"Version: {version}\r\nOS: {os}\r\n.NET: {dotNetVersion}\r\n\r\n";

            string logTail = "";
            try
            {
                if (File.Exists(Utilities.Paths.ErrorLogFile))
                {
                    using (FileStream fs = new FileStream(Utilities.Paths.ErrorLogFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        long pos = Math.Max(0, fs.Length - 40000);
                        if (pos > 0)
                        {
                            fs.Position = pos;
                            using (StreamReader sr = new StreamReader(fs))
                            {
                                // read one line and discard to ensure we start at a line boundary
                                sr.ReadLine();
                                logTail = sr.ReadToEnd();
                            }
                        }
                        else
                        {
                            using (StreamReader sr = new StreamReader(fs))
                            {
                                logTail = sr.ReadToEnd();
                            }
                        }
                    }
                }
            }
            catch (Exception) { }

            if (string.IsNullOrWhiteSpace(logTail))
            {
                report += "Error log is missing or empty.";
            }
            else
            {
                report += Scrub(logTail);
            }

            // Ensure we never exceed 60,000 characters
            if (report.Length > 60000)
            {
                report = report.Substring(report.Length - 60000);
            }

            return report;
        }

        public static async Task<ReportResult> Send(string report)
        {
            try
            {
                var payload = new
                {
                    secret = GetSecret(),
                    version = Assembly.GetExecutingAssembly().GetName().Version.ToString(),
                    os = Environment.OSVersion.VersionString,
                    log = report
                };

                var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                
                using (var request = new HttpRequestMessage(HttpMethod.Post, ReportUrl))
                {
                    request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
                    request.Content = content;

                    using (var response = await _httpClient.SendAsync(request).ConfigureAwait(false))
                    {
                        string responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        
                        if (response.IsSuccessStatusCode)
                        {
                            try
                            {
                                using (JsonDocument doc = JsonDocument.Parse(responseBody))
                                {
                                    if (doc.RootElement.TryGetProperty("ok", out JsonElement okElement) && okElement.GetBoolean() &&
                                        doc.RootElement.TryGetProperty("id", out JsonElement idElement))
                                    {
                                        return new ReportResult { Success = true, Id = idElement.GetString(), FriendlyMessage = "Report sent. Thank you." };
                                    }
                                }
                            }
                            catch { }
                            return new ReportResult { Success = false, FriendlyMessage = "The report could not be delivered (code 200 invalid response)." };
                        }
                        
                        if ((int)response.StatusCode == 429)
                        {
                            return new ReportResult { Success = false, FriendlyMessage = "You already sent several reports recently; please try again later." };
                        }
                        
                        return new ReportResult { Success = false, FriendlyMessage = $"The report could not be delivered (code {(int)response.StatusCode})." };
                    }
                }
            }
            catch (Exception)
            {
                return new ReportResult { Success = false, FriendlyMessage = "Could not reach the report server." };
            }
        }
    }
}
