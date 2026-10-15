import re

file_path = "SidebarDiagnostics/Properties/Resources.resx"
with open(file_path, "r", encoding="utf-8") as f:
    content = f.read()

new_resx = \"\"\"  <data name="SettingsDisplayHeader" xml:space="preserve">
    <value>Display</value>
  </data>
  <data name="SettingsHardwareHeader" xml:space="preserve">
    <value>Hardware</value>
  </data>
  <data name="SettingsMetricsHeader" xml:space="preserve">
    <value>Metrics</value>
  </data>
  <data name="SettingsAlertsHeader" xml:space="preserve">
    <value>Alerts</value>
  </data>
  <data name="SettingsProvidersHeader" xml:space="preserve">
    <value>Providers</value>
  </data>
  <data name="SettingsNoExtraOptions" xml:space="preserve">
    <value>No extra options</value>
  </data>
</root>\"\"\"

content = content.replace("</root>", new_resx)

with open(file_path, "w", encoding="utf-8") as f:
    f.write(content)

file_path = "SidebarDiagnostics/Properties/Resources.Designer.cs"
with open(file_path, "r", encoding="utf-8") as f:
    content = f.read()

new_props = \"\"\"
        public static string SettingsDisplayHeader {
            get { return ResourceManager.GetString("SettingsDisplayHeader", resourceCulture); }
        }
        public static string SettingsHardwareHeader {
            get { return ResourceManager.GetString("SettingsHardwareHeader", resourceCulture); }
        }
        public static string SettingsMetricsHeader {
            get { return ResourceManager.GetString("SettingsMetricsHeader", resourceCulture); }
        }
        public static string SettingsAlertsHeader {
            get { return ResourceManager.GetString("SettingsAlertsHeader", resourceCulture); }
        }
        public static string SettingsProvidersHeader {
            get { return ResourceManager.GetString("SettingsProvidersHeader", resourceCulture); }
        }
        public static string SettingsNoExtraOptions {
            get { return ResourceManager.GetString("SettingsNoExtraOptions", resourceCulture); }
        }
    }
}\"\"\"

content = re.sub(r'    }\s*}\s*$', new_props, content)

with open(file_path, "w", encoding="utf-8") as f:
    f.write(content)
