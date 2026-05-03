using Josha.Services;
using Josha.ViewModels;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Input;

namespace Josha.Views
{
    public partial class SettingsSheet : Window
    {
        private readonly SettingsViewModel _vm;

        public SettingsSheet()
        {
            InitializeComponent();
            _vm = new SettingsViewModel(AppServices.Settings);
            DataContext = _vm;
        }

        private void OnBrowseEditorClick(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Pick external editor",
                Filter = "Programs|*.exe;*.cmd;*.bat|All files|*.*",
            };
            if (!string.IsNullOrEmpty(_vm.EditorPath))
            {
                try { dlg.InitialDirectory = System.IO.Path.GetDirectoryName(_vm.EditorPath); }
                catch { /* invalid existing path — ignore */ }
            }
            if (dlg.ShowDialog(this) == true)
                _vm.EditorPath = dlg.FileName;
        }

        private void OnSave(object sender, RoutedEventArgs e)
        {
            var oldTheme = AppServices.Settings.Theme;
            var oldFontScale = AppServices.Settings.FontScale;

            AppServices.UpdateSettings(_vm.ToModel());
            DialogResult = true;
            Close();

            bool themeChanged = !string.Equals(oldTheme, _vm.Theme, StringComparison.Ordinal);
            bool fontScaleChanged = System.Math.Abs(oldFontScale - _vm.FontScale) > 0.005;
            if (themeChanged || fontScaleChanged)
            {
                var what = (themeChanged, fontScaleChanged) switch
                {
                    (true, true)  => "Theme and font scale",
                    (true, false) => "Theme",
                    _             => "Font scale",
                };
                MessageBox.Show(
                    $"{what} will apply on the next launch.\n\nClose and reopen the app to see the change.",
                    "Restart required",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void OnCancelCommand(object sender, ExecutedRoutedEventArgs e) => OnCancel(sender, new RoutedEventArgs());
    }
}
