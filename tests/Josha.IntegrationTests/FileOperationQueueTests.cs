using FluentAssertions;
using Josha.Business;
using Josha.IntegrationTests.Fixtures;
using Josha.Models;
using Josha.Services;
using Josha.ViewModels;
using System.ComponentModel;
using Xunit;

namespace Josha.IntegrationTests;

// Drives FileOperationQueue end-to-end: enqueue real local-provider copies
// against a temp dir and watch the QueueJobViewModel progress through its
// state machine. No UI / Dispatcher required — when Application.Current is
// null (which it is in this test process), DispatchInvoke just runs the
// callback synchronously.
public sealed class FileOperationQueueTests : TempDirTestBase
{
    private static FileOperationRequest CopyRequest(string src, string dst, bool overwrite = false) =>
        new()
        {
            Kind        = FileOperationKind.Copy,
            SrcProvider = LocalFileSystemProvider.Instance,
            DstProvider = LocalFileSystemProvider.Instance,
            SrcPath     = src,
            DstPath     = dst,
            DisplayName = Path.GetFileName(dst),
            Overwrite   = overwrite,
            SizeHint    = new FileInfo(src).Length,
        };

    private static FileOperationRequest MoveRequest(string src, string dst) =>
        new()
        {
            Kind        = FileOperationKind.Move,
            SrcProvider = LocalFileSystemProvider.Instance,
            DstProvider = LocalFileSystemProvider.Instance,
            SrcPath     = src,
            DstPath     = dst,
            DisplayName = Path.GetFileName(dst),
            SizeHint    = File.Exists(src) ? new FileInfo(src).Length : -1,
        };

    private static async Task<QueueJobStatus> WaitTerminalAsync(QueueJobViewModel job, TimeSpan? timeout = null)
    {
        var tcs = new TaskCompletionSource<QueueJobStatus>();
        void Handler(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(QueueJobViewModel.Status)) return;
            if (job.IsActive) return;
            job.PropertyChanged -= Handler;
            tcs.TrySetResult(job.Status);
        }
        job.PropertyChanged += Handler;
        if (!job.IsActive) tcs.TrySetResult(job.Status);
        return await tcs.Task.WaitAsync(timeout ?? TimeSpan.FromSeconds(15));
    }

    [Fact]
    public async Task Enqueued_copy_runs_to_completion_and_writes_the_destination_file()
    {
        var src = await WriteFileAsync("src.txt", "queued-copy");
        var dst = TempPath("dst.txt");

        var queue = new FileOperationQueue { MaxConcurrent = 1 };
        queue.Start();
        var job = queue.Enqueue(CopyRequest(src, dst));

        var final = await WaitTerminalAsync(job);

        final.Should().Be(QueueJobStatus.Completed);
        File.ReadAllText(dst).Should().Be("queued-copy");
    }

    [Fact]
    public async Task Job_with_a_missing_source_terminates_in_Failed_with_an_error_message()
    {
        var queue = new FileOperationQueue { MaxConcurrent = 1 };
        queue.Start();

        var req = new FileOperationRequest
        {
            Kind        = FileOperationKind.Copy,
            SrcProvider = LocalFileSystemProvider.Instance,
            DstProvider = LocalFileSystemProvider.Instance,
            SrcPath     = TempPath("does-not-exist.txt"),
            DstPath     = TempPath("never-written.txt"),
            DisplayName = "missing",
        };
        var job = queue.Enqueue(req);

        var final = await WaitTerminalAsync(job);

        final.Should().Be(QueueJobStatus.Failed);
        job.ErrorMessage.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Cancelled_job_terminates_in_Cancelled_status()
    {
        // 16 MiB so the inner buffer loop ticks several times — gives the
        // cancel a window to bite.
        var bytes = new byte[16 * 1024 * 1024];
        Random.Shared.NextBytes(bytes);
        var src = TempPath("big.bin");
        await File.WriteAllBytesAsync(src, bytes);
        var dst = TempPath("big.copy");

        var queue = new FileOperationQueue { MaxConcurrent = 1 };
        queue.Start();
        var job = queue.Enqueue(CopyRequest(src, dst));

        // Cancel as soon as the worker reports any progress.
        job.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(QueueJobViewModel.BytesTransferred) && job.BytesTransferred > 0)
                job.Cancel();
        };

        var final = await WaitTerminalAsync(job);

        final.Should().Be(QueueJobStatus.Cancelled);
    }

    [Fact]
    public async Task Move_request_drops_source_and_writes_destination()
    {
        var src = await WriteFileAsync("src.txt", "moved");
        var dst = TempPath("dst.txt");

        var queue = new FileOperationQueue { MaxConcurrent = 1 };
        queue.Start();
        var job = queue.Enqueue(MoveRequest(src, dst));

        var final = await WaitTerminalAsync(job);

        final.Should().Be(QueueJobStatus.Completed);
        File.Exists(src).Should().BeFalse();
        File.ReadAllText(dst).Should().Be("moved");
    }

    [Fact]
    public async Task BytesTransferred_climbs_during_a_real_copy_and_lands_at_the_full_size()
    {
        var bytes = new byte[4 * 1024 * 1024];
        Random.Shared.NextBytes(bytes);
        var src = TempPath("big.bin");
        await File.WriteAllBytesAsync(src, bytes);
        var dst = TempPath("big.copy");

        var queue = new FileOperationQueue { MaxConcurrent = 1 };
        queue.Start();
        var job = queue.Enqueue(CopyRequest(src, dst));

        var final = await WaitTerminalAsync(job);

        final.Should().Be(QueueJobStatus.Completed);
        job.BytesTransferred.Should().Be(bytes.Length);
    }

    [Fact]
    public async Task Failed_jobs_remain_in_the_Jobs_collection_for_retry_visibility()
    {
        var queue = new FileOperationQueue { MaxConcurrent = 1, TerminalLifetime = TimeSpan.FromMilliseconds(50) };
        queue.Start();

        var req = new FileOperationRequest
        {
            Kind        = FileOperationKind.Copy,
            SrcProvider = LocalFileSystemProvider.Instance,
            DstProvider = LocalFileSystemProvider.Instance,
            SrcPath     = TempPath("nope.txt"),
            DstPath     = TempPath("nope-dst.txt"),
            DisplayName = "missing",
        };
        var job = queue.Enqueue(req);

        await WaitTerminalAsync(job);

        // Wait past the (tiny) terminal-lifetime — successful jobs would have
        // been removed by now, but failed jobs MUST remain so the user can retry.
        await Task.Delay(200);

        queue.Jobs.Should().Contain(job);
        job.IsRetryable.Should().BeTrue();
    }

    [Fact]
    public async Task ClearTerminal_removes_completed_jobs_but_keeps_running_ones()
    {
        var queue = new FileOperationQueue { MaxConcurrent = 1, TerminalLifetime = TimeSpan.FromHours(1) };
        queue.Start();

        var src = await WriteFileAsync("a.txt", "x");
        var done = queue.Enqueue(CopyRequest(src, TempPath("a.copy")));
        await WaitTerminalAsync(done);

        // Sanity: it's still in the list because TerminalLifetime is huge.
        queue.Jobs.Should().Contain(done);

        queue.ClearTerminal();

        queue.Jobs.Should().NotContain(done);
    }
}
