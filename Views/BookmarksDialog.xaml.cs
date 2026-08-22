using Josha.Models;
using Josha.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Josha.Views
{
    public partial class BookmarksDialog : Window
    {
        private readonly AppShellViewModel _shell;

        internal BookmarksDialog(AppShellViewModel shell)
        {
            _shell = shell;
            DataContext = shell;
            InitializeComponent();
            // No forced default selection — same reasoning as the command
            // palette: pre-highlighting the first bookmark before the user has
            // done anything reads as arbitrary rather than intentional. Arrow
            // keys still select naturally from here once pressed.
            Loaded += (_, _) => BookmarkList.Focus();
        }

        private void Activate(Bookmark? bookmark)
        {
            if (bookmark == null) return;
            _shell.NavigateToBookmarkCommand.Execute(bookmark);
            Close();
        }

        private void OnRowDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (BookmarkList.SelectedItem is Bookmark b)
                Activate(b);
        }

        private void OnListKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && BookmarkList.SelectedItem is Bookmark b)
            {
                Activate(b);
                e.Handled = true;
            }
        }

        private void OnRemoveClick(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is Bookmark b)
            {
                _shell.RemoveBookmarkCommand.Execute(b);
                e.Handled = true;
            }
        }

        private void OnClose(object sender, ExecutedRoutedEventArgs e) => Close();
    }
}
