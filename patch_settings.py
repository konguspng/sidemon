import re

file_path = "SidebarDiagnostics/Settings.cs"
with open(file_path, "r", encoding="utf-8") as f:
    content = f.read()

# Add AutoFitScale
new_prop = """
        private bool _autoFitScale { get; set; } = true;

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

# Insert before UIScale
content = content.replace("public double UIScale", new_prop.strip() + "\n\n        public double UIScale")

with open(file_path, "w", encoding="utf-8") as f:
    f.write(content)
