using System;
using System.Windows.Input;

namespace VideoDuplicateFinderWindows.MVVM
{
    public sealed class DelegateCommand : ICommand
    {
        readonly Action<object> exec;
        readonly Predicate<object> canExec;

        public DelegateCommand(Action<object> exec, Predicate<object> canExec = null)
        {
            this.exec = exec ?? throw new ArgumentNullException(nameof(exec));
            this.canExec = canExec;
        }

        public bool CanExecute(object parameter) => canExec?.Invoke(parameter) ?? true;

        event EventHandler ICommand.CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

        public void Execute(object parameter) => exec(parameter);
    }

    public sealed class DelegateCommand<T> : ICommand
    {
        readonly Action<T> exec;
        readonly Predicate<T> canExec;

        public DelegateCommand(Action<T> exec, Predicate<T> canExec = null)
        {
            this.exec = exec ?? throw new ArgumentNullException(nameof(exec));
            this.canExec = canExec;
        }

        public bool CanExecute(object parameter) => canExec?.Invoke((T)parameter) ?? true;

        event EventHandler ICommand.CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

        public void Execute(object parameter) => exec((T)parameter);
    }
}
