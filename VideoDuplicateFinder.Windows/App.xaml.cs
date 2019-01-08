using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace VideoDuplicateFinderWindows
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        static void InstallExceptionHandlers()
        {
            TaskScheduler.UnobservedTaskException += (s, e) => e.SetObserved();
            if (Debugger.IsAttached) return;
            AppDomain.CurrentDomain.UnhandledException += (s, e) => ShowException(e.ExceptionObject as Exception);
            Dispatcher.CurrentDispatcher.UnhandledException += (s, e) =>
            {
                ShowException(e.Exception);
                e.Handled = true;
            };
        }
        static void ShowException(Exception ex)
        {
            string msg = ex?.ToString() ?? "Unknown exception";
            MessageBox.Show(msg, "Video Duplicate Finder", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        public App()
        {
            InstallExceptionHandlers();
        }
    }
}
