using Josha.Business;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;

namespace Josha.Views
{
    public partial class OpenWithDialog : Window
    {
        internal OpenWithHandlerEntry? SelectedHandler { get; private set; }

        internal OpenWithDialog(IReadOnlyList<OpenWithHandlerEntry> handlers)
        {
            InitializeComponent();
            HandlerList.ItemsSource = handlers;
            if (handlers.Count > 0) HandlerList.SelectedIndex = 0;
            Loaded += (_, _) => HandlerList.Focus();
        }

        private void OnOk(object sender, RoutedEventArgs e) => Accept();

        private void OnListDoubleClick(object sender, MouseButtonEventArgs e) => Accept();

        private void Accept()
        {
            if (HandlerList.SelectedItem is not OpenWithHandlerEntry handler) return;
            SelectedHandler = handler;
            DialogResult = true;
            Close();
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void OnCancelCommand(object sender, ExecutedRoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
