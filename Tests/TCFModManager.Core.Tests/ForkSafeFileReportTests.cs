using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

//
// The fork's one addition to SafeFile: a damaged file kept aside is reported (Problems and
// ProblemFound), so the app can say so instead of leaving it to the log - once per copy kept.
//
public class ForkSafeFileReportTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "tcfmm-safefile-" + Guid.NewGuid().ToString("N"));

    public ForkSafeFileReportTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { }

        GC.SuppressFinalize(this);
    }

    private IReadOnlyList<SafeFileProblem> Mine =>
        [.. SafeFile.Problems.Where(p => p.Path.StartsWith(_dir, StringComparison.OrdinalIgnoreCase))];

    [Fact]
    public void ADamagedFileKeptAside_IsReportedOnce()
    {
        var file = Path.Combine(_dir, "settings.json");
        File.WriteAllText(file, "{ not json");

        var raised = new List<SafeFileProblem>();
        void OnFound(object? sender, SafeFileProblem problem)
        {
            if (problem.Path.StartsWith(_dir, StringComparison.OrdinalIgnoreCase)) raised.Add(problem);
        }

        SafeFile.ProblemFound += OnFound;
        try
        {
            var copy = SafeFile.PreserveDamaged(file);

            // The same damaged file read again keeps the same copy - and is not reported again.
            Assert.Equal(copy, SafeFile.PreserveDamaged(file));

            var problem = Assert.Single(Mine);
            Assert.Equal(Path.GetFullPath(file), problem.Path);
            Assert.Equal(copy, problem.KeptAs);
            Assert.Single(raised);
        }
        finally
        {
            SafeFile.ProblemFound -= OnFound;
        }
    }

    [Fact]
    public void NoFile_IsNotAProblem()
    {
        Assert.Null(SafeFile.PreserveDamaged(Path.Combine(_dir, "missing.json")));
        Assert.Empty(Mine);
    }
}
