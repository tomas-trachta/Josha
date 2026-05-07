using FluentAssertions;
using Josha.Business;
using Josha.IntegrationTests.Fixtures;
using Josha.Models;
using Xunit;

namespace Josha.IntegrationTests;

[Collection("Persistence")]
public sealed class SnapshotComponentTests : PersistenceTestBase
{
    private static DirOD MakeTree()
    {
        var nested = new DirOD("nested", @"C:\root\sub\nested")
        {
            Files = new[] { new FileOD("c.txt", 4) },
            IsScanned = true,
        };
        var sub = new DirOD("sub", @"C:\root\sub")
        {
            Subdirectories = new[] { nested },
            Files = new[] { new FileOD("b.txt", 2) },
            IsScanned = true,
        };
        var root = new DirOD("root", @"C:\root")
        {
            Subdirectories = new[] { sub },
            Files = new[] { new FileOD("a.txt", 1) },
            IsScanned = true,
        };
        root.GetDirSize(); // populate SizeKiloBytes throughout
        return root;
    }

    [Fact]
    public void Save_then_Load_round_trips_a_DirOD_tree()
    {
        var saved = MakeTree();

        SnapshotComponent.SaveSnapshot("D", saved);
        var loaded = SnapshotComponent.LoadSnapshot("D");

        loaded.Should().NotBeNull();
        loaded!.Name.Should().Be(saved.Name);
        loaded.SizeKiloBytes.Should().Be(saved.SizeKiloBytes);

        var loadedSub = loaded.Subdirectories.Single();
        loadedSub.Name.Should().Be("sub");
        loadedSub.Files.Single().Name.Should().Be("b.txt");
        loadedSub.Files.Single().SizeKiloBytes.Should().Be(2);

        loadedSub.Subdirectories.Single().Name.Should().Be("nested");
        loadedSub.Subdirectories.Single().Files.Single().Name.Should().Be("c.txt");
    }

    [Fact]
    public void Snapshots_are_namespaced_per_drive_letter()
    {
        var c = new DirOD("C-root", @"C:\");
        var d = new DirOD("D-root", @"D:\");

        SnapshotComponent.SaveSnapshot("C", c);
        SnapshotComponent.SaveSnapshot("D", d);

        File.Exists(Path.Combine(DataDir, "tree.C.daps")).Should().BeTrue();
        File.Exists(Path.Combine(DataDir, "tree.D.daps")).Should().BeTrue();

        SnapshotComponent.LoadSnapshot("C")!.Name.Should().Be("C-root");
        SnapshotComponent.LoadSnapshot("D")!.Name.Should().Be("D-root");
    }

    [Fact]
    public void LoadSnapshot_returns_null_when_no_snapshot_exists_for_that_drive()
    {
        SnapshotComponent.LoadSnapshot("Z").Should().BeNull();
    }

    [Fact]
    public void SnapshotExists_reports_false_for_an_unwritten_drive_and_true_after_save()
    {
        SnapshotComponent.SnapshotExists("E").Should().BeFalse();

        SnapshotComponent.SaveSnapshot("E", new DirOD("e", @"E:\"));

        SnapshotComponent.SnapshotExists("E").Should().BeTrue();
    }

    [Fact]
    public void MigrateLegacyOnStartup_renames_the_old_tree_daps_to_tree_C_daps()
    {
        Directory.CreateDirectory(DataDir);
        var legacy = Path.Combine(DataDir, "tree.daps");
        File.WriteAllBytes(legacy, new byte[] { 1, 2, 3 });

        SnapshotComponent.MigrateLegacyOnStartup();

        File.Exists(legacy).Should().BeFalse();
        File.Exists(Path.Combine(DataDir, "tree.C.daps")).Should().BeTrue();
    }

    [Fact]
    public void MigrateLegacyOnStartup_does_not_clobber_an_existing_tree_C_daps()
    {
        Directory.CreateDirectory(DataDir);
        var legacy = Path.Combine(DataDir, "tree.daps");
        var newC   = Path.Combine(DataDir, "tree.C.daps");
        File.WriteAllBytes(legacy, new byte[] { 1, 1, 1 });
        File.WriteAllBytes(newC,   new byte[] { 9, 9, 9 });

        SnapshotComponent.MigrateLegacyOnStartup();

        // Both files preserved — migration is a no-op when the new file already exists.
        File.Exists(legacy).Should().BeTrue();
        File.ReadAllBytes(newC).Should().Equal(9, 9, 9);
    }
}
