using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SidebarDiagnostics.Utilities;

namespace SidebarDiagnostics.Monitoring
{
    // Persists last-good AI usage readings (numbers, reset times, plan, timestamp, and a
    // short hash of the credential fingerprint) so the sidebar shows dimmed last-known
    // values immediately after restart while the first fresh poll runs. Never stores
    // account names, emails, tokens, or any credential content.
    internal static class UsageCache
    {
        public static readonly TimeSpan MaxUnsignedAge = TimeSpan.FromHours(12);
        public const string PastResetNote = "resets now - refreshing";

        private static readonly object _lock = new object();
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private static bool _loaded;
        private static Dictionary<string, CachedProvider> _entries;
        private static bool _readErrorLogged;
        private static bool _writeErrorLogged;

        internal static Func<string> PathProvider { get; set; } = () => Paths.UsageCacheFile;
        internal static Action<string> LogWriter { get; set; } = ErrorLog.Write;

        internal sealed class CacheDocument
        {
            public int Version { get; set; } = 1;
            public Dictionary<string, CachedProvider> Providers { get; set; } = new Dictionary<string, CachedProvider>(StringComparer.Ordinal);
        }

        internal sealed class CachedProvider
        {
            public List<CachedWindow> Windows { get; set; } = new List<CachedWindow>();
            public string Plan { get; set; }
            public DateTimeOffset LastGoodAt { get; set; }
            public string SignatureHash { get; set; }
        }

        internal sealed class CachedWindow
        {
            public UsageKind Kind { get; set; }
            public string Slot { get; set; }
            public double? UsedPercent { get; set; }
            public DateTimeOffset? ResetsAt { get; set; }
            public string Note { get; set; }
        }

        public static string HashSignature(string signature)
        {
            if (string.IsNullOrEmpty(signature))
            {
                return null;
            }

            byte[] bytes = Encoding.UTF8.GetBytes(signature);
            byte[] hash = SHA256.HashData(bytes);
            // first 16 hex chars (8 bytes)
            return Convert.ToHexString(hash, 0, 8);
        }

        internal static void ResetForTests(Func<string> pathProvider = null, Action<string> logWriter = null)
        {
            lock (_lock)
            {
                _loaded = false;
                _entries = null;
                _readErrorLogged = false;
                _writeErrorLogged = false;
                PathProvider = pathProvider ?? (() => Paths.UsageCacheFile);
                LogWriter = logWriter ?? ErrorLog.Write;
            }
        }

        // Caller must hold _lock.
        private static void EnsureLoadedLocked()
        {
            if (_loaded)
            {
                return;
            }

            _loaded = true;
            _entries = new Dictionary<string, CachedProvider>(StringComparer.Ordinal);

            try
            {
                string path = PathProvider?.Invoke();
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    return;
                }

                string json = File.ReadAllText(path, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return;
                }

                CacheDocument doc = JsonSerializer.Deserialize<CacheDocument>(json, JsonOptions);
                if (doc?.Providers != null)
                {
                    foreach (var kvp in doc.Providers)
                    {
                        if (!string.IsNullOrEmpty(kvp.Key) && kvp.Value != null)
                        {
                            _entries[kvp.Key] = kvp.Value;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _entries = new Dictionary<string, CachedProvider>(StringComparer.Ordinal);
                LogReadErrorOnceLocked(ex);
            }
        }

        private static void LogReadErrorOnceLocked(Exception ex)
        {
            if (_readErrorLogged)
            {
                return;
            }

            _readErrorLogged = true;
            try { LogWriter?.Invoke("UsageCache read failed: " + ex.GetType().Name); }
            catch { }
        }

        private static void LogWriteErrorOnceLocked(Exception ex)
        {
            if (_writeErrorLogged)
            {
                return;
            }

            _writeErrorLogged = true;
            try { LogWriter?.Invoke("UsageCache write failed: " + ex.GetType().Name); }
            catch { }
        }

        public static UsageSnapshot TrySeed(IUsageProvider provider, DateTimeOffset nowUtc, out string currentSignature)
        {
            currentSignature = null;
            if (provider == null || string.IsNullOrEmpty(provider.Id))
            {
                return null;
            }

            bool hasWatch = provider is ICredentialWatch;
            if (hasWatch)
            {
                try { currentSignature = ((ICredentialWatch)provider).CredentialSignature(); }
                catch { currentSignature = null; }
            }

            CachedProvider entry;
            lock (_lock)
            {
                EnsureLoadedLocked();
                if (!_entries.TryGetValue(provider.Id, out entry) || entry == null)
                {
                    return null;
                }
            }

            if (entry.Windows == null || entry.Windows.Count == 0 || entry.LastGoodAt == default)
            {
                return null;
            }

            if (hasWatch)
            {
                string currentHash = HashSignature(currentSignature);
                if (string.IsNullOrEmpty(entry.SignatureHash)
                    || string.IsNullOrEmpty(currentHash)
                    || !string.Equals(entry.SignatureHash, currentHash, StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }
            }
            else
            {
                // Providers without ICredentialWatch: seed only when no signature is stored
                // and the cached reading is younger than 12 hours.
                if (!string.IsNullOrEmpty(entry.SignatureHash))
                {
                    return null;
                }

                TimeSpan age = nowUtc - entry.LastGoodAt;
                if (age >= MaxUnsignedAge)
                {
                    return null;
                }
            }

            List<UsageWindow> windows = new List<UsageWindow>(entry.Windows.Count);
            foreach (CachedWindow cw in entry.Windows)
            {
                if (cw == null)
                {
                    continue;
                }

                bool pastReset = cw.ResetsAt.HasValue && cw.ResetsAt.Value <= nowUtc;
                windows.Add(new UsageWindow
                {
                    Kind = cw.Kind,
                    Slot = cw.Slot,
                    UsedPercent = pastReset ? (double?)null : cw.UsedPercent,
                    ResetsAt = cw.ResetsAt,
                    Note = pastReset ? PastResetNote : cw.Note
                });
            }

            if (windows.Count == 0)
            {
                return null;
            }

            return new UsageSnapshot
            {
                HasAttempt = true,
                Status = UsageStatus.Cached,
                Windows = windows,
                Plan = entry.Plan,
                Account = null,
                LastGoodAt = entry.LastGoodAt
            };
        }

        public static void Save(string providerId, UsageSnapshot snapshot, string signatureHash)
        {
            if (string.IsNullOrEmpty(providerId)
                || snapshot == null
                || snapshot.Status != UsageStatus.Ok
                || snapshot.Windows == null
                || snapshot.Windows.Count == 0
                || !snapshot.LastGoodAt.HasValue)
            {
                return;
            }

            List<CachedWindow> cachedWindows = new List<CachedWindow>(snapshot.Windows.Count);
            foreach (UsageWindow w in snapshot.Windows)
            {
                if (w == null)
                {
                    continue;
                }

                cachedWindows.Add(new CachedWindow
                {
                    Kind = w.Kind,
                    Slot = w.Slot,
                    UsedPercent = w.UsedPercent,
                    ResetsAt = w.ResetsAt,
                    Note = w.Note
                });
            }

            if (cachedWindows.Count == 0)
            {
                return;
            }

            CachedProvider entry = new CachedProvider
            {
                Windows = cachedWindows,
                Plan = snapshot.Plan,
                LastGoodAt = snapshot.LastGoodAt.Value,
                SignatureHash = signatureHash
            };

            string tempPath = null;
            lock (_lock)
            {
                try
                {
                    EnsureLoadedLocked();
                    _entries[providerId] = entry;

                    string path = PathProvider?.Invoke();
                    if (string.IsNullOrEmpty(path))
                    {
                        return;
                    }

                    string dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }

                    CacheDocument doc = new CacheDocument
                    {
                        Version = 1,
                        Providers = new Dictionary<string, CachedProvider>(_entries, StringComparer.Ordinal)
                    };

                    string json = JsonSerializer.Serialize(doc, JsonOptions);
                    tempPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    File.WriteAllText(tempPath, json, new UTF8Encoding(false));
                    File.Move(tempPath, path, true);
                    tempPath = null;
                }
                catch (Exception ex)
                {
                    LogWriteErrorOnceLocked(ex);
                }
                finally
                {
                    if (tempPath != null)
                    {
                        try { if (File.Exists(tempPath)) File.Delete(tempPath); }
                        catch { }
                    }
                }
            }
        }
    }
}
