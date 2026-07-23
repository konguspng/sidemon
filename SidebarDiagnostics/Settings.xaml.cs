using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using SidebarDiagnostics.Models;
using SidebarDiagnostics.Monitoring;
using SidebarDiagnostics.Windows;
using SidebarDiagnostics.Style;
using SidebarDiagnostics.Utilities;

namespace SidebarDiagnostics
{
    /// <summary>
    /// Interaction logic for Settings.xaml
    /// </summary>
    public partial class Settings : FlatWindow
    {
        public Settings(Sidebar sidebar)
        {
            InitializeComponent();

            DataContext = Model = new SettingsModel(sidebar);

            Owner = sidebar;
            ShowDialog();
        }

        private async Task Save(bool finalize)
        {
            // any save involving glass must recreate the sidebar: the in-place reset
            // re-captures the desktop while this dialog may overlap the sidebar, which
            // pollutes the captured background; card toggles restyle enough to need it too
            bool _reloadNeeded =
                Model.GlassBackground != Framework.Settings.Instance.GlassBackground ||
                Model.UseCardStyle != Framework.Settings.Instance.UseCardStyle ||
                Model.GlassBackground;

            Model.Save();

            App.RefreshFpsOverlay();

            await App.Current.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, (Action)(async () =>
            {
                Sidebar _sidebar = App.Current.Sidebar;

                if (_sidebar == null)
                {
                    return;
                }

                if (_reloadNeeded)
                {
                    // glass toggles the window's layered mode, which can only be set
                    // at creation, so the sidebar (and this dialog) must be recreated
                    App._reloadOpenSettings = !finalize;
                    _sidebar.Reload();
                    return;
                }

                await _sidebar.Reset(finalize);
            }));
        }
        
        private static void MoveItem<T>(ObservableCollection<T> collection, T item, int offset)
        {
            if (collection == null || item == null)
            {
                return;
            }

            int _index = collection.IndexOf(item);
            int _newIndex = _index + offset;

            if (_index < 0 || _newIndex < 0 || _newIndex >= collection.Count)
            {
                return;
            }

            collection.Move(_index, _newIndex);
        }

        private void MonitorUp_Click(object sender, RoutedEventArgs e)
        {
            MoveItem(Model.MonitorConfig, (sender as FrameworkElement).DataContext as MonitorConfig, -1);
        }

        private void MonitorDown_Click(object sender, RoutedEventArgs e)
        {
            MoveItem(Model.MonitorConfig, (sender as FrameworkElement).DataContext as MonitorConfig, 1);
        }

        private void HardwareUp_Click(object sender, RoutedEventArgs e)
        {
            HardwareConfig _hardware = (sender as FrameworkElement).DataContext as HardwareConfig;

            MoveItem(OwningHardwareCollection(_hardware), _hardware, -1);
        }

        private void HardwareDown_Click(object sender, RoutedEventArgs e)
        {
            HardwareConfig _hardware = (sender as FrameworkElement).DataContext as HardwareConfig;

            MoveItem(OwningHardwareCollection(_hardware), _hardware, 1);
        }

        private ObservableCollection<HardwareConfig> OwningHardwareCollection(HardwareConfig hardware)
        {
            if (hardware == null)
            {
                return null;
            }

            return Model.MonitorConfig.Select(c => c.HardwareOC).FirstOrDefault(oc => oc != null && oc.Contains(hardware));
        }

        private void NumberBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            if (new Regex("[^0-9.-]+").IsMatch(e.Text))
            {
                e.Handled = true;
            }
        }

        private void OffsetSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (e.NewValue != 0d)
            {
                ShowTrayIconCheckbox.IsChecked = true;
            }
        }

        private void ClickThroughCheckbox_Checked(object sender, RoutedEventArgs e)
        {
            ShowTrayIconCheckbox.IsChecked = true;
        }

        private void ShowTrayIconCheckbox_Unchecked(object sender, RoutedEventArgs e)
        {
            XOffsetSlider.Value = 0d;
            YOffsetSlider.Value = 0d;

            ClickThroughCheckbox.IsChecked = false;
        }
        
        private void BindButton_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_hotkey != null)
            {
                EndBind();
            }

            (sender as ToggleButton).IsChecked = false;
        }

        private void BindToggle_Click(object sender, RoutedEventArgs e)
        {
            _keybinder = (ToggleButton)sender;

            if (_keybinder.IsChecked == true)
            {
                BeginBind(Hotkey.KeyAction.Toggle);
            }
            else
            {
                EndBind();
            }
        }

        private void BindShow_Click(object sender, RoutedEventArgs e)
        {
            _keybinder = (ToggleButton)sender;

            if (_keybinder.IsChecked == true)
            {
                BeginBind(Hotkey.KeyAction.Show);
            }
            else
            {
                EndBind();
            }
        }

        private void BindHide_Click(object sender, RoutedEventArgs e)
        {
            _keybinder = (ToggleButton)sender;

            if (_keybinder.IsChecked == true)
            {
                BeginBind(Hotkey.KeyAction.Hide);
            }
            else
            {
                EndBind();
            }
        }

        private void BindReload_Click(object sender, RoutedEventArgs e)
        {
            _keybinder = (ToggleButton)sender;

            if (_keybinder.IsChecked == true)
            {
                BeginBind(Hotkey.KeyAction.Reload);
            }
            else
            {
                EndBind();
            }
        }

        private void BindClose_Click(object sender, RoutedEventArgs e)
        {
            _keybinder = (ToggleButton)sender;

            if (_keybinder.IsChecked == true)
            {
                BeginBind(Hotkey.KeyAction.Close);
            }
            else
            {
                EndBind();
            }
        }

        private void BindCycleEdge_Click(object sender, RoutedEventArgs e)
        {
            _keybinder = (ToggleButton)sender;

            if (_keybinder.IsChecked == true)
            {
                BeginBind(Hotkey.KeyAction.CycleEdge);
            }
            else
            {
                EndBind();
            }
        }

        private void BindCycleScreen_Click(object sender, RoutedEventArgs e)
        {
            _keybinder = (ToggleButton)sender;

            if (_keybinder.IsChecked == true)
            {
                BeginBind(Hotkey.KeyAction.CycleScreen);
            }
            else
            {
                EndBind();
            }
        }

        private void BindReserveSpace_Click(object sender, RoutedEventArgs e)
        {
            _keybinder = (ToggleButton)sender;

            if (_keybinder.IsChecked == true)
            {
                BeginBind(Hotkey.KeyAction.ReserveSpace);
            }
            else
            {
                EndBind();
            }
        }

        private void BeginBind(Hotkey.KeyAction action)
        {
            _hotkey = new Hotkey();
            _hotkey.Action = action;
            _hotkey.WinKey = Key.Escape;

            KeyDown += Window_KeyDown;
        }

        private void EndBind()
        {
            KeyDown -= Window_KeyDown;

            Hotkey.KeyAction _action = _hotkey.Action;

            if (_hotkey.WinKey == Key.Escape)
            {
                _hotkey = null;
            }

            switch (_action)
            {
                case Hotkey.KeyAction.Toggle:
                    Model.ToggleKey = _hotkey;
                    break;

                case Hotkey.KeyAction.Show:
                    Model.ShowKey = _hotkey;
                    break;

                case Hotkey.KeyAction.Hide:
                    Model.HideKey = _hotkey;
                    break;

                case Hotkey.KeyAction.Reload:
                    Model.ReloadKey = _hotkey;
                    break;

                case Hotkey.KeyAction.Close:
                    Model.CloseKey = _hotkey;
                    break;

                case Hotkey.KeyAction.CycleEdge:
                    Model.CycleEdgeKey = _hotkey;
                    break;

                case Hotkey.KeyAction.CycleScreen:
                    Model.CycleScreenKey = _hotkey;
                    break;

                case Hotkey.KeyAction.ReserveSpace:
                    Model.ReserveSpaceKey = _hotkey;
                    break;
            }

            _keybinder.IsChecked = false;
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            Key _key = e.Key == Key.System ? e.SystemKey : e.Key;

            if (new Key[] { Key.LeftAlt, Key.RightAlt, Key.LeftCtrl, Key.RightCtrl, Key.LeftShift, Key.RightShift, Key.LWin, Key.RWin }.Contains(_key))
            {
                return;
            }

            if ((e.KeyboardDevice.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                _hotkey.CtrlMod = true;
            }

            if ((e.KeyboardDevice.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
            {
                _hotkey.ShiftMod = true;
            }

            if ((e.KeyboardDevice.Modifiers & ModifierKeys.Windows) == ModifierKeys.Windows)
            {
                _hotkey.WinMod = true;
            }

            if ((e.KeyboardDevice.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt)
            {
                _hotkey.AltMod = true;
            }

            _hotkey.WinKey = _key;

            EndBind();

            e.Handled = true;
        }

        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            await Save(true);

            Close();
        }

        private async void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            await Save(false);
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            if (Model.IsChanged)
            {
                Sidebar _sidebar = App.Current.Sidebar;

                if (_sidebar != null)
                {
                    DataContext = Model = new SettingsModel(_sidebar);
                    return;
                }
            }

            Close();
        }

        private async void InstallPawnIO_Click(object sender, RoutedEventArgs e)
        {
            Model.PawnIODriverInstalling = true;

            bool _installed;

            try
            {
                _installed = await PawnIO.InstallAsync();
            }
            catch (Exception ex)
            {
                ErrorLog.Write(ex);
                _installed = false;
            }
            finally
            {
                Model.PawnIODriverInstalling = false;
            }

            Model.PawnIODriverInstalled = _installed;

            if (!_installed)
            {
                MessageBoxResult _fallback = MessageBox.Show(
                    "Automatic installation failed. Open pawnio.eu in your browser to install it manually?",
                    Framework.Resources.AppName,
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning,
                    MessageBoxResult.Yes);

                if (_fallback == MessageBoxResult.Yes)
                {
                    App.OpenURL("https://pawnio.eu");
                }

                return;
            }

            Sidebar _sidebar = App.Current.Sidebar;

            if (_sidebar == null || !_sidebar.Ready)
            {
                return;
            }

            var _prompt = new ReloadPromptDialog("PawnIO installed successfully. Reload SideMon now to start showing CPU and GPU sensor data?");
            _prompt.ShowDialog();

            if (_prompt.ReloadRequested)
            {
                _sidebar.Reload();
            }
        }

        private void ColorBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var border = sender as Border;
            if (border == null) return;

            string propertyName = border.Tag as string;
            if (string.IsNullOrEmpty(propertyName)) return;

            if (Model == null) return;

            string currentColorStr = "";
            if (propertyName == "BGColor") currentColorStr = Model.BGColor;
            else if (propertyName == "FontColor") currentColorStr = Model.FontColor;
            else if (propertyName == "AlertFontColor") currentColorStr = Model.AlertFontColor;
            else if (propertyName == "AccentColor") currentColorStr = Model.AccentColor;

            using (var dialog = new System.Windows.Forms.ColorDialog())
            {
                dialog.AnyColor = true;
                dialog.FullOpen = true;

                // Try to parse the existing hex color to set the picker initial state
                try
                {
                    var drawingColor = System.Drawing.ColorTranslator.FromHtml(currentColorStr);
                    dialog.Color = drawingColor;
                }
                catch { }

                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    string hexColor = "#" + dialog.Color.R.ToString("X2") + dialog.Color.G.ToString("X2") + dialog.Color.B.ToString("X2");

                    if (propertyName == "BGColor") Model.BGColor = hexColor;
                    else if (propertyName == "FontColor") Model.FontColor = hexColor;
                    else if (propertyName == "AlertFontColor") Model.AlertFontColor = hexColor;
                    else if (propertyName == "AccentColor") Model.AccentColor = hexColor;
                }
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            Hotkey.Disable();

            // keep the whole dialog (incl. Save/Apply) above the taskbar on any resolution
            MaxHeight = SystemParameters.WorkArea.Height;

            if (Top + ActualHeight > SystemParameters.WorkArea.Bottom)
            {
                Top = Math.Max(SystemParameters.WorkArea.Top, SystemParameters.WorkArea.Bottom - ActualHeight);
            }
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            DataContext = null;
            Model = null;
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            Hotkey.Enable();
        }

        public SettingsModel Model { get; private set; }

        private Hotkey _hotkey { get; set; }

        private ToggleButton _keybinder { get; set; }
    }
}
