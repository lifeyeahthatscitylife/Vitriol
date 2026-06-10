using System;
using System.Windows;
using System.Windows.Threading;

namespace Vitriol.Editor;

public partial class App : Application
{
    public App()
    {
        // Catch UI thread exceptions
        DispatcherUnhandledException += (_, e) =>
        {
            MessageBox.Show(e.Exception.ToString(), "Vitriol Editor crashed (UI thread)");
            e.Handled = true;
            Shutdown(-1);
        };

        // Catch non-UI exceptions
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            MessageBox.Show(e.ExceptionObject?.ToString() ?? "Unknown error",
                "Vitriol Editor crashed (AppDomain)");
        };
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            // Force-create and show MainWindow so StartupUri issues can't silently exit the app
            var window = new MainWindow();
            MainWindow = window;
            window.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.ToString(), "Vitriol Editor failed during startup");
            Shutdown(-1);
        }
    }
}
