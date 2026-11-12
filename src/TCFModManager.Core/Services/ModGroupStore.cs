using System.Text.Json;
using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

//
// Loads/saves ModGroupData as JSON under <app folder>\Data\mod_groups.json - the groups on
// Subscribed items (its Groups view, grouping, Add to group) and which installed mod (by name) sits
// in which one. Purely organizational: installing and removing mods never read it. A corrupt or
// hand-edited file falls back to an empty set rather than blocking the page.
//
public sealed class ModGroupStore
{
    private readonly string _filePath;

    public ModGroupStore()
        : this(Path.Combine(AppPaths.DataDirectory, "mod_groups.json"))
    {
    }

    // A file of its own - for the tests.
    public ModGroupStore(string filePath) => _filePath = filePath;

    public ModGroupData Load()
    {
        if (!File.Exists(_filePath)) return new ModGroupData();

        try
        {
            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<ModGroupData>(json) ?? new ModGroupData();
        }
        catch (JsonException)
        {
            SafeFile.PreserveDamaged(_filePath);
            return new ModGroupData();
        }
    }

    public void Save(ModGroupData data)
    {
        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
        SafeFile.WriteText(_filePath, json, keepBackups: true);
    }

    // The key an installed mod is tracked under - InstalledModCardViewModel.Name, lowercased so
    // lookups don't care about case.
    public static string KeyFor(string modName) => modName.Trim().ToLowerInvariant();

    // Adds a new group at the end of the sort order and returns it - or, when a group already has
    // that name (case aside), returns that one instead of making a second of the same name.
    public ModGroup AddGroup(string name)
    {
        var data = Load();
        name = name.Trim();
        if (data.Groups.FirstOrDefault(g => string.Equals(g.Name.Trim(), name, StringComparison.OrdinalIgnoreCase)) is { } existing)
            return existing;

        var group = new ModGroup
        {
            Id = Guid.NewGuid(),
            Name = name,
            SortOrder = data.Groups.Count == 0 ? 0 : data.Groups.Max(g => g.SortOrder) + 1,
        };

        data.Groups.Add(group);
        Save(data);
        return group;
    }

    public void RenameGroup(Guid groupId, string name)
    {
        var data = Load();
        var group = data.Groups.FirstOrDefault(g => g.Id == groupId);
        if (group is null) return;

        group.Name = name;
        Save(data);
    }

    // The other group already called this (case and surrounding spaces aside), if any.
    public ModGroup? NamedLike(string name, Guid except) =>
        Load().Groups.FirstOrDefault(g => g.Id != except
            && string.Equals(g.Name.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase));

    // Fork (1.4.3): one group folded into another - its mods move over and it goes. Renaming a
    // group to a name another already has offers this, rather than leaving two of one name.
    public void MergeGroup(Guid fromId, Guid intoId)
    {
        var data = Load();
        if (fromId == intoId || data.Groups.All(g => g.Id != intoId)) return;

        foreach (var key in data.Assignments.Where(kvp => kvp.Value == fromId).Select(kvp => kvp.Key).ToList())
            data.Assignments[key] = intoId;
        data.Groups.RemoveAll(g => g.Id == fromId);
        Save(data);
    }

    // Removes the group; any mod assigned to it falls back to Ungrouped.
    public void DeleteGroup(Guid groupId)
    {
        var data = Load();
        data.Groups.RemoveAll(g => g.Id == groupId);
        foreach (var key in data.Assignments.Where(kvp => kvp.Value == groupId).Select(kvp => kvp.Key).ToList())
            data.Assignments.Remove(key);

        Save(data);
    }

    // Fork (1.4.3): the group's colour, "#RRGGBB" or null for none (see ModColors).
    public void SetColor(Guid groupId, string? color)
    {
        var data = Load();
        var group = data.Groups.FirstOrDefault(g => g.Id == groupId);
        if (group is null) return;

        group.Color = ModColors.Normalize(color);
        Save(data);
    }

    public void SetCollapsed(Guid groupId, bool collapsed)
    {
        var data = Load();
        var group = data.Groups.FirstOrDefault(g => g.Id == groupId);
        if (group is null || group.IsCollapsed == collapsed) return;

        group.IsCollapsed = collapsed;
        Save(data);
    }

    // Swaps this group's SortOrder with its neighbor in the given direction. No-op at either end.
    public void Move(Guid groupId, int direction)
    {
        var data = Load();
        var ordered = data.Groups.OrderBy(g => g.SortOrder).ToList();
        var index = ordered.FindIndex(g => g.Id == groupId);
        var swapWith = index + direction;
        if (index < 0 || swapWith < 0 || swapWith >= ordered.Count) return;

        (ordered[index].SortOrder, ordered[swapWith].SortOrder) = (ordered[swapWith].SortOrder, ordered[index].SortOrder);
        Save(data);
    }

    // Swaps two groups' places in the manual order, whatever lies between them. No-op unless both exist.
    public void SwapOrder(Guid first, Guid second)
    {
        var data = Load();
        var a = data.Groups.FirstOrDefault(g => g.Id == first);
        var b = data.Groups.FirstOrDefault(g => g.Id == second);
        if (a is null || b is null || ReferenceEquals(a, b)) return;

        (a.SortOrder, b.SortOrder) = (b.SortOrder, a.SortOrder);
        Save(data);
    }

    // Assigns modName to groupId, or clears its assignment (back to Ungrouped) when groupId is null.
    public void AssignMod(string modName, Guid? groupId) => AssignMods([modName], groupId);

    // The same for several mods at once (several picked and dragged, or "Add to group" on them):
    // one read and one write, so the file never holds half of them moved.
    public void AssignMods(IEnumerable<string> modNames, Guid? groupId)
    {
        var data = Load();

        foreach (var key in modNames.Select(KeyFor).Distinct())
        {
            if (groupId is null)
                data.Assignments.Remove(key);
            else
                data.Assignments[key] = groupId.Value;
        }

        Save(data);
    }
}
