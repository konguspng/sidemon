import os
p = "SidebarDiagnostics/Sidebar.xaml"
txt = open(p, encoding='utf-8').read()
import re
txt = re.sub(r'<DataTrigger\.EnterActions>.*?</DataTrigger\.EnterActions>', '', txt, flags=re.DOTALL)
txt = re.sub(r'<DataTrigger\.ExitActions>.*?</DataTrigger\.ExitActions>', '', txt, flags=re.DOTALL)
open(p, 'w', encoding='utf-8').write(txt)
