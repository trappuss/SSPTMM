using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace TCFModManager.Core.Services;

//
// Fork (1.4.9): whether two values read the same all the way down - lists item by item, tuples part
// by part, this app's own classes property by property. For telling that what a fresh scan built is
// what is already on screen, so the page keeps it rather than drawing it again.
//
// Errs towards "different": anything too deep to follow, or of a kind it does not know and that
// does not say it is equal, is not the same.
//
public static class ValueSameness
{
    private const int MaxDepth = 8;

    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> Readable = new();

    public static bool Same(object? a, object? b) => Same(a, b, 0);

    private static bool Same(object? a, object? b, int depth)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null) return false;

        var type = a.GetType();
        if (type != b.GetType()) return false;
        if (a is string || type.IsPrimitive || type.IsEnum) return a.Equals(b);
        if (depth >= MaxDepth) return false;

        if (a is ITuple left)
        {
            var right = (ITuple)b;
            if (left.Length != right.Length) return false;
            for (var i = 0; i < left.Length; i++)
                if (!Same(left[i], right[i], depth + 1)) return false;
            return true;
        }

        if (a is IEnumerable first)
        {
            var one = first.GetEnumerator();
            var other = ((IEnumerable)b).GetEnumerator();
            while (true)
            {
                var more = one.MoveNext();
                if (more != other.MoveNext()) return false;
                if (!more) return true;
                if (!Same(one.Current, other.Current, depth + 1)) return false;
            }
        }

        if (!IsOurs(type)) return a.Equals(b);

        foreach (var property in Readable.GetOrAdd(type, PropertiesOf))
            if (!Same(property.GetValue(a), property.GetValue(b), depth + 1)) return false;
        return true;
    }

    // The app's own types are followed into; anything else (a date, a Guid, a Version) answers for itself.
    private static bool IsOurs(Type type) =>
        type.Namespace?.StartsWith("TCFModManager", StringComparison.Ordinal) == true;

    private static PropertyInfo[] PropertiesOf(Type type) =>
        [.. type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)];

    /// <summary>The properties of a type that are set once, as it is made: those with an init or a
    /// non-public setter.</summary>
    public static PropertyInfo[] SetOnce(Type type) =>
        [.. type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(p => p.GetMethod is { IsPublic: true } && p.GetIndexParameters().Length == 0 && p.SetMethod is { } set
                && (!set.IsPublic || set.ReturnParameter.GetRequiredCustomModifiers().Contains(typeof(IsExternalInit))))];
}
