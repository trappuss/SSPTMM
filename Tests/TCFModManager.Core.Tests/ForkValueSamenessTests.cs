using Xunit;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;

namespace TCFModManager.Core.Tests;

public class ForkValueSamenessTests
{
    private static InstalledMod Entry(string version = "1.0.0", string dll = "a.dll") => new()
    {
        Name = "Mod",
        Version = version,
        Target = InstalledModTarget.Client,
        FolderPath = @"C:\spt\BepInEx\plugins\Mod",
        Guids = ["com.a", "com.b"],
        Assemblies = [new ModAssembly(dll, "A", "1.0.0.0", 10)],
    };

    [Fact]
    public void Two_entries_read_from_the_same_folder_are_the_same()
    {
        Assert.True(ValueSameness.Same(Entry(), Entry()));
        Assert.True(ValueSameness.Same(new List<InstalledMod> { Entry() }, new List<InstalledMod> { Entry() }));
    }

    [Fact]
    public void A_changed_version_or_file_is_not_the_same()
    {
        Assert.False(ValueSameness.Same(Entry(), Entry(version: "1.0.1")));
        Assert.False(ValueSameness.Same(Entry(), Entry(dll: "b.dll")));
        Assert.False(ValueSameness.Same(new List<InstalledMod> { Entry() }, new List<InstalledMod> { Entry(), Entry() }));
    }

    [Fact]
    public void Tuples_and_arrays_are_read_through()
    {
        (string, object?[])? one = ("key", ["1.0", 2]);
        (string, object?[])? same = ("key", ["1.0", 2]);
        (string, object?[])? other = ("key", ["1.1", 2]);

        Assert.True(ValueSameness.Same(one, same));
        Assert.False(ValueSameness.Same(one, other));
        Assert.False(ValueSameness.Same(one, null));
        Assert.True(ValueSameness.Same(null, null));
    }

    [Fact]
    public void Dates_answer_for_themselves()
    {
        DateTimeOffset? at = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
        Assert.True(ValueSameness.Same(at, (DateTimeOffset?)new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero)));
        Assert.False(ValueSameness.Same(at, (DateTimeOffset?)at.Value.AddSeconds(1)));
    }

    [Fact]
    public void Set_once_properties_are_the_init_ones()
    {
        var names = ValueSameness.SetOnce(typeof(InstalledMod)).Select(p => p.Name).ToList();

        Assert.Contains("Version", names);
        Assert.Contains("Assemblies", names);
        Assert.DoesNotContain("AllGuids", names);
    }
}
