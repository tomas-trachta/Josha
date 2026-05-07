using FluentAssertions;
using Josha.Business;
using Josha.IntegrationTests.Fixtures;
using Josha.Models;
using Xunit;

namespace Josha.IntegrationTests;

[Collection("Persistence")]
public sealed class SettingsComponentTests : PersistenceTestBase
{
    [Fact]
    public void Save_then_Load_round_trips_all_fields()
    {
        var saved = new AppSettings
        {
            EditorPath             = @"C:\Program Files\Editor\edit.exe",
            Theme                  = "Light",
            ConfirmDeletePermanent = false,
            DefaultViewMode        = "Tiles",
            FontScale              = 1.25,
        };

        SettingsComponent.Save(saved);
        var loaded = SettingsComponent.Load();

        loaded.EditorPath.Should().Be(saved.EditorPath);
        loaded.Theme.Should().Be(saved.Theme);
        loaded.ConfirmDeletePermanent.Should().Be(saved.ConfirmDeletePermanent);
        loaded.DefaultViewMode.Should().Be(saved.DefaultViewMode);
        loaded.FontScale.Should().Be(saved.FontScale);
    }

    [Fact]
    public void Load_returns_default_AppSettings_when_no_file_exists()
    {
        var loaded = SettingsComponent.Load();

        loaded.Theme.Should().Be("Dark");
        loaded.ConfirmDeletePermanent.Should().BeTrue();
        loaded.DefaultViewMode.Should().Be("List");
        loaded.FontScale.Should().Be(1.0);
        loaded.EditorPath.Should().BeEmpty();
    }

    [Fact]
    public void Load_falls_back_to_defaults_when_the_settings_file_is_corrupt()
    {
        // The Load contract is "never throw" — corrupt file → defaults so the
        // app still starts.
        Directory.CreateDirectory(DataDir);
        File.WriteAllText(Path.Combine(DataDir, "settings.json"), "{ this is not json");

        var loaded = SettingsComponent.Load();

        loaded.Theme.Should().Be("Dark");
    }

    [Fact]
    public void Save_creates_the_data_directory_if_it_doesnt_exist()
    {
        Directory.Exists(DataDir).Should().BeFalse();

        SettingsComponent.Save(new AppSettings { Theme = "Light" });

        File.Exists(Path.Combine(DataDir, "settings.json")).Should().BeTrue();
    }

    [Fact]
    public void Settings_file_is_saved_as_plain_JSON_not_encrypted()
    {
        // Settings carry no secrets; storing as plain JSON is intentional so
        // users can hand-edit if needed.
        SettingsComponent.Save(new AppSettings { Theme = "Light" });

        var contents = File.ReadAllText(Path.Combine(DataDir, "settings.json"));
        contents.Should().Contain("\"Theme\"").And.Contain("\"Light\"");
    }
}
