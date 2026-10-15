import re

with open('SidebarDiagnostics/Monitoring.cs', 'r', encoding='utf-8') as f:
    content = f.read()

props = '''        private bool _autoRenew { get; set; }

        public bool AutoRenew
        {
            get { return _autoRenew; }
            set { if (_autoRenew == value) return; _autoRenew = value; NotifyPropertyChanged("AutoRenew"); }
        }

        private string _renewStatus { get; set; }

        [System.Text.Json.Serialization.JsonIgnore]
        public string RenewStatus
        {
            get { return _renewStatus; }
            set { if (_renewStatus == value) return; _renewStatus = value; NotifyPropertyChanged("RenewStatus"); }
        }

'''

content = content.replace('        private string _customPath { get; set; }', props + '        private string _customPath { get; set; }')

with open('SidebarDiagnostics/Monitoring.cs', 'w', encoding='utf-8') as f:
    f.write(content)
