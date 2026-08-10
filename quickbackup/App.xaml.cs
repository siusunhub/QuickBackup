namespace quickbackup
{
    public partial class App : System.Windows.Application
    {
        protected override void OnStartup(System.Windows.StartupEventArgs e)
        {
            base.OnStartup(e);
            MainWindow mainWindow = new();
            mainWindow.StartApplication();
        }
    }
}
