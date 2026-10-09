using System;
using System.Windows;
using System.Windows.Threading;

namespace DualSync
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            AppDomain.CurrentDomain.UnhandledException += (sender, args) => { };

            DispatcherUnhandledException += (sender, args) =>
            {
                args.Handled = true;
            };

            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (sender, args) =>
            {
                args.SetObserved();
            };
        }
    }
}
