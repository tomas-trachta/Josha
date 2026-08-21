using Josha.Services;
using Josha.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Josha.Views
{
    public partial class GitHistoryWindow : Window
    {
        private GitHistoryWindowViewModel? _vm;

        public GitHistoryWindow()
        {
            InitializeComponent();
        }

        // repoRoot must already be resolved to the git top-level directory —
        // caller is responsible for locating it (keeps this view free of the
        // "is this even a repo" plumbing).
        internal void Load(string repoRoot)
        {
            _vm = new GitHistoryWindowViewModel(repoRoot);
            DataContext = _vm;
            Title = $"Git — {repoRoot}";
            Loaded += async (_, _) => await _vm.InitializeAsync();
        }

        private void OnCloseCommand(object sender, ExecutedRoutedEventArgs e) => Close();

        private void OnChangedFileDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (_vm?.OpenFileCommand.CanExecute(null) == true)
                _vm.OpenFileCommand.Execute(null);
        }

        // Right-click on a row must select it first — otherwise the context
        // menu's "Open" / "Open with..." would act on whatever was already
        // selected instead of the item under the cursor (Explorer behavior).
        private void OnChangedFileRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not ListBox list) return;
            if (e.OriginalSource is not DependencyObject src) return;

            var container = ItemsControl.ContainerFromElement(list, src) as ListBoxItem;
            if (container != null) container.IsSelected = true;
        }

        // Shows the same "recommended apps" list Explorer's own "Open with"
        // menu would for this file's extension. The chosen handler opens
        // both the commit's version and the current version in one call, so
        // editors that reuse a window show them as two tabs in one instance.
        private void OnOpenFileWithClick(object sender, RoutedEventArgs e)
        {
            if (_vm?.SelectedFile == null) return;

            var handlers = _vm.GetOpenWithHandlers();
            if (handlers.Count == 0)
            {
                AppServices.Toast.Warning("No apps are registered to open this file type.");
                return;
            }

            var dlg = new OpenWithDialog(handlers) { Owner = this };
            if (dlg.ShowDialog() == true && dlg.SelectedHandler != null)
                _ = _vm.OpenSelectedFileWithHandlerAsync(dlg.SelectedHandler);
        }
    }
}
