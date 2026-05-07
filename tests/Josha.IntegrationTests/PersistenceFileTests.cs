using FluentAssertions;
using Josha.Business;
using Josha.IntegrationTests.Fixtures;
using System.Text;
using Xunit;

namespace Josha.IntegrationTests;

// Drives the DPAPI envelope round-trip and the BackupAndIsolate paths in
// PersistenceFile. DPAPI is per-user-per-machine, so these tests run on
// Windows under whatever account the test runner is using and they
// encrypt/decrypt as that user. No mocks.
[Collection("Persistence")]
public sealed class PersistenceFileTests : PersistenceTestBase
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Josha/tests/v1");
    private const string LogCat = "Tests";

    private string DataPath(string name) => Path.Combine(DataDir, name);

    [Fact]
    public void SaveEncrypted_then_LoadDecrypted_round_trips_a_string()
    {
        Directory.CreateDirectory(DataDir);
        var path = DataPath("roundtrip.dans");

        PersistenceFile.SaveEncrypted(path, "hello-dpapi", Entropy, LogCat);

        File.Exists(path).Should().BeTrue();
        PersistenceFile.LoadDecrypted(path, Entropy, LogCat).Should().Be("hello-dpapi");
    }

    [Fact]
    public void SaveEncrypted_does_not_write_plaintext_to_disk()
    {
        Directory.CreateDirectory(DataDir);
        var path = DataPath("ciphertext.dans");

        PersistenceFile.SaveEncrypted(path, "secret-payload-xyz", Entropy, LogCat);

        var raw = File.ReadAllBytes(path);
        Encoding.UTF8.GetString(raw).Should().NotContain("secret-payload-xyz",
            "DPAPI ciphertext must not contain the plaintext bytes");
    }

    [Fact]
    public void LoadDecrypted_with_wrong_entropy_isolates_the_file_to_a_bak()
    {
        Directory.CreateDirectory(DataDir);
        var path = DataPath("entropy.dans");
        PersistenceFile.SaveEncrypted(path, "with-entropy-A", Entropy, LogCat);

        // Try to read with a different entropy → unprotect throws → BackupAndIsolate.
        var wrongEntropy = Encoding.UTF8.GetBytes("Josha/tests/different");
        var result = PersistenceFile.LoadDecrypted(path, wrongEntropy, LogCat);

        result.Should().BeEmpty();
        File.Exists(path).Should().BeFalse("the unreadable file must be moved aside, not left in place");
        Directory.GetFiles(DataDir, "entropy.dans.dpapi-failed-*.bak").Should().NotBeEmpty(
            "the original ciphertext must be preserved as <name>.dpapi-failed-*.bak so it isn't clobbered by the next save");
    }

    [Fact]
    public void LoadDecrypted_with_pre_envelope_bytes_isolates_them_as_legacy_format()
    {
        Directory.CreateDirectory(DataDir);
        var path = DataPath("legacy.dans");

        // Looks like raw pre-envelope ciphertext: no "DAS" magic.
        File.WriteAllBytes(path, new byte[] { 0xCA, 0xFE, 0xBA, 0xBE, 0x01, 0x02, 0x03 });

        var result = PersistenceFile.LoadDecrypted(path, Entropy, LogCat);

        result.Should().BeEmpty();
        File.Exists(path).Should().BeFalse();
        Directory.GetFiles(DataDir, "legacy.dans.legacy-format-*.bak").Should().NotBeEmpty();
    }

    [Fact]
    public void LoadDecrypted_with_a_newer_version_envelope_isolates_as_newer_version()
    {
        Directory.CreateDirectory(DataDir);
        var path = DataPath("future.dans");

        // "DAS" + version byte one above current.
        var future = new byte[] {
            0x44, 0x41, 0x53,
            (byte)(Josha.Services.PersistenceMigrator.CurrentVersion + 1),
            0, 0, 0, 0,
            0x10, 0x20, 0x30
        };
        File.WriteAllBytes(path, future);

        var result = PersistenceFile.LoadDecrypted(path, Entropy, LogCat);

        result.Should().BeEmpty();
        Directory.GetFiles(DataDir, "future.dans.newer-version-*.bak").Should().NotBeEmpty();
    }

    [Fact]
    public void LoadDecrypted_on_a_missing_file_returns_empty_without_throwing()
    {
        // Persistence layers expect "no file yet" to be a normal first-run state,
        // not an error. The contract is empty-string + no exception.
        var result = PersistenceFile.LoadDecrypted(DataPath("does-not-exist.dans"), Entropy, LogCat);

        result.Should().BeEmpty();
    }
}
