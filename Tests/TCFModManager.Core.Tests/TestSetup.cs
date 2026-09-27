using System.Diagnostics;
using System.Runtime.CompilerServices;
using TCFModManager.Core.Services;

namespace TCFModManager.Core.Tests;

//
// Runs once, when the test assembly loads - before any test.
//
// The in-use guard (ModInstallService.EnsureInstallNotInUse) looks for SPT's game and server among
// the machine's real processes. The tests install into temporary folders nothing runs from, so what
// the guard finds says nothing about them - but a game or server whose path Windows will not show
// (run as administrator, say) counts as blocking every install, and failed every test that disables,
// enables or applies a list while SPT was open. The tests see no processes; the containment rule the
// guard is built on is tested on its own (InstallInUseScopeTests).
//
internal static class TestSetup
{
#pragma warning disable CA2255 // A module initializer in a test assembly is what this is for.
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void NothingRunning() => ModInstallService.FindProcesses = _ => Array.Empty<Process>();
}
