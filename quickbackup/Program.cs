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
                MessageBox.Show(
                    "QuickBackup is already running.",
                    "QuickBackup",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            ApplicationConfiguration.Initialize();
            Application.Run(new Form1());
        }
    }
}
