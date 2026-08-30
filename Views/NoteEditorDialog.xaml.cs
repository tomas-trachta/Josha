using System.Windows;
using System.Windows.Input;

namespace Josha.Views
{
    public partial class NoteEditorDialog : Window
    {
        public bool Deleted { get; private set; }
        public string Result { get; private set; } = "";

        public NoteEditorDialog(string name, string path, string initialText)
        {
            InitializeComponent();
            TitleText.Text = name;
            PathText.Text = path;
            NoteBox.Text = initialText;
            DeleteButton.Visibility = string.IsNullOrEmpty(initialText) ? Visibility.Collapsed : Visibility.Visible;

            Loaded += (_, _) =>
            {
                NoteBox.Focus();
                NoteBox.CaretIndex = NoteBox.Text.Length;
            };
        }

        private void Save()
        {
            Result = NoteBox.Text;
            DialogResult = true;
            Close();
        }

        private void OnSave(object sender, RoutedEventArgs e) => Save();

        private void OnSaveCommand(object sender, ExecutedRoutedEventArgs e) => Save();

        private void OnDelete(object sender, RoutedEventArgs e)
        {
            Deleted = true;
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
