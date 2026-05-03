using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Josha.Views
{
    public partial class SearchBox : UserControl
    {
        public static readonly DependencyProperty PlaceholderProperty =
            DependencyProperty.Register(
                nameof(Placeholder), typeof(string), typeof(SearchBox),
                new PropertyMetadata(""));

        public static readonly DependencyProperty SearchTextProperty =
            DependencyProperty.Register(
                nameof(SearchText), typeof(string), typeof(SearchBox),
                new FrameworkPropertyMetadata("",
                    FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public static readonly DependencyProperty IsDropDownOpenProperty =
            DependencyProperty.Register(
                nameof(IsDropDownOpen), typeof(bool), typeof(SearchBox),
                new PropertyMetadata(false));

        public static readonly DependencyProperty ItemsSourceProperty =
            DependencyProperty.Register(
                nameof(ItemsSource), typeof(object), typeof(SearchBox),
                new PropertyMetadata(null));

        public static readonly DependencyProperty SelectedItemProperty =
            DependencyProperty.Register(
                nameof(SelectedItem), typeof(object), typeof(SearchBox),
                new FrameworkPropertyMetadata(null,
                    FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public string Placeholder
        {
            get => (string)GetValue(PlaceholderProperty);
            set => SetValue(PlaceholderProperty, value);
        }

        public string SearchText
        {
            get => (string)GetValue(SearchTextProperty);
            set => SetValue(SearchTextProperty, value);
        }

        public bool IsDropDownOpen
        {
            get => (bool)GetValue(IsDropDownOpenProperty);
            set => SetValue(IsDropDownOpenProperty, value);
        }

        public object? ItemsSource
        {
            get => GetValue(ItemsSourceProperty);
            set => SetValue(ItemsSourceProperty, value);
        }

        public object? SelectedItem
        {
            get => GetValue(SelectedItemProperty);
            set => SetValue(SelectedItemProperty, value);
        }

        // Raised when the user presses Escape inside the input. The control
        // already clears SearchText; the consumer typically uses this to move
        // focus back to whatever component owned it before the search began.
        public event EventHandler? EscapePressed;

        public SearchBox()
        {
            InitializeComponent();
        }

        private void OnInputKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape) return;

            SearchText = "";
            EscapePressed?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }
    }
}
