using MCUScope.Services;
using System;
using System.IO;
using System.Windows;

namespace MCUScope
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            string logDir = Path.Combine(AppContext.BaseDirectory, "logs");
            LogService.Initialize(logDir);
            LogService.Info("MCUScope starting");
        }
    }
}
