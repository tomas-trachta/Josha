using Josha.Business;
using Josha.Models;
using Josha.ViewModels;
using System.Collections.ObjectModel;
using System.Threading.Channels;
using System.Windows;

namespace Josha.Services
{
    // MaxConcurrent default 3: avoids saturating a single FTP site's 2-conn
    // pool while still letting a local copy run alongside a remote upload.
    internal sealed class FileOperationQueue
    {
        public int MaxConcurrent { get; set; } = 3;
        public TimeSpan TerminalLifetime { get; set; } = TimeSpan.FromSeconds(10);

        public ObservableCollection<QueueJobViewModel> Jobs { get; } = new();

        private readonly Channel<QueueJobViewModel> _channel =
            Channel.CreateUnbounded<QueueJobViewModel>(new UnboundedChannelOptions
            {
                SingleReader = false,
                SingleWriter = false,
            });

        private bool _started;

        public void Start()
        {
            if (_started) return;
            _started = true;
            for (int i = 0; i < MaxConcurrent; i++)
                _ = Task.Run(WorkerLoopAsync);
        }

        public QueueJobViewModel Enqueue(FileOperationRequest request)
        {
            var job = new QueueJobViewModel(request, RetryJob);
            // Mutating Jobs off the UI thread crashes bound views.
            DispatchInvoke(() => Jobs.Add(job));
            _channel.Writer.TryWrite(job);
            return job;
        }

        private void RetryJob(QueueJobViewModel job)
        {
            if (job.Status != QueueJobStatus.Failed) return;
            DispatchInvoke(job.ResetForRetry);
            _channel.Writer.TryWrite(job);
        }

        public void ClearTerminal()
        {
            DispatchInvoke(() =>
            {
                for (int i = Jobs.Count - 1; i >= 0; i--)
                    if (!Jobs[i].IsActive)
                        Jobs.RemoveAt(i);
            });
        }

        private async Task WorkerLoopAsync()
        {
            await foreach (var job in _channel.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                try { await ExecuteAsync(job).ConfigureAwait(false); }
                catch (Exception ex) { Log.Error("Queue", $"Worker crashed on {job.Request.DisplayName}", ex); }
            }
        }

        private async Task ExecuteAsync(QueueJobViewModel job)
        {
            var req = job.Request;
            var ct = job.CancellationToken;

            if (ct.IsCancellationRequested) { job.MarkCancelled(); ScheduleClear(job); return; }

            job.MarkRunning();

            var progress = new Progress<long>(b => job.UpdateBytes(b));

            FileOpsComponent.OpResult result;
            try
            {
                if (req.Kind == FileOperationKind.Copy)
                {
                    result = await ExecuteCopyAsync(req, progress, ct).ConfigureAwait(false);
                }
                else
                {
                    result = await ExecuteMoveAsync(req, progress, ct).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { job.MarkCancelled(); ScheduleClear(job); FireRefresh(req); return; }
            catch (Exception ex) { job.MarkFailed(ex.Message); ScheduleClear(job); FireRefresh(req); return; }

            if (result.Success) job.MarkCompleted();
            else if (string.Equals(result.Error, "Cancelled", StringComparison.OrdinalIgnoreCase)) job.MarkCancelled();
            else job.MarkFailed(result.Error ?? "Unknown error");

            ScheduleClear(job);
            FireRefresh(req);
        }

        private static async Task<FileOpsComponent.OpResult> ExecuteCopyAsync(
            FileOperationRequest req, IProgress<long> progress, CancellationToken ct)
        {
            if (req.SrcProvider.ProviderId == req.DstProvider.ProviderId)
                return await req.DstProvider.CopyAsync(req.SrcPath, req.DstPath, progress, req.Overwrite, ct).ConfigureAwait(false);
            return await req.DstProvider.ImportFromAsync(req.SrcProvider, req.SrcPath, req.DstPath, progress, req.Overwrite, ct).ConfigureAwait(false);
        }

        private static async Task<FileOpsComponent.OpResult> ExecuteMoveAsync(
            FileOperationRequest req, IProgress<long> progress, CancellationToken ct)
        {
            if (req.SrcProvider.ProviderId == req.DstProvider.ProviderId)
                return await req.DstProvider.MoveAsync(req.SrcPath, req.DstPath, progress, req.Overwrite, ct).ConfigureAwait(false);

            // Cross-provider: copy first, delete source only on success.
            // If the delete fails the file is duplicated, not lost.
            var copy = await req.DstProvider.ImportFromAsync(req.SrcProvider, req.SrcPath, req.DstPath, progress, req.Overwrite, ct).ConfigureAwait(false);
            if (!copy.Success) return copy;

            var del = await req.SrcProvider.DeleteAsync(req.SrcPath, toRecycle: false, ct).ConfigureAwait(false);
            if (!del.Success) return FileOpsComponent.OpResult.Fail($"Copied but couldn't remove source: {del.Error}");
            return FileOpsComponent.OpResult.Ok();
        }

        // Failed jobs stay until retried or manually cleared so the failure
        // remains actionable.
        private void ScheduleClear(QueueJobViewModel job)
        {
            if (job.Status == QueueJobStatus.Failed) return;

            _ = Task.Delay(TerminalLifetime).ContinueWith(_ =>
            {
                DispatchInvoke(() =>
                {
                    if (job.Status != QueueJobStatus.Failed && !job.IsActive)
                        Jobs.Remove(job);
                });
            });
        }

        private static void FireRefresh(FileOperationRequest req)
        {
            if (req.OnComplete == null) return;
            DispatchInvoke(req.OnComplete);
        }

        private static void DispatchInvoke(Action action)
        {
            var disp = Application.Current?.Dispatcher;
            if (disp == null || disp.CheckAccess()) action();
            else disp.BeginInvoke(action);
        }
    }
}
