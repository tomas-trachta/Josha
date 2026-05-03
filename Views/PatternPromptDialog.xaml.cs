using System.Windows;
using System.Windows.Input;

namespace Josha.Views
{
    public partial class PatternPromptDialog : Window
    {
        public string? Result { get; private set; }

        public PatternPromptDialog(string label)
        {
            InitializeComponent();
            LabelText.Text = label;
            Loaded += (_, _) =>
            {
                PatternBox.Focus();
                PatternBox.SelectAll();
            };
        }

        private void OnOk(object sender, RoutedEventArgs e)
        {
            Result = PatternBox.Text;
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
