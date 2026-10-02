using System.Net;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using TCFModManager.Core.SpModApi;
using Xunit;

namespace TCFModManager.Core.Tests;

// 
// The addon-specific halves of install support. The one that matters most is identity: sp-mod.com
// numbers addons in their own sequence, so addon 116 and mod 116 are unrelated objects that would
// otherwise share a row in installed-mods.json.
// 
public class AddonSupportTests
{
    [Fact]
    public void InstallTarget_ForMod_IsNotAnAddon()
    {
        var target = InstallTarget.For(new Mod { Id = 116, Name = "Some Mod", Guid = "com.example.mod" });

        Assert.Equal(116, target.Id);
        Assert.False(target.IsAddon);
        Assert.Equal("com.example.mod", target.Guid);
    }

    [Fact]
    public void InstallTarget_ForAddon_IsAnAddonAndHasNoGuid()
    {
        var target = InstallTarget.For(new Addon { Id = 116, Name = "Some Addon", ModId = 2441 });

        Assert.Equal(116, target.Id);
        Assert.True(target.IsAddon);
        Assert.Null(target.Guid);
    }

    [Fact]
    public void InstallTarget_Matches_DistinguishesAnAddonFromAModWithTheSameId()
    {
        var modTarget = InstallTarget.For(new Mod { Id = 116, Name = "Some Mod" });
        var addonTarget = InstallTarget.For(new Addon { Id = 116, Name = "Some Addon" });

        var addonRecord = new InstalledModRecord
        {
            ModId = 116,
            IsAddon = true,
            Name = "Some Addon",
            Version = "1.0.0",
            InstalledAt = DateTimeOffset.UtcNow,
        };

        Assert.True(addonTarget.Matches(addonRecord));
        Assert.False(modTarget.Matches(addonRecord));
    }

    [Fact]
    public void Manifest_Find_NeverHandsBackTheOtherKindWithTheSameId()
    {
        // Mod 106 and addon 106 both exist on sp-mod.com. Removing one used to look its record up by
        // id alone and could delete the other's files.
        var addon = new InstalledModRecord
        {
            ModId = 106, IsAddon = true, Name = "Addon 106", Version = "1.0.0",
            InstalledAt = DateTimeOffset.UtcNow, Files = ["BepInEx/plugins/AddonFolder/a.dll"],
        };
        var mod = new InstalledModRecord
        {
            ModId = 106, IsAddon = false, Name = "Mod 106", Version = "2.0.0",
            InstalledAt = DateTimeOffset.UtcNow, Files = ["BepInEx/plugins/ModFolder/m.dll"],
        };

        var addonFirst = new ModInstallManifest { Mods = [addon, mod] };
        var modFirst = new ModInstallManifest { Mods = [mod, addon] };

        Assert.Same(mod, addonFirst.Find(106, isAddon: false));
        Assert.Same(addon, addonFirst.Find(106, isAddon: true));
        Assert.Same(mod, modFirst.Find(106, isAddon: false));
        Assert.Same(addon, modFirst.Find(106, isAddon: true));
        Assert.Null(addonFirst.Find(107, isAddon: false));
    }

    [Fact]
    public void InstalledModRecord_DefaultsToNotAnAddon()
    {
        // Every record written before addons were supported has no IsAddon field at all; the
        // default is what keeps those meaning exactly what they always meant.
        var record = new InstalledModRecord
        {
            ModId = 2441,
            Name = "Some Mod",
            Version = "1.0.0",
            InstalledAt = DateTimeOffset.UtcNow,
        };

        Assert.False(record.IsAddon);
    }

    // 
    // An addon version constrains against its parent MOD's version rather than an SPT version, in
    // the same syntax. These are the real constraint strings sp-mod.com returns.
    // 
    [Theory]
    [InlineData("^1.5.0", "1.5.0", true)]
    [InlineData("^1.5.0", "1.5.2", true)]
    [InlineData("^1.5.0", "1.9.9", true)]
    [InlineData("^1.5.0", "1.4.9", false)]
    [InlineData("^1.5.0", "2.0.0", false)]
    [InlineData("1.7.0", "1.7.0", true)]
    [InlineData("1.7.0", "1.7.1", false)]
    [InlineData("~1.5.0", "1.5.4", true)]
    [InlineData("~1.5.0", "1.6.0", false)]
    [InlineData(">=1.5.3", "1.5.1", false)]
    [InlineData(">=1.5.3", "1.5.3", true)]
    public void ModVersionConstraint_IsReadLiterally(string constraint, string parentVersion, bool expected)
    {
        Assert.Equal(expected, ModVersionMatcher.IsSatisfiedBy(constraint, parentVersion));
    }

    [Fact]
    public void ModVersionConstraint_WithNoParentInstalled_IsUndecidable()
    {
        // Null rather than false: "the parent isn't installed" is a different answer from
        // "the parent is installed and doesn't fit", and the UI says so differently.
        Assert.Null(ModVersionMatcher.IsSatisfiedBy("^1.5.0", null));
    }

    [Fact]
    public async Task GetAddonsAsync_DeserializesTheEmbeddedVersionsWithTheirDownloadLinks()
    {
        // Captured from a live GET /api/v0/addons?include=versions on 2026-08-30. The embedded
        // version objects carry link and content_length, which is what lets an addon install
        // straight from the cached catalog with no per-addon lookup.
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, AddonsListFixture);
        using var client = new SpModApiClient(new HttpClient(handler));

        var result = await client.GetAddonsAsync(new AddonsQuery { Include = "versions" });

        var addon = Assert.Single(result.Data);
        Assert.Equal(115, addon.Id);
        Assert.Equal("RaidReviewOverlay", addon.Name);
        Assert.Equal(1479, addon.ModId);
        Assert.False(addon.IsDetached);
        Assert.Equal("maschine", addon.Owner?.Name);

        var version = Assert.Single(addon.Versions!);
        Assert.Equal("1.0.1", version.Version);
        Assert.Equal("^1.5.0", version.ModVersionConstraint);
        Assert.Equal("https://sp-mod.com/addon/download/115/raidreviewoverlay/1.0.1", version.Link);
        Assert.Equal(18103, version.ContentLength);
    }

    [Fact]
    public void AddonsQuery_FiltersByParentMod()
    {
        var query = new AddonsQuery { FilterModId = "1479", Include = "versions" }.ToParameters().ToList();

        Assert.Contains(query, p => p is { Key: "filter[mod_id]", Value: "1479" });
        Assert.Contains(query, p => p is { Key: "include", Value: "versions" });
    }

    private const string AddonsListFixture = """
        {"success":true,"data":[{"id":115,"name":"RaidReviewOverlay","slug":"raidreviewoverlay",
        "teaser":"Opens Raid Review's web interface in a window over the game instead of a browser tab",
        "thumbnail":"https://files.sp-mod.com/addons/V8sCNfEsiVH3hYni03ynz74C149iy2FyAaPCjmtk.png",
        "downloads":77,"detail_url":"https://sp-mod.com/addon/115/raidreviewoverlay",
        "contains_ads":false,"contains_ai_content":true,"mod_id":1479,"is_detached":false,
        "detached_at":null,"published_at":"2026-08-24T13:10:00.000000Z",
        "created_at":"2026-08-24T13:00:58.000000Z","updated_at":"2026-08-28T15:35:46.000000Z",
        "owner":{"id":109459,"name":"maschine"},"additional_authors":[],
        "versions":[{"id":252,"version":"1.0.1",
        "link":"https://sp-mod.com/addon/download/115/raidreviewoverlay/1.0.1",
        "content_length":18103,"mod_version_constraint":"^1.5.0","downloads":27,
        "published_at":"2026-08-28T15:30:00.000000Z"}]}],
        "links":{},"meta":{"current_page":1,"last_page":1,"total":1}}
        """;
}

// 
// The manifest and the addon cache both write into Data\ next to the test assembly, so these run
// serially against each other rather than in parallel.
// 
[Collection("addon-data-files")]
public class AddonManifestFileTests
{
    [Fact]
    public void SetManualVersion_KeepsAModAndAnAddonWithTheSameIdApart()
    {
        var manifestPath = Path.Combine(AppPaths.DataDirectory, "installed-mods.json");
        var backup = File.Exists(manifestPath) ? File.ReadAllText(manifestPath) : null;

        try
        {
            File.Delete(manifestPath);
            var service = new ModInstallManifestService();

            service.SetManualVersion(116, "com.example.mod", "Some Mod", "2.0.0", versionId: 1, folders: ["SomeMod"]);
            service.SetManualVersion(116, null, "Some Addon", "1.0.1", versionId: 2, folders: ["SomeAddon"], isAddon: true);

            var records = service.Load().Mods;
            Assert.Equal(2, records.Count);
            Assert.Equal("2.0.0", records.Single(r => r is { ModId: 116, IsAddon: false }).Version);
            Assert.Equal("1.0.1", records.Single(r => r is { ModId: 116, IsAddon: true }).Version);

            // Clearing one leaves the other exactly as it was.
            service.ClearManualVersion(116, isAddon: true);

            var remaining = Assert.Single(service.Load().Mods);
            Assert.False(remaining.IsAddon);
            Assert.Equal("2.0.0", remaining.Version);
        }
        finally
        {
            if (backup is null) File.Delete(manifestPath);
            else File.WriteAllText(manifestPath, backup);
        }
    }

    [Fact]
    public void AddonCacheStore_RoundTripsTheCatalog()
    {
        var cachePath = Path.Combine(AppPaths.DataDirectory, "addon_cache.json");
        var backup = File.Exists(cachePath) ? File.ReadAllText(cachePath) : null;

        try
        {
            var store = new AddonCacheStore();
            store.Save([
                new Addon
                {
                    Id = 115,
                    Name = "RaidReviewOverlay",
                    ModId = 1479,
                    Versions =
                    [
                        new AddonVersionSummary
                        {
                            Id = 252,
                            Version = "1.0.1",
                            ModVersionConstraint = "^1.5.0",
                            Link = "https://sp-mod.com/addon/download/115/raidreviewoverlay/1.0.1",
                            ContentLength = 18103,
                        },
                    ],
                },
            ]);

            var loaded = store.Load();

            Assert.NotNull(loaded);
            var addon = Assert.Single(loaded!.Addons);
            Assert.Equal(1479, addon.ModId);
            Assert.Equal("^1.5.0", Assert.Single(addon.Versions!).ModVersionConstraint);
            Assert.Equal(18103, addon.Versions![0].ContentLength);
        }
        finally
        {
            if (backup is null) File.Delete(cachePath);
            else File.WriteAllText(cachePath, backup);
        }
    }

    [Fact]
    public void AddonCacheStore_TreatsACorruptFileAsNoCache()
    {
        var cachePath = Path.Combine(AppPaths.DataDirectory, "addon_cache.json");
        var backup = File.Exists(cachePath) ? File.ReadAllText(cachePath) : null;

        try
        {
            File.WriteAllText(cachePath, "{ this is not json");
            Assert.Null(new AddonCacheStore().Load());
        }
        finally
        {
            if (backup is null) File.Delete(cachePath);
            else File.WriteAllText(cachePath, backup);
        }
    }
}
