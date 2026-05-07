using FluentAssertions;
using Josha.Business;
using Josha.IntegrationTests.Fixtures;
using Xunit;

namespace Josha.IntegrationTests;

// FileAnalyserComponent is a thin "swallow-everything" wrapper around File.*
// — the value of testing it is locking in that contract: it MUST NOT throw,
// and it MUST return a sentinel (false / empty bytes / silent no-op) on
// failure, because callers in the persistence layer rely on that to decide
// whether to BackupAndIsolate.
public sealed class FileAnalyserTests : TempDirTestBase
{
    [Fact]
    public async Task FileExists_returns_true_for_a_real_file()
    {
        var path = await WriteFileAsync("a.txt", "x");

        FileAnalyserComponent.FileExists(path).Should().BeTrue();
    }

    [Fact]
    public void FileExists_returns_false_for_a_missing_path_without_throwing()
    {
        FileAnalyserComponent.FileExists(TempPath("nope.txt")).Should().BeFalse();
    }

    [Fact]
    public async Task ReadFile_returns_the_file_bytes()
    {
        var path = await WriteFileAsync("a.bin", "");
        await File.WriteAllBytesAsync(path, new byte[] { 1, 2, 3, 4 });

        FileAnalyserComponent.ReadFile(path).Should().Equal(1, 2, 3, 4);
    }

    [Fact]
    public void ReadFile_on_missing_path_returns_empty_array_not_an_exception()
    {
        // Persistence layers depend on this contract — they distinguish a
        // missing file from a corrupt one by length, not by exception.
        FileAnalyserComponent.ReadFile(TempPath("nope.bin")).Should().BeEmpty();
    }

    [Fact]
    public void WriteFile_creates_the_file_with_the_given_bytes()
    {
        var path = TempPath("out.bin");

        FileAnalyserComponent.WriteFile(path, new byte[] { 9, 8, 7 });

        File.ReadAllBytes(path).Should().Equal(9, 8, 7);
    }
}
