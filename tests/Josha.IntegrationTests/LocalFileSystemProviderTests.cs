using FluentAssertions;
using Josha.Business;
using Josha.IntegrationTests.Fixtures;
using System.Text;
using Xunit;

namespace Josha.IntegrationTests;

// LocalFileSystemProvider's mutation methods just delegate to FileOpsComponent
// (covered by LocalFileOpsTests), so this class focuses on the surface that
// only lives on the provider: Enumerate, OpenRead/OpenWrite, and the cross-
// provider ImportFromAsync.
public sealed class LocalFileSystemProviderTests : TempDirTestBase
{
    private LocalFileSystemProvider Sut => LocalFileSystemProvider.Instance;

    [Fact]
    public async Task EnumerateAsync_returns_files_and_dirs_with_metadata()
    {
        await WriteFileAsync("a.txt", "hello");      // 5 bytes
        Directory.CreateDirectory(TempPath("sub"));

        var entries = await Sut.EnumerateAsync(TempDir);

        entries.Should().HaveCount(2);
        entries.Should().Contain(e => e.Name == "a.txt"
            && !e.IsDirectory && e.Size == 5
            && e.FullPath == TempPath("a.txt"));
        entries.Should().Contain(e => e.Name == "sub"
            && e.IsDirectory && e.Size == null);
    }

    [Fact]
    public async Task EnumerateAsync_on_a_missing_path_returns_an_empty_list_not_an_exception()
    {
        var entries = await Sut.EnumerateAsync(TempPath("does-not-exist"));

        entries.Should().BeEmpty();
    }

    [Fact]
    public async Task OpenReadAsync_returns_the_file_contents()
    {
        var path = await WriteFileAsync("payload.txt", "hello-world");

        await using var s = await Sut.OpenReadAsync(path);
        using var sr = new StreamReader(s);

        (await sr.ReadToEndAsync()).Should().Be("hello-world");
    }

    [Fact]
    public async Task OpenWriteAsync_with_overwrite_true_replaces_existing_content()
    {
        var path = await WriteFileAsync("doc.txt", "old");

        await using (var s = await Sut.OpenWriteAsync(path, overwrite: true))
            await s.WriteAsync(Encoding.UTF8.GetBytes("new"));

        File.ReadAllText(path).Should().Be("new");
    }

    [Fact]
    public async Task OpenWriteAsync_with_overwrite_false_throws_on_existing_file()
    {
        var path = await WriteFileAsync("doc.txt", "old");

        var act = async () => await Sut.OpenWriteAsync(path, overwrite: false);

        await act.Should().ThrowAsync<IOException>();
        File.ReadAllText(path).Should().Be("old");
    }

    [Fact]
    public async Task ImportFromAsync_local_to_local_falls_back_to_a_normal_copy()
    {
        var src = await WriteFileAsync("src.txt", "imported");
        var dst = TempPath("dst.txt");

        var r = await Sut.ImportFromAsync(Sut, src, dst, null, overwrite: false, default);

        r.Success.Should().BeTrue(r.Error);
        File.ReadAllText(dst).Should().Be("imported");
        File.ReadAllText(src).Should().Be("imported");
    }
}
