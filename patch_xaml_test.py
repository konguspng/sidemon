import os
p = "SidebarDiagnostics/Sidebar.xaml"
txt = open(p).read()
import re
txt = re.sub(r'<DataTrigger\.EnterActions>.*?</DataTrigger\.EnterActions>', '', txt, flags=re.DOTALL)
txt = re.sub(r'<DataTrigger\.ExitActions>.*?</DataTrigger\.ExitActions>', '', txt, flags=re.DOTALL)
open(p, 'w').write(txt)
