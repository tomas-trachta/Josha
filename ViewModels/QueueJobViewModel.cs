using Josha.Models;
using System.Windows.Input;

namespace Josha.ViewModels
{
    internal enum QueueJobStatus { Pending, Running, Completed, Failed, Cancelled }

    internal sealed class QueueJobViewModel : BaseViewModel
    {
        private CancellationTokenSource _cts = new();
        private QueueJobStatus _status = QueueJobStatus.Pending;
        private long _bytesTransferred;
        private string? _errorMessage;
        private DateTime _startedUtc;
        private TimeSpan _duration;
        private readonly Action<QueueJobViewModel>? _retryHandler;

        public FileOperationRequest Request { get; }
        public CancellationToken CancellationToken => _cts.Token;

        public ICommand CancelCommand { get; }
        public ICommand RetryCommand { get; }

        public QueueJobViewModel(FileOperationRequest request, Action<QueueJobViewModel>? retryHandler = null)
        {
            Request = request;
            _retryHandler = retryHandler;
            CancelCommand = new RelayCommand(_ => Cancel(),
                _ => _status == QueueJobStatus.Pending || _status == QueueJobStatus.Running);
            RetryCommand = new RelayCommand(_ => _retryHandler?.Invoke(this),
                _ => IsRetryable);
        }

        public bool IsRetryable => _status == QueueJobStatus.Failed && _retryHandler != null;

        public void ResetForRetry()
        {
            _cts = new CancellationTokenSource();
            _bytesTransferred = 0;
            _errorMessage = null;
            Status = QueueJobStatus.Pending;
            OnPropertyChanged(nameof(BytesTransferred));
            OnPropertyChanged(nameof(ErrorMessage));
            OnPropertyChanged(nameof(ProgressDisplay));
            OnPropertyChanged(nameof(ProgressFraction));
            OnPropertyChanged(nameof(IsRetryable));
        }

        public QueueJobStatus Status
        {
            get => _status;
            private set
            {
                if (_status == value) return;
                _status = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StatusLabel));
                OnPropertyChanged(nameof(StatusBrushKey));
                OnPropertyChanged(nameof(IsActive));
                OnPropertyChanged(nameof(IsRetryable));
                System.Windows.Input.CommandManager.InvalidateRequerySuggested();
            }
        }

        public long BytesTransferred
        {
            get => _bytesTransferred;
            private set
            {
                if (_bytesTransferred == value) return;
                _bytesTransferred = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ProgressDisplay));
                OnPropertyChanged(nameof(ProgressFraction));
            }
        }

        // 0..1; -1 sentinel = unknown total (rendered indeterminate by the bar).
        public double ProgressFraction
        {
            get
            {
                if (Request.SizeHint <= 0) return -1;
                return Math.Clamp((double)_bytesTransferred / Request.SizeHint, 0, 1);
            }
        }

        public string ProgressDisplay
        {
            get
            {
                if (Request.SizeHint > 0)
                    return $"{Format(_bytesTransferred)} / {Format(Request.SizeHint)}";
                return Format(_bytesTransferred);
            }
        }

        public string Title => Request.DisplayName;

        public string KindLabel => Request.Kind == FileOperationKind.Copy ? "Copy" : "Move";

        public bool IsActive => _status == QueueJobStatus.Pending || _status == QueueJobStatus.Running;

        public string? ErrorMessage
        {
            get => _errorMessage;
            private set { if (_errorMessage == value) return; _errorMessage = value; OnPropertyChanged(); }
        }

        public string StatusLabel => _status switch
        {
            QueueJobStatus.Pending   => "Queued",
            QueueJobStatus.Running   => "Running",
            QueueJobStatus.Completed => "Done",
            QueueJobStatus.Failed    => "Failed",
            QueueJobStatus.Cancelled => "Cancelled",
            _                        => _status.ToString(),
        };

        public string StatusBrushKey => _status switch
        {
            QueueJobStatus.Running   => "Brush.Status.Pending",
            QueueJobStatus.Completed => "Brush.Status.Ok",
            QueueJobStatus.Failed    => "Brush.Status.Error",
            QueueJobStatus.Cancelled => "Brush.OnSurfaceMuted",
            _                        => "Brush.OnSurfaceMuted",
        };

        public TimeSpan Duration => _duration;

        public void MarkRunning()
        {
            _startedUtc = DateTime.UtcNow;
            Status = QueueJobStatus.Running;
        }

        public void MarkCompleted()
        {
            _duration = DateTime.UtcNow - _startedUtc;
            Status = QueueJobStatus.Completed;
        }

        public void MarkFailed(string error)
        {
            _duration = DateTime.UtcNow - _startedUtc;
            ErrorMessage = error;
            Status = QueueJobStatus.Failed;
        }

        public void MarkCancelled()
        {
            _duration = DateTime.UtcNow - _startedUtc;
            Status = QueueJobStatus.Cancelled;
        }

        public void UpdateBytes(long total) => BytesTransferred = total;

        public void Cancel()
        {
            try { _cts.Cancel(); } catch { }
        }

        private static string Format(long bytes)
        {
            if (bytes < 1024) return $"{bytes:N0} B";
            double v = bytes / 1024.0;
            if (v < 1024) return $"{v:N1} KB";
            v /= 1024;
            if (v < 1024) return $"{v:N1} MB";
            v /= 1024;
            return $"{v:N2} GB";
        }
    }
}
