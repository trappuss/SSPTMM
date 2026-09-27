using System.Text.Json;
using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

//
// Loads/saves ModGroupData as JSON under <app folder>\Data\mod_groups.json - the Mod Groups
// window's separators and which installed mod (by name) sits in which one. Purely organizational;
// nothing here is read by the Installed page or by installing/removing mods. A corrupt or
// hand-edited file falls back to an empty set rather than blocking the window from opening.
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
        return SafeFile.ReadJson(_filePath, json => JsonSerializer.Deserialize<ModGroupData>(json)) ?? new ModGroupData();
    }

    public void Save(ModGroupData data)
    {
        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
        SafeFile.WriteAllText(_filePath, json, keepBackup: true);
    }

    // The key an installed mod is tracked under - InstalledModCardViewModel.Name, lowercased so
    // lookups don't care about case.
    public static string KeyFor(string modName) => modName.Trim().ToLowerInvariant();

    // Adds a new group at the end of the sort order and returns it.
    public ModGroup AddGroup(string name)
    {
        var data = Load();
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

    // Removes the group; any mod assigned to it falls back to Ungrouped.
    public void DeleteGroup(Guid groupId)
    {
        var data = Load();
        data.Groups.RemoveAll(g => g.Id == groupId);
        foreach (var key in data.Assignments.Where(kvp => kvp.Value == groupId).Select(kvp => kvp.Key).ToList())
            data.Assignments.Remove(key);

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
    public void AssignMod(string modName, Guid? groupId)
    {
        var data = Load();
        var key = KeyFor(modName);

        if (groupId is null)
            data.Assignments.Remove(key);
        else
            data.Assignments[key] = groupId.Value;

        Save(data);
    }
}
