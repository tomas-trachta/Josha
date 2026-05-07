using FluentAssertions;
using Josha.Business;
using Josha.IntegrationTests.Fixtures;
using Josha.Models;
using Xunit;

namespace Josha.IntegrationTests;

[Collection("Persistence")]
public sealed class SiteManagerComponentTests : PersistenceTestBase
{
    private static FtpSite NewSite(string name) => new()
    {
        Id            = Guid.NewGuid(),
        Name          = name,
        Host          = $"{name}.example.com",
        Port          = 21,
        Username      = $"user-{name}",
        Password      = $"pw-{name}",
        Protocol      = FtpProtocol.FtpsExplicit,
        Mode          = FtpMode.Passive,
        TlsValidation = TlsValidation.AcceptOnFirstUse,
    };

    [Fact]
    public void Save_then_Load_round_trips_a_site_list()
    {
        var sites = new List<FtpSite> { NewSite("alpha"), NewSite("bravo") };

        SiteManagerComponent.Save(sites);
        var loaded = SiteManagerComponent.Load();

        loaded.Should().HaveCount(2);
        loaded.Select(s => s.Name).Should().BeEquivalentTo(new[] { "alpha", "bravo" });
        loaded.Single(s => s.Name == "alpha").Password.Should().Be("pw-alpha");
    }

    [Fact]
    public void Upsert_inserts_a_new_site_when_the_id_is_unknown()
    {
        SiteManagerComponent.Save(new[] { NewSite("alpha") });

        SiteManagerComponent.Upsert(NewSite("bravo"));

        SiteManagerComponent.Load().Select(s => s.Name)
            .Should().BeEquivalentTo(new[] { "alpha", "bravo" });
    }

    [Fact]
    public void Upsert_updates_an_existing_site_when_the_id_matches()
    {
        var existing = NewSite("alpha");
        SiteManagerComponent.Save(new[] { existing });

        existing.Host = "renamed.example.com";
        existing.Password = "new-secret";
        SiteManagerComponent.Upsert(existing);

        var loaded = SiteManagerComponent.Load();
        loaded.Should().HaveCount(1, "Upsert with same Id must replace, not add");
        loaded.Single().Host.Should().Be("renamed.example.com");
        loaded.Single().Password.Should().Be("new-secret");
    }

    [Fact]
    public void Delete_removes_the_site_with_the_given_id()
    {
        var keep   = NewSite("keep");
        var purge  = NewSite("purge");
        SiteManagerComponent.Save(new[] { keep, purge });

        SiteManagerComponent.Delete(purge.Id);

        SiteManagerComponent.Load().Should().ContainSingle()
            .Which.Id.Should().Be(keep.Id);
    }

    [Fact]
    public void Delete_with_an_unknown_id_is_a_silent_no_op()
    {
        SiteManagerComponent.Save(new[] { NewSite("alpha") });

        SiteManagerComponent.Delete(Guid.NewGuid()); // not present

        SiteManagerComponent.Load().Should().HaveCount(1);
    }

    [Fact]
    public void Saved_password_is_not_present_as_plaintext_on_disk()
    {
        var site = NewSite("alpha");
        site.Password = "ZZTopSecret123!";
        SiteManagerComponent.Save(new[] { site });

        var raw = File.ReadAllBytes(Path.Combine(DataDir, "sites.dans"));
        System.Text.Encoding.UTF8.GetString(raw).Should().NotContain("ZZTopSecret123!",
            "DPAPI ciphertext must not contain the plaintext password bytes");
    }

    [Fact]
    public void Load_on_a_fresh_data_dir_returns_an_empty_list()
    {
        SiteManagerComponent.Load().Should().BeEmpty();
    }
}
