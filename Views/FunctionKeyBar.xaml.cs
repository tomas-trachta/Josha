using System.Windows;
using System.Windows.Controls;

namespace Josha.Views
{
    public partial class FunctionKeyBar : UserControl
    {
        public FunctionKeyBar()
        {
            InitializeComponent();
        }

        private void OnQuit(object sender, RoutedEventArgs e)
        {
            Application.Current?.Shutdown();
        }
    }
}
