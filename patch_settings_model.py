import re

file_path = "SidebarDiagnostics/SettingsModel.cs"
with open(file_path, "r", encoding="utf-8") as f:
    content = f.read()

# Add AutoFitScale
new_prop = """
        private bool _autoFitScale;

        public bool AutoFitScale
        {
            get
            {
                return _autoFitScale;
            }
            set
            {
                _autoFitScale = value;

                NotifyPropertyChanged("AutoFitScale");
            }
        }
"""

content = content.replace("public double UIScale", new_prop.strip() + "\n\n        public double UIScale")

# In constructor: AutoFitScale = Framework.Settings.Instance.AutoFitScale;
content = content.replace("UIScale = Framework.Settings.Instance.UIScale;", "AutoFitScale = Framework.Settings.Instance.AutoFitScale;\n            UIScale = Framework.Settings.Instance.UIScale;")

# In Save(): Framework.Settings.Instance.AutoFitScale = AutoFitScale;
content = content.replace("Framework.Settings.Instance.UIScale = Math.Round(UIScale, 2);", "Framework.Settings.Instance.AutoFitScale = AutoFitScale;\n            Framework.Settings.Instance.UIScale = Math.Round(UIScale, 2);")

with open(file_path, "w", encoding="utf-8") as f:
    f.write(content)
