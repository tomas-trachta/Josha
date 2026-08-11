using Josha.Business;
using Josha.Models;
using Josha.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;

namespace Josha.Views
{
    public partial class HistorySidebarView : UserControl
    {
        public static readonly DependencyProperty IsExpandedProperty = DependencyProperty.Register(
            nameof(IsExpanded), typeof(bool), typeof(HistorySidebarView),
            new PropertyMetadata(true, OnIsExpandedChanged));

        public bool IsExpanded
        {
            get => (bool)GetValue(IsExpandedProperty);
            set => SetValue(IsExpandedProperty, value);
        }

        // MainWindow owns the column width the sidebar lives in, so it needs
        // to know when the collapse state changes to resize/restore it.
        public event Action<bool>? ExpandedChanged;

        public HistorySidebarView()
        {
            InitializeComponent();
        }

        private static void OnIsExpandedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var view = (HistorySidebarView)d;
            view.ExpandedChanged?.Invoke((bool)e.NewValue);
        }

        private void OnToggleExpandClick(object sender, RoutedEventArgs e)
        {
            IsExpanded = !IsExpanded;
        }

        private void OnRowDoubleClick(object sender, MouseButtonEventArgs e)
        {
            NavigateToSelected(sender as ListBox);
        }

        private void OnListKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            NavigateToSelected(sender as ListBox);
            e.Handled = true;
        }

        private void NavigateToSelected(ListBox? list)
        {
            if (list?.SelectedItem is not NavigationHistoryEntry entry) return;
            if (DataContext is not AppShellViewModel shell) return;

            shell.NavigateToHistoryEntryCommand.Execute(entry);
        }

        private void OnRowPreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is not ListBox list) return;

            var src = e.OriginalSource as DependencyObject;
            var container = ItemsControl.ContainerFromElement(list, src) as ListBoxItem;
            if (container?.Content is not NavigationHistoryEntry entry) return;

            list.SelectedItem = entry;

            var hwnd = (PresentationSource.FromVisual(this) as HwndSource)?.Handle ?? IntPtr.Zero;
            var screen = list.PointToScreen(e.GetPosition(list));
            ShellContextMenuComponent.Show(new[] { entry.TargetPath }, hwnd, (int)screen.X, (int)screen.Y);
            e.Handled = true;
        }
    }
}
