import re

file_path = "SidebarDiagnostics/Properties/Resources.resx"
with open(file_path, "r", encoding="utf-8") as f:
    content = f.read()

new_resx = """  <data name="UsageClaudeExpiredStaleNote" xml:space="preserve">
    <value>Login expired - last updated {0} ago. Open Claude Code to renew.</value>
  </data>
  <data name="SettingsUsageClaudeRenewButton" xml:space="preserve">
    <value>Renew</value>
  </data>
  <data name="SettingsUsageClaudeRenewTooltip" xml:space="preserve">
    <value>Renew login</value>
  </data>
  <data name="SettingsUsageClaudeRenewed" xml:space="preserve">
    <value>Renewed</value>
  </data>
  <data name="SettingsUsageClaudeStillExpired" xml:space="preserve">
    <value>Still expired - run claude once, or use a minimal prompt</value>
  </data>
  <data name="SettingsUsageClaudeAutoRenew" xml:space="preserve">
    <value>Renew Claude login automatically when it expires</value>
  </data>
  <data name="HardwareLimitedWarning" xml:space="preserve">
    <value>Some hardware sensors unavailable - run SideMon as administrator</value>
  </data>
  <data name="SettingsUsageClaudeRenewConfirm" xml:space="preserve">
    <value>This will send one very small request and uses a tiny amount of the Claude usage. Continue?</value>
  </data>
</root>"""

content = content.replace("</root>", new_resx)

with open(file_path, "w", encoding="utf-8") as f:
    f.write(content)

file_path = "SidebarDiagnostics/Properties/Resources.Designer.cs"
with open(file_path, "r", encoding="utf-8") as f:
    content = f.read()

new_props = """
        public static string UsageClaudeExpiredStaleNote {
            get { return ResourceManager.GetString("UsageClaudeExpiredStaleNote", resourceCulture); }
        }
        public static string SettingsUsageClaudeRenewButton {
            get { return ResourceManager.GetString("SettingsUsageClaudeRenewButton", resourceCulture); }
        }
        public static string SettingsUsageClaudeRenewTooltip {
            get { return ResourceManager.GetString("SettingsUsageClaudeRenewTooltip", resourceCulture); }
        }
        public static string SettingsUsageClaudeRenewed {
            get { return ResourceManager.GetString("SettingsUsageClaudeRenewed", resourceCulture); }
        }
        public static string SettingsUsageClaudeStillExpired {
            get { return ResourceManager.GetString("SettingsUsageClaudeStillExpired", resourceCulture); }
        }
        public static string SettingsUsageClaudeAutoRenew {
            get { return ResourceManager.GetString("SettingsUsageClaudeAutoRenew", resourceCulture); }
        }
        public static string HardwareLimitedWarning {
            get { return ResourceManager.GetString("HardwareLimitedWarning", resourceCulture); }
        }
        public static string SettingsUsageClaudeRenewConfirm {
            get { return ResourceManager.GetString("SettingsUsageClaudeRenewConfirm", resourceCulture); }
        }
    }
}"""

content = re.sub(r'    }\s*}\s*$', new_props, content)

with open(file_path, "w", encoding="utf-8") as f:
    f.write(content)
