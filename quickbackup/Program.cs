using System;
using System.Threading;
using System.Windows;

namespace quickbackup
{
    internal static class Program
    {
        private const string MutexName = "quickbackup_single_instance_7E93F1CC-7952-4C91-A0C4-7E2440D6F8F0";

        [STAThread]
        static void Main()
        {
            using Mutex mutex = new(true, MutexName, out bool createdNew);
            if (!createdNew)
            {
                System.Windows.MessageBox.Show(
                    "QuickBackup is already running.",
                    "QuickBackup",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            App app = new();
            app.InitializeComponent();
            app.Run();
        }
    }
}
