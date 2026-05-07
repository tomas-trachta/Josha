using FluentAssertions;
using Josha.Business.Ftp;
using Josha.IntegrationTests.Fixtures;
using Josha.Models;
using Xunit;

namespace Josha.IntegrationTests;

// FTPS-explicit smoke tests focused on the three TlsValidation modes.
// We don't re-test all the file ops here — that's RemoteFileSystemProviderTests
// against SFTP, and the FluentFTP path shares the same provider. This class
// owns the trust-model surface specifically.
[Collection("Ftps")]
public sealed class FtpsClientTlsValidationTests
{
    private readonly FtpsServerFixture _fx;

    public FtpsClientTlsValidationTests(FtpsServerFixture fx) => _fx = fx;

    private FtpSite NewSite(TlsValidation mode, string? pinnedFingerprint = null) => new()
    {
        Name              = "test-ftps",
        Host              = _fx.Host,
        Port              = _fx.Port,
        Username          = FtpsServerFixture.User,
        Password          = FtpsServerFixture.Password,
        Protocol          = FtpProtocol.FtpsExplicit,
        Mode              = FtpMode.Passive,
        TlsValidation     = mode,
        PinnedFingerprint = pinnedFingerprint,
    };

    [Fact]
    public async Task AcceptAny_connects_against_self_signed_cert()
    {
        var client = new FtpClientComponent(NewSite(TlsValidation.AcceptAny));
        await using var _ = client;

        await client.ConnectAsync(CancellationToken.None);

        client.IsConnected.Should().BeTrue();
    }

    [Fact]
    public async Task AcceptOnFirstUse_pins_the_servers_fingerprint_on_first_connect()
    {
        var site = NewSite(TlsValidation.AcceptOnFirstUse);
        site.PinnedFingerprint.Should().BeNull("precondition: nothing pinned yet");

        var client = new FtpClientComponent(site);
        await using var _ = client;
        await client.ConnectAsync(CancellationToken.None);

        client.IsConnected.Should().BeTrue();
        site.PinnedFingerprint.Should().Be(_fx.FingerprintSha256,
            "TOFU mode must capture the exact fingerprint our self-signed cert produces");
    }

    [Fact]
    public async Task AcceptOnFirstUse_with_mismatched_pin_rejects_the_connect()
    {
        // Wrong (but valid-shape) fingerprint — same length, different bytes.
        var bogus = new string('A', _fx.FingerprintSha256.Length);
        var client = new FtpClientComponent(NewSite(TlsValidation.AcceptOnFirstUse, pinnedFingerprint: bogus));
        await using var _ = client;

        var act = async () => await client.ConnectAsync(CancellationToken.None);

        await act.Should().ThrowAsync<Exception>(
            "a fingerprint mismatch in TOFU mode must abort the TLS handshake");
        client.IsConnected.Should().BeFalse();
    }

    [Fact]
    public async Task Strict_with_correct_pinned_fingerprint_overrides_chain_failure()
    {
        // Strict normally fails on a self-signed cert (chain doesn't validate),
        // but a pre-pinned fingerprint matching the cert is the documented
        // override path — the test asserts that override actually fires.
        var client = new FtpClientComponent(NewSite(TlsValidation.Strict, pinnedFingerprint: _fx.FingerprintSha256));
        await using var _ = client;

        await client.ConnectAsync(CancellationToken.None);

        client.IsConnected.Should().BeTrue();
    }
}
