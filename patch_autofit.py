import re

file_path = "SidebarDiagnostics/Sidebar.xaml.cs"
with open(file_path, "r", encoding="utf-8") as f:
    content = f.read()

old_code = """
            bool _overflowing = _naturalHeight > _availableHeight && _naturalHeight > 0d;

            double _scale = _overflowing
                ? Math.Round(Math.Max(0.5d, Math.Min(3.0d, _availableHeight / _naturalHeight)), 2)
                : 1.0d;

            // hard cap: once the enabled monitors need more room than the screen at
            // full size, the user can no longer scale back up past the point where
            // it would overflow again
            double _maxScale = _overflowing ? _scale : 3.0d;

            bool _changed = Math.Abs(Framework.Settings.Instance.UIScale - _scale) > 0.01d
                || Math.Abs(Framework.Settings.Instance.MaxUIScale - _maxScale) > 0.01d;

            if (_changed)
            {
                Framework.Settings.Instance.MaxUIScale = _maxScale;
                Framework.Settings.Instance.UIScale = _scale;
                Framework.Settings.Instance.Save();
            }
"""

new_code = """
            if (!Framework.Settings.Instance.AutoFitScale)
            {
                if (Math.Abs(Framework.Settings.Instance.MaxUIScale - 3.0d) > 0.01d)
                {
                    Framework.Settings.Instance.MaxUIScale = 3.0d;
                    Framework.Settings.Instance.Save();
                }
                return;
            }

            bool _overflowing = _naturalHeight > _availableHeight && _naturalHeight > 0d;

            double _maxScale = 3.0d;
            bool _changed = false;

            if (_overflowing)
            {
                double _scale = Math.Round(Math.Max(0.5d, Math.Min(3.0d, _availableHeight / _naturalHeight)), 2);
                if (Math.Abs(Framework.Settings.Instance.UIScale - _scale) > 0.01d)
                {
                    Framework.Settings.Instance.UIScale = _scale;
                    _changed = true;
                }
            }

            if (Math.Abs(Framework.Settings.Instance.MaxUIScale - _maxScale) > 0.01d)
            {
                Framework.Settings.Instance.MaxUIScale = _maxScale;
                _changed = true;
            }

            if (_changed)
            {
                Framework.Settings.Instance.Save();
            }
"""

content = content.replace(old_code.strip(), new_code.strip())

with open(file_path, "w", encoding="utf-8") as f:
    f.write(content)
