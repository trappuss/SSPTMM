using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

public sealed class ModArchiveCacheTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "archive-cache-" + Guid.NewGuid().ToString("N"));

    public ModArchiveCacheTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static readonly InstallTarget Mod = new(827, IsAddon: false, "Waypoints", null, null, null);
    private static readonly InstallTarget Addon = new(827, IsAddon: true, "An addon", null, null, null);

    private static ModVersion Version(int id, long? size = null) => new() { Id = id, Version = "1.0.0", ContentLength = size };

    private static void Write(string path, int bytes, DateTime? used = null)
    {
        File.WriteAllBytes(path, new byte[bytes]);
        if (used is { } at) File.SetLastWriteTimeUtc(path, at);
    }

    [Fact]
    public void A_version_without_an_id_is_not_kept()
    {
        var cache = new ModArchiveCache(_dir, 1000);

        Assert.Null(cache.PathFor(Mod, Version(0)));
        Assert.False(cache.TryGet(Mod, Version(0), out _));
    }

    [Fact]
    public void A_mod_and_an_addon_with_the_same_ids_are_kept_apart()
    {
        var cache = new ModArchiveCache(_dir, 1000);

        Assert.NotEqual(cache.PathFor(Mod, Version(5)), cache.PathFor(Addon, Version(5)));
    }

    [Fact]
    public void A_kept_archive_of_the_size_given_is_used()
    {
        var cache = new ModArchiveCache(_dir, 1000);
        Write(cache.PathFor(Mod, Version(5))!, 10);

        Assert.True(cache.TryGet(Mod, Version(5, size: 10), out var path));
        Assert.Equal(cache.PathFor(Mod, Version(5)), path);
    }

    [Fact]
    public void A_kept_archive_of_another_size_is_deleted_not_used()
    {
        var cache = new ModArchiveCache(_dir, 1000);
        var path = cache.PathFor(Mod, Version(5))!;
        Write(path, 10);

        Assert.False(cache.TryGet(Mod, Version(5, size: 11), out _));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void A_mismatched_archive_another_item_is_using_is_passed_over_not_deleted()
    {
        var cache = new ModArchiveCache(_dir, 1000);
        var path = cache.PathFor(Mod, Version(5))!;
        Write(path, 10);

        Assert.False(cache.TryGet(Mod, Version(5, size: 11), out _, new HashSet<string> { Path.GetFullPath(path) }));
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void An_unfinished_download_is_not_taken_for_a_kept_one()
    {
        var cache = new ModArchiveCache(_dir, 1000);
        Write(ModArchiveCache.PartPathFor(cache.PathFor(Mod, Version(5))!), 10);

        Assert.False(cache.TryGet(Mod, Version(5), out _));
    }

    [Fact]
    public void Trimming_deletes_the_ones_used_longest_ago_until_the_rest_fit()
    {
        var cache = new ModArchiveCache(_dir, 25);
        var oldest = cache.PathFor(Mod, Version(1))!;
        var middle = cache.PathFor(Mod, Version(2))!;
        var newest = cache.PathFor(Mod, Version(3))!;
        Write(oldest, 10, DateTime.UtcNow.AddDays(-3));
        Write(middle, 10, DateTime.UtcNow.AddDays(-2));
        Write(newest, 10, DateTime.UtcNow.AddDays(-1));

        cache.Trim(new HashSet<string>());

        Assert.False(File.Exists(oldest));
        Assert.True(File.Exists(middle));
        Assert.True(File.Exists(newest));
    }

    [Fact]
    public void Trimming_and_clearing_leave_archives_in_use_and_their_downloads_alone()
    {
        var cache = new ModArchiveCache(_dir, 0);
        var inUse = cache.PathFor(Mod, Version(1))!;
        var writing = cache.PathFor(Mod, Version(2))!;
        var leftover = ModArchiveCache.PartPathFor(cache.PathFor(Mod, Version(3))!);
        Write(inUse, 10);
        Write(ModArchiveCache.PartPathFor(writing), 10);
        Write(leftover, 10);

        cache.Clear(new HashSet<string> { inUse, writing });

        Assert.True(File.Exists(inUse));
        Assert.True(File.Exists(ModArchiveCache.PartPathFor(writing)));
        Assert.False(File.Exists(leftover));
    }

    [Fact]
    public void One_archive_larger_than_the_budget_goes_before_the_rest()
    {
        var cache = new ModArchiveCache(_dir, 25);
        var small = cache.PathFor(Mod, Version(1))!;
        var huge = cache.PathFor(Mod, Version(2))!;
        Write(small, 10, DateTime.UtcNow.AddDays(-2));
        Write(huge, 30, DateTime.UtcNow.AddDays(-1));

        Assert.False(cache.IsKeepable(huge));
        cache.Trim(new HashSet<string>());

        Assert.True(File.Exists(small));
        Assert.False(File.Exists(huge));
    }

    [Fact]
    public void One_off_downloads_are_not_kept_and_leftovers_go()
    {
        var cache = new ModArchiveCache(_dir, 1000);
        var once = cache.TemporaryPath();
        Write(once, 10);

        Assert.False(cache.IsKeepable(once));
        cache.Trim(new HashSet<string>());

        Assert.False(File.Exists(once));
    }
}
