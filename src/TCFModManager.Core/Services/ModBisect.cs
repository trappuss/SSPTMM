namespace TCFModManager.Core.Services;

//
// Fork (1.4.7): Find the broken mod - which of a list of mods makes something fail, found by trying
// with only some of them on, as git bisect finds a commit.
//
// The mods are numbered in an order where each comes after what it needs. A run of them from the
// start ("the first k") is then always a set that can load, and the search is for the shortest such
// run that fails: the mod that ends it is the one. It is then tried with only what it needs; when
// that works, the fault takes a second mod, found the same way among the ones before it.
//
// Known before it starts, and never tried here: none of them on works, all of them on fails.
// Every set is tried once - an answer is remembered - so a fault that comes and goes cannot make
// the search run for ever; it ends on whatever the answers given add up to.
//
public sealed class ModBisect
{
    private enum Phase
    {
        Run,
        Alone,
        Partner,
        Done,
    }

    private readonly int _count;
    private readonly Func<int, IEnumerable<int>> _needs;
    private readonly Dictionary<string, bool> _answers = new(StringComparer.Ordinal);

    private Phase _phase = Phase.Run;
    private int _low;
    private int _high;
    private IReadOnlyList<int> _alone = [];
    private IReadOnlyList<int>? _next;

    /// <param name="count">How many mods, numbered 0 to count - 1, each after what it needs.</param>
    /// <param name="needs">The mods a mod cannot load without, by number - direct ones are enough.</param>
    public ModBisect(int count, Func<int, IEnumerable<int>>? needs = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);

        _count = count;
        _needs = needs ?? (_ => []);
        _high = count;
        Advance();
    }

    /// <summary>The mods to have on for the next try, in order; null once the search is over.</summary>
    public IReadOnlyList<int>? Next => _next;

    public bool IsDone => _phase == Phase.Done;

    /// <summary>The mod the fault comes with, once the search is over.</summary>
    public int? Culprit { get; private set; }

    /// <summary>The second mod the fault takes, when the first is fine with only what it needs.</summary>
    public int? Partner { get; private set; }

    /// <summary>About how many tries are left - one more may follow when the fault takes two mods.</summary>
    public int TriesLeft => _phase switch
    {
        Phase.Run => Halvings(_high - _low) + 1,
        Phase.Alone => 1,
        Phase.Partner => Math.Max(1, Halvings(_high - _low)),
        _ => 0,
    };

    /// <summary>What the try with <see cref="Next"/> on showed.</summary>
    public void Report(bool failed)
    {
        if (_next is null) throw new InvalidOperationException("the search is over");

        _answers[Key(_next)] = failed;
        Advance();
    }

    // Moves on through every step whose answer is already known, to the next set to try.
    private void Advance()
    {
        while (true)
        {
            switch (_phase)
            {
                case Phase.Run:
                    if (_high - _low <= 1)
                    {
                        Culprit = _high - 1;
                        _alone = WithNeeds(_high - 1);
                        _phase = Phase.Alone;
                        continue;
                    }

                    var middle = (_low + _high) / 2;
                    if (!Ask(First(middle), out var runFails)) return;
                    if (runFails) _high = middle;
                    else _low = middle;
                    continue;

                case Phase.Alone:
                    // The first k when the mod needs every one before it: already answered.
                    if (_alone.Count == Culprit + 1 && _alone[^1] == Culprit) { Finish(); continue; }
                    if (!Ask(_alone, out var aloneFails)) return;
                    if (aloneFails) { Finish(); continue; }

                    // Fine by itself: with the first j of the others it works for j = 0 and fails
                    // for every one before it.
                    _low = 0;
                    _high = Culprit!.Value;
                    _phase = Phase.Partner;
                    continue;

                case Phase.Partner:
                    if (_high - _low <= 1)
                    {
                        Partner = _high - 1;
                        Finish();
                        continue;
                    }

                    var half = (_low + _high) / 2;
                    if (!Ask(Merge(First(half), _alone), out var pairFails)) return;
                    if (pairFails) _high = half;
                    else _low = half;
                    continue;

                default:
                    _next = null;
                    return;
            }
        }
    }

    private void Finish() => _phase = Phase.Done;

    // True with the answer when this set was tried before; otherwise it becomes the next to try.
    private bool Ask(IReadOnlyList<int> set, out bool failed)
    {
        if (_answers.TryGetValue(Key(set), out failed)) return true;

        _next = set;
        return false;
    }

    private static List<int> First(int count) => [.. Enumerable.Range(0, count)];

    private static List<int> Merge(IEnumerable<int> one, IEnumerable<int> other) => [.. one.Concat(other).Distinct().Order()];

    // The mod and everything it needs, and what those need.
    private List<int> WithNeeds(int mod)
    {
        var seen = new HashSet<int> { mod };
        var open = new Queue<int>([mod]);

        while (open.Count > 0)
            foreach (var needed in _needs(open.Dequeue()))
                if (needed >= 0 && needed < _count && seen.Add(needed)) open.Enqueue(needed);

        return [.. seen.Order()];
    }

    private static string Key(IEnumerable<int> set) => string.Join(',', set);

    private static int Halvings(int span)
    {
        var tries = 0;
        while (span > 1)
        {
            span = (span + 1) / 2;
            tries++;
        }

        return tries;
    }
}
