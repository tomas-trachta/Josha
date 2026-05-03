using Josha.ViewModels;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Josha.Views
{
    public partial class PathBarControl : UserControl
    {
        public PathBarControl()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
        }

        public sealed class Segment
        {
            public string Display { get; init; } = "";
            public string FullPath { get; init; } = "";
            public bool ShowSeparator { get; init; }
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is FilePaneViewModel oldVm)
                oldVm.PropertyChanged -= OnVmPropertyChanged;

            if (e.NewValue is FilePaneViewModel newVm)
            {
                newVm.PropertyChanged += OnVmPropertyChanged;
                RebuildSegments(newVm.CurrentPath);
            }
        }

        private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(FilePaneViewModel.CurrentPath)
                && sender is FilePaneViewModel vm)
            {
                RebuildSegments(vm.CurrentPath);
            }
        }

        private void RebuildSegments(string fullPath)
        {
            var segments = new List<Segment>();
            if (string.IsNullOrEmpty(fullPath))
            {
                SegmentsHost.ItemsSource = segments;
                return;
            }

            try
            {
                var trimmed = fullPath.TrimEnd('\\', '/');
                if (trimmed.Length == 0) trimmed = fullPath;
                var parts = trimmed.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);

                var accum = "";
                for (int i = 0; i < parts.Length; i++)
                {
                    if (i == 0)
                    {
                        // Drive root needs a trailing '\' for navigation; bare "C:"
                        // resolves relative to the process's per-drive cwd.
                        accum = parts[i] + Path.DirectorySeparatorChar;
                        segments.Add(new Segment { Display = parts[i], FullPath = accum, ShowSeparator = false });
                    }
                    else
                    {
                        accum = Path.Combine(accum, parts[i]);
                        segments.Add(new Segment { Display = parts[i], FullPath = accum, ShowSeparator = true });
                    }
                }
            }
            catch
            {
                segments.Clear();
                segments.Add(new Segment { Display = fullPath, FullPath = fullPath, ShowSeparator = false });
            }

            SegmentsHost.ItemsSource = segments;
        }

        private void OnSegmentClick(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn) return;
            if (btn.Tag is not string fullPath) return;
            if (DataContext is not FilePaneViewModel vm) return;

            _ = vm.NavigateAsync(fullPath);
            e.Handled = true;
        }

        private void OnBackgroundClick(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is Button) return;
            if (e.OriginalSource is TextBlock) return;
            if (DataContext is not FilePaneViewModel vm) return;

            EnterEditMode(vm.CurrentPath);
            e.Handled = true;
        }

        private void EnterEditMode(string initialText)
        {
            SegmentsHost.Visibility = Visibility.Collapsed;
            EditBox.Visibility = Visibility.Visible;
            EditBox.Text = initialText;
            EditBox.SelectAll();
            EditBox.Focus();
            Keyboard.Focus(EditBox);
        }

        private void ExitEditMode()
        {
            SegmentsHost.Visibility = Visibility.Visible;
            EditBox.Visibility = Visibility.Collapsed;
        }

        private void OnEditKeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Enter:
                    if (DataContext is FilePaneViewModel vm)
                    {
                        var target = EditBox.Text?.Trim();
                        if (!string.IsNullOrEmpty(target))
                            _ = vm.NavigateAsync(target);
                    }
                    ExitEditMode();
                    e.Handled = true;
                    break;

                case Key.Escape:
                    ExitEditMode();
                    e.Handled = true;
                    break;
            }
        }

        private void OnEditLostFocus(object sender, RoutedEventArgs e)
        {
            ExitEditMode();
        }
    }
}
