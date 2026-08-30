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
            if (DataContext is not AppShellViewModel shell) return;

            switch (list?.SelectedItem)
            {
                case NavigationHistoryEntry entry:
                    shell.NavigateToHistoryEntryCommand.Execute(entry);
                    break;
                case RemoteHistoryEntry remoteEntry:
                    shell.NavigateToRemoteHistoryEntryCommand.Execute(remoteEntry);
                    break;
                case Note note:
                    shell.NavigateToNoteCommand.Execute(note);
                    break;
            }
        }

        // Notes open on a single click, unlike the double-click sections above
        // — see the layout comment in the XAML for why.
        private void OnNoteRowClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is not ListBox list) return;
            if (DataContext is not AppShellViewModel shell) return;

            var src = e.OriginalSource as DependencyObject;
            var container = ItemsControl.ContainerFromElement(list, src) as ListBoxItem;
            if (container?.Content is not Note note) return;

            list.SelectedItem = note;
            shell.NavigateToNoteCommand.Execute(note);
        }

        private void OnNoteRowPreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is not ListBox list) return;
            if (DataContext is not AppShellViewModel shell) return;

            var src = e.OriginalSource as DependencyObject;
            var container = ItemsControl.ContainerFromElement(list, src) as ListBoxItem;
            if (container?.Content is not Note note) return;

            list.SelectedItem = note;

            var menu = new ContextMenu
            {
                PlacementTarget = list,
                Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint,
            };
            var delete = new MenuItem { Header = "_Delete note", Command = shell.DeleteNoteCommand, CommandParameter = note };
            menu.Items.Add(delete);
            menu.IsOpen = true;

            e.Handled = true;
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
