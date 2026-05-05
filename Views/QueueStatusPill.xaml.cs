using Josha.Services;
using Josha.ViewModels;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace Josha.Views
{
    public partial class QueueStatusPill : UserControl
    {
        public QueueStatusPill()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            var queue = AppServices.Queue;
            if (queue == null) return;

            JobsList.ItemsSource = queue.Jobs;
            queue.Jobs.CollectionChanged += OnJobsChanged;
            UpdatePill();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            var queue = AppServices.Queue;
            if (queue != null) queue.Jobs.CollectionChanged -= OnJobsChanged;
        }

        // Per-job PropertyChanged is needed: active→completed transitions
        // change our "active" count without moving entries in the collection.
        private void OnJobsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
                foreach (var x in e.NewItems.OfType<QueueJobViewModel>())
                    x.PropertyChanged += OnJobPropertyChanged;
            if (e.OldItems != null)
                foreach (var x in e.OldItems.OfType<QueueJobViewModel>())
                    x.PropertyChanged -= OnJobPropertyChanged;

            UpdatePill();
        }

        // Status transitions fire from worker threads (FileOperationQueue),
        // so the handler runs off the dispatcher; UpdatePill touches WPF
        // DependencyObjects and would otherwise throw, crashing the worker
        // before the copy/move starts and leaving the job stuck at 0%.
        private void OnJobPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(QueueJobViewModel.Status)) return;
            if (Dispatcher.CheckAccess()) UpdatePill();
            else Dispatcher.BeginInvoke(new Action(UpdatePill));
        }

        private void UpdatePill()
        {
            var queue = AppServices.Queue;
            if (queue == null) return;

            int active = 0;
            int failed = 0;
            foreach (var j in queue.Jobs)
            {
                if (j.IsActive) active++;
                else if (j.Status == QueueJobStatus.Failed) failed++;
            }

            if (active == 0 && failed == 0)
            {
                PillToggle.Visibility = Visibility.Collapsed;
                PillToggle.IsChecked = false;
                return;
            }

            PillToggle.Visibility = Visibility.Visible;
            PillText.Text = (active, failed) switch
            {
                ( > 0, 0)  => $"{active} job{(active == 1 ? "" : "s")} running",
                (0, > 0)   => $"{failed} failed — click to review",
                _          => $"{active} running · {failed} failed",
            };

            PillToggle.BorderBrush = failed > 0 && active == 0
                ? (System.Windows.Media.Brush)Application.Current.Resources["Brush.Status.Error"]
                : (System.Windows.Media.Brush)Application.Current.Resources["Brush.Outline"];
        }

        private void OnClearFinishedClick(object sender, RoutedEventArgs e)
        {
            AppServices.Queue?.ClearTerminal();
        }
    }
}
