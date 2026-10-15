import re

file_path = "SidebarDiagnostics/Settings.xaml.cs"
with open(file_path, "r", encoding="utf-8") as f:
    content = f.read()

new_class = """
    public class Win32Window : System.Windows.Forms.IWin32Window
    {
        public System.IntPtr Handle { get; private set; }

        public Win32Window(System.IntPtr handle)
        {
            Handle = handle;
        }
    }
}
"""

content = re.sub(r'}\s*}\s*$', new_class, content)

with open(file_path, "w", encoding="utf-8") as f:
    f.write(content)
