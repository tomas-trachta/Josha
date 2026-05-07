using FluentAssertions;
using Josha.Business;
using Josha.IntegrationTests.Fixtures;
using Xunit;

namespace Josha.IntegrationTests;

// Drives FileOpsComponent against a real temp directory. Each test runs in
// its own scratch dir under %TEMP%\josha-tests\<guid>\.
public sealed class LocalFileOpsTests : TempDirTestBase
{
    [Fact]
    public async Task CopyAsync_round_trips_a_single_file()
    {
        var src = await WriteFileAsync("src.txt", "alpha");
        var dst = TempPath("dst.txt");

        var r = await FileOpsComponent.CopyAsync(src, dst);

        r.Success.Should().BeTrue(r.Error);
        File.ReadAllText(dst).Should().Be("alpha");
        File.ReadAllText(src).Should().Be("alpha"); // copy doesn't move
    }

    [Fact]
    public async Task CopyAsync_recursively_copies_a_directory_tree()
    {
        await WriteFileAsync(@"src\a.txt", "1");
        await WriteFileAsync(@"src\sub\b.txt", "2");

        var r = await FileOpsComponent.CopyAsync(TempPath("src"), TempPath("dst"));

        r.Success.Should().BeTrue(r.Error);
        File.ReadAllText(TempPath(@"dst\a.txt")).Should().Be("1");
        File.ReadAllText(TempPath(@"dst\sub\b.txt")).Should().Be("2");
    }

    [Fact]
    public async Task CopyAsync_refuses_to_overwrite_existing_destination_when_overwrite_false()
    {
        var src = await WriteFileAsync("src.txt", "new");
        var dst = await WriteFileAsync("dst.txt", "existing");

        var r = await FileOpsComponent.CopyAsync(src, dst, overwrite: false);

        r.Success.Should().BeFalse();
        File.ReadAllText(dst).Should().Be("existing");
    }

    [Fact]
    public async Task CopyAsync_overwrites_when_overwrite_true()
    {
        var src = await WriteFileAsync("src.txt", "new");
        var dst = await WriteFileAsync("dst.txt", "existing");

        var r = await FileOpsComponent.CopyAsync(src, dst, overwrite: true);

        r.Success.Should().BeTrue(r.Error);
        File.ReadAllText(dst).Should().Be("new");
    }

    [Fact]
    public async Task CopyAsync_reports_progress_in_total_bytes()
    {
        // 4 MiB so we cross the 1 MiB internal buffer multiple times.
        var bytes = new byte[4 * 1024 * 1024];
        Random.Shared.NextBytes(bytes);
        var src = TempPath("big.bin");
        await File.WriteAllBytesAsync(src, bytes);

        var samples = new List<long>();
        var progress = new Progress<long>(samples.Add);

        var r = await FileOpsComponent.CopyAsync(src, TempPath("big.copy"), progress);

        r.Success.Should().BeTrue(r.Error);
        // Wait for the Progress<T> SynchronizationContext-posted callbacks to drain.
        await Task.Delay(100);
        samples.Should().NotBeEmpty();
        samples.Should().BeInAscendingOrder();
        samples.Last().Should().Be(bytes.Length);
    }

    [Fact]
    public async Task CopyAsync_can_be_cancelled_mid_copy_and_cleans_up_partial()
    {
        // Big enough that the inner read/write loop will tick at least twice
        // before we cancel — small files complete in a single pass and you
        // can't cancel them.
        var bytes = new byte[8 * 1024 * 1024];
        Random.Shared.NextBytes(bytes);
        var src = TempPath("big.bin");
        await File.WriteAllBytesAsync(src, bytes);

        var dst = TempPath("big.copy");
        using var cts = new CancellationTokenSource();

        // Fire the cancel after the first chunk has already been written, so
        // we hit the in-flight cancellation path rather than the pre-start path.
        var progress = new Progress<long>(_ => cts.Cancel());

        var r = await FileOpsComponent.CopyAsync(src, dst, progress, ct: cts.Token);

        r.Success.Should().BeFalse();
        r.Error.Should().Be("Cancelled");
        File.Exists(dst).Should().BeFalse("partial destination must be removed on cancel");
    }

    [Fact]
    public async Task MoveAsync_same_volume_uses_atomic_rename_and_drops_source()
    {
        var src = await WriteFileAsync("src.txt", "moveme");
        var dst = TempPath("moved.txt");

        var r = await FileOpsComponent.MoveAsync(src, dst);

        r.Success.Should().BeTrue(r.Error);
        File.Exists(src).Should().BeFalse();
        File.ReadAllText(dst).Should().Be("moveme");
    }

    [Fact]
    public async Task MoveAsync_overwrite_replaces_existing_destination_file()
    {
        var src = await WriteFileAsync("src.txt", "new");
        var dst = await WriteFileAsync("dst.txt", "old");

        var r = await FileOpsComponent.MoveAsync(src, dst, overwrite: true);

        r.Success.Should().BeTrue(r.Error);
        File.ReadAllText(dst).Should().Be("new");
        File.Exists(src).Should().BeFalse();
    }

    [Fact]
    public async Task MoveAsync_overwrite_replaces_existing_destination_directory()
    {
        await WriteFileAsync(@"src\a.txt", "new");
        await WriteFileAsync(@"dst\old.txt", "existing");

        var r = await FileOpsComponent.MoveAsync(TempPath("src"), TempPath("dst"), overwrite: true);

        r.Success.Should().BeTrue(r.Error);
        Directory.Exists(TempPath("src")).Should().BeFalse();
        File.Exists(TempPath(@"dst\a.txt")).Should().BeTrue();
        File.Exists(TempPath(@"dst\old.txt")).Should().BeFalse();
    }

    [Fact]
    public void Rename_changes_the_leaf_name_only()
    {
        var src = TempPath("before.txt");
        File.WriteAllText(src, "x");

        var r = FileOpsComponent.Rename(src, "after.txt");

        r.Success.Should().BeTrue(r.Error);
        File.Exists(src).Should().BeFalse();
        File.Exists(TempPath("after.txt")).Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("bad/name.txt")]
    [InlineData("bad:name.txt")]
    public void Rename_rejects_invalid_or_empty_name(string newName)
    {
        var src = TempPath("file.txt");
        File.WriteAllText(src, "x");

        var r = FileOpsComponent.Rename(src, newName);

        r.Success.Should().BeFalse();
    }

    [Fact]
    public void Rename_to_existing_sibling_fails_without_clobbering()
    {
        var src = TempPath("a.txt"); File.WriteAllText(src, "a");
        var dst = TempPath("b.txt"); File.WriteAllText(dst, "b");

        var r = FileOpsComponent.Rename(src, "b.txt");

        r.Success.Should().BeFalse();
        File.ReadAllText(src).Should().Be("a");
        File.ReadAllText(dst).Should().Be("b");
    }

    [Fact]
    public void CreateDirectory_creates_the_named_subfolder()
    {
        var r = FileOpsComponent.CreateDirectory(TempDir, "newdir");

        r.Success.Should().BeTrue(r.Error);
        Directory.Exists(TempPath("newdir")).Should().BeTrue();
    }

    [Fact]
    public void CreateDirectory_fails_when_an_item_with_that_name_already_exists()
    {
        Directory.CreateDirectory(TempPath("dup"));

        var r = FileOpsComponent.CreateDirectory(TempDir, "dup");

        r.Success.Should().BeFalse();
    }

    [Fact]
    public void DeletePermanent_removes_a_file()
    {
        var path = TempPath("doomed.txt");
        File.WriteAllText(path, "x");

        var r = FileOpsComponent.DeletePermanent(path);

        r.Success.Should().BeTrue(r.Error);
        File.Exists(path).Should().BeFalse();
    }

    [Fact]
    public async Task DeletePermanent_recursively_removes_a_populated_directory()
    {
        await WriteFileAsync(@"sub\a.txt", "1");
        await WriteFileAsync(@"sub\nested\b.txt", "2");

        var r = FileOpsComponent.DeletePermanent(TempPath("sub"));

        r.Success.Should().BeTrue(r.Error);
        Directory.Exists(TempPath("sub")).Should().BeFalse();
    }

    [Fact]
    public void DeletePermanent_returns_failure_when_target_missing()
    {
        var r = FileOpsComponent.DeletePermanent(TempPath("does-not-exist"));

        r.Success.Should().BeFalse();
    }
}
