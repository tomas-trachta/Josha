using FluentAssertions;
using Josha.Business;
using Josha.Models;
using Xunit;

namespace Josha.IntegrationTests;

// SearchComponent walks a DirOD tree and matches names case-insensitively.
// No I/O — feed it a synthetic tree, assert the hit list.
public sealed class SearchComponentTests
{
    private static DirOD MakeDir(string name, string path,
        IEnumerable<DirOD>? subs = null,
        IEnumerable<FileOD>? files = null)
        => new(name, path)
        {
            Subdirectories = subs?.ToArray() ?? Array.Empty<DirOD>(),
            Files          = files?.ToArray() ?? Array.Empty<FileOD>(),
        };

    private static FileOD MakeFile(string name, decimal sizeKb = 1)
        => new(name, sizeKb);

    [Fact]
    public void Search_finds_a_file_by_partial_name()
    {
        var root = MakeDir("root", @"C:\root", files: new[]
        {
            MakeFile("budget.xlsx"),
            MakeFile("notes.txt"),
        });
        var sut = new SearchComponent();

        sut.Search("budg", root);

        sut.SearchResults.Should().ContainSingle()
            .Which.Name.Should().Be("budget.xlsx");
    }

    [Fact]
    public void Search_is_case_insensitive()
    {
        var root = MakeDir("root", @"C:\root", files: new[] { MakeFile("README.md") });
        var sut = new SearchComponent();

        sut.Search("readme", root);

        sut.SearchResults.Should().ContainSingle();
    }

    [Fact]
    public void Search_descends_into_subdirectories()
    {
        var leaf = MakeDir("leaf", @"C:\root\sub\leaf", files: new[] { MakeFile("target.dat") });
        var sub  = MakeDir("sub",  @"C:\root\sub", subs: new[] { leaf });
        var root = MakeDir("root", @"C:\root", subs: new[] { sub });
        var sut  = new SearchComponent();

        sut.Search("target", root);

        sut.SearchResults.Should().ContainSingle()
            .Which.Name.Should().Be("target.dat");
    }

    [Fact]
    public void Search_records_directory_hits_when_the_directory_name_matches()
    {
        var docs = MakeDir("docs", @"C:\root\docs");
        var root = MakeDir("root", @"C:\root", subs: new[] { docs });
        var sut  = new SearchComponent();

        sut.Search("docs", root);

        sut.SearchResults.Should().ContainSingle()
            .Which.ResultType.Should().Be(SearchResultType.Directory);
    }

    [Fact]
    public void Search_caps_results_at_50_to_keep_the_UI_responsive()
    {
        var files = Enumerable.Range(0, 200).Select(i => MakeFile($"hit-{i:000}.txt"));
        var root  = MakeDir("root", @"C:\root", files: files);
        var sut   = new SearchComponent();

        sut.Search("hit", root);

        sut.SearchResults.Should().HaveCount(50);
    }

    [Fact]
    public void Search_returns_nothing_when_nothing_matches()
    {
        var root = MakeDir("root", @"C:\root", files: new[] { MakeFile("a.txt") });
        var sut  = new SearchComponent();

        sut.Search("zzz-no-match", root);

        sut.SearchResults.Should().BeEmpty();
    }
}
