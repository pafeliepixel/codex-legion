using System;
using System.Windows;

namespace CodexNativeLite
{
    public static class App
    {
        [STAThread]
        public static void Main()
        {
            var application = new Application();
            application.ShutdownMode = ShutdownMode.OnMainWindowClose;
            application.Run(new MainWindow());
        }
    }
}
