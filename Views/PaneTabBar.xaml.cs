using Josha.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Josha.Views
{
    public partial class PaneTabBar : UserControl
    {
        public PaneTabBar()
        {
            InitializeComponent();
        }

        private void OnTabClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement fe) return;
            if (fe.Tag is not FilePaneViewModel tab) return;
            if (DataContext is not PaneColumnViewModel col) return;

            col.ActiveTab = tab;
            e.Handled = true;
        }

        private void OnTabRightClick(object sender, MouseButtonEventArgs e)
        {
            // Reserved for a future tab context menu (Close others / Move to other column).
            // For now, right-click just activates the tab like left-click.
            OnTabClick(sender, e);
        }

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe) return;
            if (fe.Tag is not FilePaneViewModel tab) return;
            if (DataContext is not PaneColumnViewModel col) return;

            col.CloseTab(tab);
            e.Handled = true;
        }
    }
}
