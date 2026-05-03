using Josha.Models;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace Josha.Views
{
    public partial class OverwriteSheet : Window
    {
        public string Headline { get; }
        public string ConflictListText { get; }

        internal OverwriteResolution Result { get; private set; } = OverwriteResolution.Cancel;

        internal OverwriteSheet(System.Collections.Generic.IReadOnlyList<string> conflictNames)
        {
            var n = conflictNames.Count;
            Headline = $"{n} item{(n == 1 ? "" : "s")} already exist in the destination.";
            var sample = string.Join(", ", conflictNames.Take(8));
            if (n > 8) sample += $" (+{n - 8} more)";
            ConflictListText = sample;

            InitializeComponent();
        }

        private void OnReplace(object sender, RoutedEventArgs e) { Result = OverwriteResolution.Replace; Close(); }
        private void OnSkip(object sender, RoutedEventArgs e)    { Result = OverwriteResolution.Skip;    Close(); }
        private void OnCancel(object sender, RoutedEventArgs e)  { Result = OverwriteResolution.Cancel;  Close(); }
        private void OnCancelCommand(object sender, ExecutedRoutedEventArgs e) { Result = OverwriteResolution.Cancel; Close(); }
    }
}
