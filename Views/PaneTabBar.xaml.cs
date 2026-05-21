using Josha.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Josha.Views
{
    public partial class PaneTabBar : UserControl
    {
        // Setting col.ActiveTab here would only switch the tab within this
        // column; the app's ActiveColumn wouldn't move. The shell owns the
        // combined "set ActiveTab + set ActiveColumn" transition, so we bubble
        // the click up and let MainWindow route it through SetActive.
        internal event Action<FilePaneViewModel>? TabActivated;

        public PaneTabBar()
        {
            InitializeComponent();
        }

        private void OnTabClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement fe) return;
            if (fe.Tag is not FilePaneViewModel tab) return;

            TabActivated?.Invoke(tab);
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
