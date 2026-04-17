using System;
using System.IO;
using System.Threading;
using System.Windows;

namespace DBServersManager;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    private static Mutex? _singleInstanceMutex;
    private static bool _ownsMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        const string mutexName = "Global\\DBServersManagerSingleInstance";

        _singleInstanceMutex = new Mutex(true, mutexName, out _ownsMutex);
        if (!_ownsMutex)
        {
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
            System.Windows.MessageBox.Show(
                "Another instance of DB Servers Manager is already running.\nOnly one instance is allowed.",
                "Instance already running",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown();
            return;
        }

        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        DispatcherUnhandledException += App_DispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

        base.OnStartup(e);
    }

    private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        LogException(e.Exception);
        System.Windows.MessageBox.Show($"Unhandled UI exception:\n{e.Exception}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            LogException(ex);
            System.Windows.MessageBox.Show($"Unhandled exception:\n{ex}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        LogException(e.Exception);
        System.Windows.MessageBox.Show($"Unhandled task exception:\n{e.Exception}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        e.SetObserved();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_ownsMutex)
            _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        _singleInstanceMutex = null;
        base.OnExit(e);
    }

    private static void LogException(Exception ex)
    {
        try
        {
            var logFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "error.log");
            File.AppendAllText(logFile, $"[{DateTime.Now:O}] {ex}\n\n");
        }
        catch
        {
            // ignore
        }
    }
}

