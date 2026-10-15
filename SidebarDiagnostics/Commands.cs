using System;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace SidebarDiagnostics.Commands
{
    public class ActivateCommand : ICommand
    {
        public void Execute(object parameter)
        {
            Sidebar _sidebar = App.Current.Sidebar;

            if (_sidebar == null)
            {
                return;
            }
            
            _sidebar.Activate();
        }

        public bool CanExecute(object parameter)
        {
            return true;
        }

        public event EventHandler CanExecuteChanged { add { } remove { } }
    }

    public class ActionCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool> _canExecute;

        public ActionCommand(Action execute, Func<bool> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object parameter)
        {
            return _canExecute == null || _canExecute();
        }

        public void Execute(object parameter)
        {
            _execute();
        }

        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }
    }
}
