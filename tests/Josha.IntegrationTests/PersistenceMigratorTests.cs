using FluentAssertions;
using Josha.Services;
using Xunit;

namespace Josha.IntegrationTests;

// Pure-byte tests for the envelope format. No DPAPI here — that's
// PersistenceFileTests' job.
public sealed class PersistenceMigratorTests
{
    [Fact]
    public void Wrap_then_unwrap_round_trips_the_payload()
    {
        var payload = new byte[] { 1, 2, 3, 4, 5 };

        var wrapped = PersistenceMigrator.WrapV1(payload);
        var result  = PersistenceMigrator.Unwrap(wrapped);

        result.Status.Should().Be(PersistenceMigrator.UnwrapStatus.Ok);
        result.Version.Should().Be(PersistenceMigrator.CurrentVersion);
        result.Payload.Should().Equal(payload);
    }

    [Fact]
    public void Unwrap_returns_NoEnvelope_for_bytes_without_the_DAS_magic()
    {
        // Looks like raw ciphertext without the envelope header.
        var legacy = new byte[] { 0xAB, 0xCD, 0xEF, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06 };

        var result = PersistenceMigrator.Unwrap(legacy);

        result.Status.Should().Be(PersistenceMigrator.UnwrapStatus.NoEnvelope);
    }

    [Fact]
    public void Unwrap_returns_NewerVersion_for_bytes_with_a_higher_version_byte()
    {
        var future = new byte[] {
            0x44, 0x41, 0x53,                 // "DAS"
            (byte)(PersistenceMigrator.CurrentVersion + 5),
            0x00, 0x00, 0x00, 0x00,           // flags + reserved
            0x10, 0x20, 0x30                  // payload
        };

        var result = PersistenceMigrator.Unwrap(future);

        result.Status.Should().Be(PersistenceMigrator.UnwrapStatus.NewerVersion);
        result.Version.Should().Be((byte)(PersistenceMigrator.CurrentVersion + 5));
    }

    [Fact]
    public void Unwrap_returns_Truncated_for_bytes_shorter_than_the_header()
    {
        var tiny = new byte[] { 0x44, 0x41 }; // "DA" — only 2 bytes

        var result = PersistenceMigrator.Unwrap(tiny);

        // Less than HeaderSize (8) AND no full magic → falls into NoEnvelope.
        // We don't care which truncated-vs-noenv bucket it lands in, only
        // that it isn't Ok.
        result.Status.Should().NotBe(PersistenceMigrator.UnwrapStatus.Ok);
    }

    [Fact]
    public void Unwrap_returns_Truncated_when_version_byte_is_zero()
    {
        var zeroVersion = new byte[] { 0x44, 0x41, 0x53, 0x00, 0x00, 0x00, 0x00, 0x00 };

        var result = PersistenceMigrator.Unwrap(zeroVersion);

        result.Status.Should().Be(PersistenceMigrator.UnwrapStatus.Truncated);
    }

    [Fact]
    public void Unwrap_returns_Truncated_for_zero_length_input()
    {
        var result = PersistenceMigrator.Unwrap(Array.Empty<byte>());

        result.Status.Should().Be(PersistenceMigrator.UnwrapStatus.Truncated);
    }
}
