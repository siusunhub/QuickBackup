using System.Windows;

namespace quickbackup
{
    public partial class LogWindow : Window
    {
        public LogWindow(string logContent)
        {
            InitializeComponent();
            txtLogContent.Text = logContent;
        }
    }
}
