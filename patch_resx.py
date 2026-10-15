import re

file_path = "SidebarDiagnostics/Properties/Resources.resx"
with open(file_path, "r", encoding="utf-8") as f:
    content = f.read()

new_resx = """  <data name="SettingsAutoFitScale" xml:space="preserve">
    <value>Shrink automatically to fit the screen</value>
  </data>
  <data name="SettingsAutoFitScaleTooltip" xml:space="preserve">
    <value>Automatically shrink the UI scale if the enabled monitors and drives require more space than the screen height.</value>
  </data>
</root>"""

content = content.replace("</root>", new_resx)

with open(file_path, "w", encoding="utf-8") as f:
    f.write(content)

file_path = "SidebarDiagnostics/Properties/Resources.Designer.cs"
with open(file_path, "r", encoding="utf-8") as f:
    content = f.read()

new_props = """
        /// <summary>
        ///   Looks up a localized string similar to Shrink automatically to fit the screen.
        /// </summary>
        public static string SettingsAutoFitScale {
            get {
                return ResourceManager.GetString("SettingsAutoFitScale", resourceCulture);
            }
        }

        /// <summary>
        ///   Looks up a localized string similar to Automatically shrink the UI scale if the enabled monitors and drives require more space than the screen height..
        /// </summary>
        public static string SettingsAutoFitScaleTooltip {
            get {
                return ResourceManager.GetString("SettingsAutoFitScaleTooltip", resourceCulture);
            }
        }
    }
}"""

# Using regex to replace the last two closing braces safely
content = re.sub(r'    }\s*}\s*$', new_props, content)

with open(file_path, "w", encoding="utf-8") as f:
    f.write(content)
