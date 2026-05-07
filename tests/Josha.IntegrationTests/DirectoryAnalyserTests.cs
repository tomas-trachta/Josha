using FluentAssertions;
using Josha.Business;
using Josha.IntegrationTests.Fixtures;
using Josha.Models;
using Xunit;

namespace Josha.IntegrationTests;

// Drives ScanCore.DeepScan + DirOD.GetDirSize() against a synthetic directory
// tree on disk. Validates tree shape, on-disk sizes (in decimal kilobytes,
// per Josha's KB convention), and that one-level-only ScanOneLevel + lazy
// re-entry produces the same tree as a single DeepScan.
public sealed class DirectoryAnalyserTests : TempDirTestBase
{
    [Fact]
    public async Task DeepScan_builds_a_DirOD_tree_that_matches_the_on_disk_layout()
    {
        // Layout:
        //   <tmp>/
        //     a.txt           (1000 bytes → 1 KB by Josha's /1000 convention)
        //     sub/
        //       b.txt         (2000 bytes → 2 KB)
        //       nested/
        //         c.txt       (4000 bytes → 4 KB)
        await File.WriteAllBytesAsync(TempPath("a.txt"),                       new byte[1000]);
        Directory.CreateDirectory(TempPath("sub"));
        await File.WriteAllBytesAsync(TempPath(@"sub\b.txt"),                  new byte[2000]);
        Directory.CreateDirectory(TempPath(@"sub\nested"));
        await File.WriteAllBytesAsync(TempPath(@"sub\nested\c.txt"),           new byte[4000]);

        var root = new DirOD(Path.GetFileName(TempDir), TempDir);
        ScanCore.DeepScan(root, default, progress: null, level: 0);
        root.GetDirSize();

        root.IsScanned.Should().BeTrue();
        root.Files.Select(f => f.Name).Should().BeEquivalentTo(new[] { "a.txt" });
        root.Subdirectories.Select(d => d.Name).Should().BeEquivalentTo(new[] { "sub" });

        var sub = root.Subdirectories.Single(d => d.Name == "sub");
        sub.Files.Select(f => f.Name).Should().BeEquivalentTo(new[] { "b.txt" });
        sub.Subdirectories.Select(d => d.Name).Should().BeEquivalentTo(new[] { "nested" });

        var nested = sub.Subdirectories.Single();
        nested.Files.Single().Name.Should().Be("c.txt");
        nested.Files.Single().SizeKiloBytes.Should().Be(4); // 4000/1000
        nested.SizeKiloBytes.Should().Be(4);

        sub.SizeKiloBytes.Should().Be(6);                  // 2 + 4
        root.SizeKiloBytes.Should().Be(7);                 // 1 + 2 + 4
    }

    [Fact]
    public void DeepScan_on_an_empty_directory_yields_a_scanned_node_with_no_children()
    {
        var root = new DirOD(Path.GetFileName(TempDir), TempDir);

        ScanCore.DeepScan(root, default, null, level: 0);

        root.IsScanned.Should().BeTrue();
        root.Subdirectories.Should().BeEmpty();
        root.Files.Should().BeEmpty();
    }

    [Fact]
    public void DeepScan_on_a_nonexistent_path_marks_the_node_scanned_with_empty_children()
    {
        // The scan must not throw — UI relies on IsScanned=true even for
        // unreadable / vanished paths so EnsureScanned() doesn't loop.
        var bogus = new DirOD("ghost", TempPath("nope"));

        ScanCore.DeepScan(bogus, default, null, level: 0);

        bogus.IsScanned.Should().BeTrue();
        bogus.Subdirectories.Should().BeEmpty();
        bogus.Files.Should().BeEmpty();
    }

    [Fact]
    public async Task ScanOneLevel_does_not_recurse()
    {
        await File.WriteAllBytesAsync(TempPath("a.txt"), new byte[100]);
        Directory.CreateDirectory(TempPath("sub"));
        await File.WriteAllBytesAsync(TempPath(@"sub\b.txt"), new byte[100]);

        var root = new DirOD(Path.GetFileName(TempDir), TempDir);
        ScanCore.ScanOneLevel(root);

        root.IsScanned.Should().BeTrue();
        root.Subdirectories.Should().HaveCount(1);

        // The subdirectory was *enumerated* (it appears as a child), but its
        // own contents are not yet scanned — the lazy-expansion contract.
        var sub = root.Subdirectories.Single();
        sub.IsScanned.Should().BeFalse();
        sub.Files.Should().BeEmpty();
    }

    [Fact]
    public async Task DeepScan_reports_per_directory_and_per_file_progress_counters()
    {
        await File.WriteAllBytesAsync(TempPath("a.txt"), new byte[10]);
        Directory.CreateDirectory(TempPath("sub"));
        await File.WriteAllBytesAsync(TempPath(@"sub\b.txt"), new byte[10]);
        await File.WriteAllBytesAsync(TempPath(@"sub\c.txt"), new byte[10]);

        var root = new DirOD(Path.GetFileName(TempDir), TempDir);
        var progress = new ScanCore.ScanProgress();
        ScanCore.DeepScan(root, default, progress, level: 0);

        progress.Directories.Should().Be(2); // root + sub
        progress.Files.Should().Be(3);       // a.txt + b.txt + c.txt
    }

    [Fact]
    public async Task DeepScan_respects_cancellation()
    {
        // Build a wider tree so cancellation has somewhere to bite — a single
        // tiny dir scans in microseconds and the token never gets a chance.
        for (int i = 0; i < 100; i++)
        {
            var d = TempPath($"d{i}");
            Directory.CreateDirectory(d);
            await File.WriteAllBytesAsync(Path.Combine(d, "f.txt"), new byte[10]);
        }

        var root = new DirOD(Path.GetFileName(TempDir), TempDir);
        using var cts = new CancellationTokenSource();
        cts.Cancel(); // cancel up front — DeepScan must bail without throwing

        var act = () => ScanCore.DeepScan(root, cts.Token, null, level: 0);

        // ParallelForEach surfaces cancellation as OperationCanceledException;
        // the outer handler in DirectoryAnalyserComponent.Run guards the
        // GetDirSize call. We just assert here that the scanner doesn't go
        // off and process the whole tree on a cancelled token.
        try { act(); } catch (OperationCanceledException) { }

        // Either we exited early (root not scanned) OR we scanned root then
        // bailed before processing all 100 children — both are valid. Just
        // assert we didn't end up with a fully populated tree.
        var fullyScanned = root.IsScanned
            && root.Subdirectories.Length == 100
            && root.Subdirectories.All(s => s.IsScanned);
        fullyScanned.Should().BeFalse();
    }
}
