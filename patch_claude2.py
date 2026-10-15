import re

with open('SidebarDiagnostics/UsageProviders.cs', 'r', encoding='utf-8') as f:
    content = f.read()

# find the expiry check in ClaudeProvider
find_str = '''
                        if (oauth.TryGetProperty("expiresAt", out var exp) && exp.ValueKind == JsonValueKind.Number && exp.TryGetInt64(out long expMs)
                            && DateTimeOffset.FromUnixTimeMilliseconds(expMs) <= DateTimeOffset.UtcNow.AddSeconds(30))
                        {
                            var stale = UsageResult.Fail(UsageStatus.AuthExpired);
                            stale.RetryIn = TimeSpan.FromSeconds(30);
                            return stale;
                        }'''

replace_str = '''
                        if (oauth.TryGetProperty("expiresAt", out var exp) && exp.ValueKind == JsonValueKind.Number && exp.TryGetInt64(out long expMs)
                            && DateTimeOffset.FromUnixTimeMilliseconds(expMs) <= DateTimeOffset.UtcNow.AddSeconds(30))
                        {
                            if (expMs != LastSeenExpiredMs) { LastSeenExpiredMs = expMs; AutoRenewAttempts = 0; }
                            var stale = UsageResult.Fail(UsageStatus.AuthExpired);
                            stale.RetryIn = TimeSpan.FromSeconds(30);
                            return stale;
                        }'''

content = content.replace(find_str, replace_str)

with open('SidebarDiagnostics/UsageProviders.cs', 'w', encoding='utf-8') as f:
    f.write(content)
