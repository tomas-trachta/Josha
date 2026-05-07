using FluentAssertions;
using Josha.Business;
using Josha.IntegrationTests.Fixtures;
using Josha.Models;
using Xunit;

namespace Josha.IntegrationTests;

[Collection("Persistence")]
public sealed class BookmarkComponentTests : PersistenceTestBase
{
    [Fact]
    public void Save_then_Load_round_trips_the_bookmark_list()
    {
        var saved = new[]
        {
            new Bookmark("Home",      @"C:\Users\me"),
            new Bookmark("Downloads", @"D:\Downloads"),
            new Bookmark("Project",   @"\\server\share\proj"),
        };

        BookmarkComponent.Save(saved);
        var loaded = BookmarkComponent.Load();

        loaded.Should().BeEquivalentTo(saved);
    }

    [Fact]
    public void Load_returns_an_empty_list_when_no_file_exists_yet()
    {
        // Fresh data dir, never written.
        var loaded = BookmarkComponent.Load();

        loaded.Should().BeEmpty();
    }

    [Fact]
    public void Save_creates_the_data_directory_if_it_doesnt_exist()
    {
        Directory.Exists(DataDir).Should().BeFalse("precondition: data dir must not exist yet");

        BookmarkComponent.Save(new[] { new Bookmark("X", "Y") });

        Directory.Exists(DataDir).Should().BeTrue();
        File.Exists(Path.Combine(DataDir, "bookmarks.dans")).Should().BeTrue();
    }

    [Fact]
    public void Save_with_an_empty_list_overwrites_the_existing_file_with_an_empty_payload()
    {
        BookmarkComponent.Save(new[] { new Bookmark("Old", @"C:\old") });
        BookmarkComponent.Load().Should().HaveCount(1);

        BookmarkComponent.Save(Array.Empty<Bookmark>());

        BookmarkComponent.Load().Should().BeEmpty();
    }

    [Fact]
    public void Bookmark_names_with_tabs_get_sanitized_so_the_TSV_format_stays_parseable()
    {
        // The on-disk format is "Name\tTargetPath" — a literal tab in the name
        // would split the line wrong. Save replaces tabs with spaces.
        var bm = new Bookmark("Name\twith\ttabs", @"C:\target");

        BookmarkComponent.Save(new[] { bm });
        var loaded = BookmarkComponent.Load();

        loaded.Single().Name.Should().Be("Name with tabs");
        loaded.Single().TargetPath.Should().Be(@"C:\target");
    }
}
