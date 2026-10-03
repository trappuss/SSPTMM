using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

// Fork: ReuploadCheck, and the listed-size note beside kept archives (ModArchiveCache).
public sealed class ForkReuploadTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "reupload-" + Guid.NewGuid().ToString("N"));

    public ForkReuploadTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static readonly DateTimeOffset Day = new(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);

    private static InstalledModRecord Record(int versionId, string version = "3.1.1", long? listed = null, bool isAddon = false) => new()
    {
        ModId = 2383,
        IsAddon = isAddon,
        Name = "Skills Extended",
        VersionId = versionId,
        Version = version,
        InstalledAt = Day.AddDays(1),
        ListedBytes = listed,
    };

    private static ModVersionSummary Entry(int id, string version, long? size, DateTimeOffset? made) =>
        new() { Id = id, Version = version, ContentLength = size, CreatedAt = made };

    [Fact]
    public void Nothing_is_flagged_while_the_listing_is_as_it_was()
    {
        var versions = new[] { Entry(15733, "3.1.1", 109_594_837, Day), Entry(15692, "3.1.0", 109_231_453, Day.AddDays(-1)) };

        Assert.Null(ReuploadCheck.Find(Record(15733, listed: 109_594_837), versions));
        Assert.Null(ReuploadCheck.Find(Record(15733), versions)); // a record from before sizes were kept
    }

    [Fact]
    public void A_double_submit_of_the_same_file_is_not_a_reupload()
    {
        // ODT's Item Info 2.0.15: two entries a minute apart, 42,510 bytes each.
        var versions = new[] { Entry(14750, "2.0.15", 42_510, Day), Entry(14751, "2.0.15", 42_510, Day.AddMinutes(1)) };

        Assert.Null(ReuploadCheck.Find(Record(14750, "2.0.15"), versions));
    }

    [Fact]
    public void A_later_entry_of_another_size_under_the_same_number_is_a_reupload()
    {
        var versions = new[] { Entry(500, "1.0.0", 1_000, Day), Entry(501, "1.0.0", 1_200, Day.AddHours(3)) };

        var found = ReuploadCheck.Find(Record(500, "1.0.0"), versions);

        Assert.NotNull(found);
        Assert.Equal(ReuploadKind.NewEntry, found.Kind);
        Assert.Equal(501, found.Version.Id);
        Assert.Equal(1_000, found.Before);
        Assert.Equal(1_200, found.Now);
    }

    [Fact]
    public void Later_means_created_later_not_a_higher_id()
    {
        // Accurate Circular Radar 1.1.10: id 942 was created after id 1156.
        var versions = new[] { Entry(1156, "1.1.10", 2_000, Day), Entry(942, "1.1.10", 2_500, Day.AddDays(13)) };

        Assert.Equal(942, ReuploadCheck.Find(Record(1156, "1.1.10"), versions)?.Version.Id);
        Assert.Null(ReuploadCheck.Find(Record(942, "1.1.10"), versions));
    }

    [Fact]
    public void The_installed_entry_listed_at_another_size_since_the_install_is_a_reupload()
    {
        var versions = new[] { Entry(700, "2.2.3", 5_800_000, Day) };

        var found = ReuploadCheck.Find(Record(700, "2.2.3", listed: 5_764_291), versions);

        Assert.Equal(ReuploadKind.ListingChanged, found?.Kind);
        Assert.Equal(5_764_291, found?.Before);
    }

    [Fact]
    public void Addons_hand_installed_records_and_versions_not_in_the_catalog_are_left_alone()
    {
        var versions = new[] { Entry(700, "2.2.3", 5_800_000, Day) };

        Assert.Null(ReuploadCheck.Find(Record(700, "2.2.3", listed: 1, isAddon: true), versions));
        Assert.Null(ReuploadCheck.Find(new InstalledModRecord
        {
            ModId = 1, Name = "x", VersionId = 700, Version = "2.2.3", InstalledAt = Day, IsAppManaged = false, ListedBytes = 1,
        }, versions));
        Assert.Null(ReuploadCheck.Find(Record(650, "2.2.2", listed: 1), versions));
        Assert.Null(ReuploadCheck.Find(Record(700, "2.2.3", listed: 1), null));
    }

    // The cache's note: a kept archive is judged by the listing it was downloaded under.

    private static readonly InstallTarget Target = new(2383, IsAddon: false, "Skills Extended", null, null, null);

    private static ModVersion Version(long? listed) => new() { Id = 15733, Version = "3.1.1", ContentLength = listed };

    [Fact]
    public void A_kept_archive_served_at_another_size_than_listed_is_used_while_the_listing_is_unchanged()
    {
        var cache = new ModArchiveCache(_dir, long.MaxValue);
        var path = cache.PathFor(Target, Version(109_594_837))!;
        File.WriteAllBytes(path, new byte[46]); // stands in for the file as served
        ModArchiveCache.NoteListed(path, 109_594_837);

        Assert.True(cache.TryGet(Target, Version(109_594_837), out var kept));
        Assert.Equal(path, kept);
    }

    [Fact]
    public void A_kept_archive_whose_listing_changed_is_deleted_with_its_note()
    {
        var cache = new ModArchiveCache(_dir, long.MaxValue);
        var path = cache.PathFor(Target, Version(46))!;
        File.WriteAllBytes(path, new byte[46]);
        ModArchiveCache.NoteListed(path, 46);

        Assert.False(cache.TryGet(Target, Version(50), out _));
        Assert.False(File.Exists(path));
        Assert.Empty(Directory.GetFiles(_dir));
    }

    [Fact]
    public void Clearing_the_kept_archives_takes_their_notes_too()
    {
        var cache = new ModArchiveCache(_dir, long.MaxValue);
        var path = cache.PathFor(Target, Version(46))!;
        File.WriteAllBytes(path, new byte[46]);
        ModArchiveCache.NoteListed(path, 46);

        cache.Clear(new HashSet<string>());

        Assert.Empty(Directory.GetFiles(_dir).Select(Path.GetFileName));
    }

    [Fact]
    public void Trimming_deletes_as_many_kept_archives_as_it_takes()
    {
        // Before the fix the tidy ended after its first delete (a deleted FileInfo's Length throws).
        var cache = new ModArchiveCache(_dir, 15);
        for (var id = 1; id <= 3; id++)
        {
            var path = cache.PathFor(Target, new ModVersion { Id = id, Version = "1" })!;
            File.WriteAllBytes(path, new byte[10]);
            File.SetLastWriteTimeUtc(path, new DateTime(2026, 1, id, 0, 0, 0, DateTimeKind.Utc));
        }

        cache.Trim(new HashSet<string>());

        Assert.Equal(["mod-2383-3.archive"], Directory.GetFiles(_dir).Select(Path.GetFileName));
    }
}
