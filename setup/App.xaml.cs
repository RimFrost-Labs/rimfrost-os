using System.Windows;

namespace RimFrostSetup
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            Log.Write("=== RimFrost Setup 0.5 started");
            DispatcherUnhandledException += (s, ev) =>
            {
                Log.Write("crash: " + ev.Exception);
                MessageBox.Show("Something went wrong in RimFrost Setup:\n\n" + ev.Exception.Message +
                                "\n\nThe log is at " + Log.FilePath, "RimFrost Setup", MessageBoxButton.OK, MessageBoxImage.Error);
                ev.Handled = true;
            };
            base.OnStartup(e);
        }
    }
}
