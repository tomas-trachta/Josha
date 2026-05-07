using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Xunit;

namespace Josha.IntegrationTests.Fixtures;

// Pure-ftpd container with explicit FTPS (AUTH TLS) enabled. The fixture
// generates a fresh self-signed cert at startup, copies it into the container
// at the path pure-ftpd reads, and exposes the SHA-256 fingerprint that
// FtpClientComponent will compute from the same cert blob — so tests can
// assert against an exact pinned value.
//
// Passive ports are bound 1:1 (host == container) because the server's PASV
// reply advertises the *server* port; if the host mapping differed, the
// data channel would dial the wrong host port. That makes parallel runs of
// this fixture on the same host port-collide — we accept that since we run
// tests sequentially within the collection.
public sealed class FtpsServerFixture : IAsyncLifetime
{
    public const string User     = "tester";
    public const string Password = "secret";
    public const string HomeDir  = "/home/ftpusers/tester";
    private const int   PasvLow  = 30000;
    private const int   PasvHigh = 30009;

    private IContainer? _container;
    public string Host => _container?.Hostname ?? throw NotStarted();
    public ushort Port => _container?.GetMappedPublicPort(21) ?? throw NotStarted();

    // Uppercase hex SHA-256 of the server cert's DER bytes — matches what
    // FtpClientComponent.ComputeFingerprint produces.
    public string FingerprintSha256 { get; private set; } = "";

    public async Task InitializeAsync()
    {
        var (pem, fingerprint) = GenerateSelfSignedPem();
        FingerprintSha256 = fingerprint;

        var builder = new ContainerBuilder()
            .WithImage("stilliard/pure-ftpd")
            .WithEnvironment("PUBLICHOST",      "localhost")
            .WithEnvironment("FTP_USER_NAME",   User)
            .WithEnvironment("FTP_USER_PASS",   Password)
            .WithEnvironment("FTP_USER_HOME",   HomeDir)
            .WithEnvironment("FTP_PASSIVE_PORTS", $"{PasvLow}:{PasvHigh}")
            // --tls=1 = allow plain *and* TLS. Tests pick the protocol via
            // FtpProtocol on the FtpSite they construct.
            .WithEnvironment("ADDED_FLAGS",     "--tls=1")
            .WithResourceMapping(pem, "/etc/ssl/private/pure-ftpd.pem")
            .WithPortBinding(21, assignRandomHostPort: true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(21));

        // PASV ports must map host==container for the advertised port to be
        // reachable from the host-side FluentFTP client.
        for (var p = PasvLow; p <= PasvHigh; p++)
            builder = builder.WithPortBinding(p, p);

        _container = builder.Build();
        await _container.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
            await _container.DisposeAsync();
    }

    // RSA-2048 self-signed cert with CN=localhost + SAN. Returns (PEM bytes
    // for pure-ftpd, fingerprint string for tests). Combined PEM order is
    // private-key-first then certificate — pure-ftpd accepts either order
    // but the historic openssl convention is key-then-cert.
    private static (byte[] Pem, string Fingerprint) GenerateSelfSignedPem()
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest("CN=localhost", rsa,
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName("localhost");
        san.AddIpAddress(IPAddress.Loopback);
        req.CertificateExtensions.Add(san.Build());

        using var cert = req.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddYears(1));

        var sb = new StringBuilder();
        sb.AppendLine(new string(PemEncoding.Write("PRIVATE KEY", rsa.ExportPkcs8PrivateKey())));
        sb.AppendLine(new string(PemEncoding.Write("CERTIFICATE", cert.RawData)));

        var fp = Convert.ToHexString(SHA256.HashData(cert.RawData));
        return (Encoding.ASCII.GetBytes(sb.ToString()), fp);
    }

    private static InvalidOperationException NotStarted() =>
        new("FtpsServerFixture has not been initialised yet — InitializeAsync must run first.");
}

[CollectionDefinition("Ftps")]
public sealed class FtpsCollection : ICollectionFixture<FtpsServerFixture> { }
