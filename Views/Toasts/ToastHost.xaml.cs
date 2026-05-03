using Josha.Services;
using System.Windows.Controls;

namespace Josha.Views.Toasts
{
    public partial class ToastHost : UserControl
    {
        public ToastHost()
        {
            InitializeComponent();
            DataContext = AppServices.Toast;
        }
    }
}
