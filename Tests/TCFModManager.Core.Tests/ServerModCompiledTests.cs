using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

//
// The metadata reader against server mods built by the real C# compiler, in Release and Debug,
// referencing SPT's types from a separate assembly the way a real mod does. What a mod writes that
// the reader cannot be sure of must come back as nothing, never as a wrong value.
//
public class ServerModCompiledTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tcf-compiled-" + Guid.NewGuid().ToString("N"));

    public ServerModCompiledTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    // SPT's own types, as far as a mod's metadata uses them (SPTarkov.Server.Core, SemanticVersioning).
    private const string SptStub = """
        namespace SemanticVersioning
        {
            public class Version { public Version(string v, bool loose = false) { } public Version(int a, int b, int c, string pre = null, string build = null) { } }
            public class Range { public Range(string r, bool loose = false) { } public static Range Parse(string r, bool loose = false) => new(r); }
        }
        namespace SPTarkov.Server.Core.Models.Spt.Mod
        {
            using System.Collections.Generic;
            public interface IModMetadata
            {
                string ModGuid { get; }
                string Name { get; }
                string Author { get; }
                SemanticVersioning.Version Version { get; }
                SemanticVersioning.Range SptVersion { get; }
                Dictionary<string, SemanticVersioning.Range> ModDependencies { get; }
            }
            public abstract record AbstractModMetadata
            {
                public abstract string ModGuid { get; init; }
                public abstract string Name { get; init; }
                public abstract string Author { get; init; }
                public abstract SemanticVersioning.Version Version { get; init; }
                public abstract SemanticVersioning.Range SptVersion { get; init; }
                public abstract Dictionary<string, SemanticVersioning.Range> ModDependencies { get; init; }
            }
        }
        """;

    private MetadataReference? _stub;

    private MetadataReference Stub()
    {
        if (_stub is not null) return _stub;

        var path = Path.Combine(_root, "SPTarkov.Server.Core.dll");
        Compile("SPTarkov.Server.Core", SptStub, path, OptimizationLevel.Release, []);
        return _stub = MetadataReference.CreateFromFile(path);
    }

    private static readonly MetadataReference[] Framework =
    [
        .. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Where(p => Path.GetFileName(p) is "System.Runtime.dll" or "System.Private.CoreLib.dll" or "System.Collections.dll" or "netstandard.dll")
            .Select(p => MetadataReference.CreateFromFile(p)),
    ];

    private static void Compile(string name, string source, string path, OptimizationLevel level, MetadataReference[] references)
    {
        var compilation = CSharpCompilation.Create(
            name,
            [CSharpSyntaxTree.ParseText(source)],
            [.. Framework, .. references],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: level, nullableContextOptions: NullableContextOptions.Disable));

        using var file = File.Create(path);
        var result = compilation.Emit(file);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
    }

    private ServerModMetadata? Build(string source, OptimizationLevel level)
    {
        var path = Path.Combine(_root, Guid.NewGuid().ToString("N") + ".dll");
        Compile("Mod" + Guid.NewGuid().ToString("N")[..6], "using System.Collections.Generic; using SPTarkov.Server.Core.Models.Spt.Mod; using SemanticVersioning;\n" + source,
            path, level, [Stub()]);
        return ServerModMetadataReader.Read(path);
    }

    public static TheoryData<OptimizationLevel> Levels => [OptimizationLevel.Release, OptimizationLevel.Debug];

    [Theory]
    [MemberData(nameof(Levels))]
    public void AnSpt41Record_IsReadInFull(OptimizationLevel level)
    {
        var metadata = Build("""
            public record ModMetadata : IModMetadata
            {
                public string ModGuid { get; init; } = "com.rcta.peely";
                public string Name { get; init; } = "RCTA - Peely";
                public string Author { get; init; } = "Hj";
                public Version Version { get; init; } = new("1.0.4");
                public Range SptVersion { get; init; } = new("~4.1.0");
                public Dictionary<string, Range> ModDependencies { get; init; } = new() { { "com.wtt.commonlib", new Range("~3.0.3") }, { "me.sol.sain", Range.Parse("~4.5.0") } };
                public string License { get; init; } = "NCSA";
            }
            """, level);

        Assert.NotNull(metadata);
        Assert.Equal("com.rcta.peely", metadata.Guid);
        Assert.Equal("RCTA - Peely", metadata.Name);
        Assert.Equal("Hj", metadata.Author);
        Assert.Equal("1.0.4", metadata.Version);
        Assert.Equal("~4.1.0", metadata.SptVersion);
        Assert.Equal([new ModDependencyRef("com.wtt.commonlib", false), new ModDependencyRef("me.sol.sain", false)], metadata.Dependencies);
    }

    [Theory]
    [MemberData(nameof(Levels))]
    public void AnSpt40Record_OverAbstractModMetadata_IsReadToo(OptimizationLevel level)
    {
        var metadata = Build("""
            public record ModMetadata : AbstractModMetadata
            {
                public override string ModGuid { get; init; } = "com.old.mod";
                public override string Name { get; init; } = "Old";
                public override string Author { get; init; } = "Someone";
                public override Version Version { get; init; } = new Version(2, 1, 0);
                public override Range SptVersion { get; init; } = new("~4.0.0");
                public override Dictionary<string, Range> ModDependencies { get; init; } = new() { ["com.x.lib"] = new("~1.0.0") };
            }
            """, level);

        Assert.Equal("com.old.mod", metadata?.Guid);
        Assert.Equal("2.1.0", metadata?.Version);
        Assert.Equal([new ModDependencyRef("com.x.lib", false)], metadata!.Dependencies);
    }

    [Theory]
    [MemberData(nameof(Levels))]
    public void ExpressionBodiedProperties_AreRead(OptimizationLevel level)
    {
        var metadata = Build("""
            public class ModMetadata : IModMetadata
            {
                public string ModGuid => "com.getters.mod";
                public string Name => "Getters";
                public string Author => "Me";
                public Version Version => new("3.0.0");
                public Range SptVersion => new("~4.1.0");
                public Dictionary<string, Range> ModDependencies => null;
            }
            """, level);

        Assert.Equal("com.getters.mod", metadata?.Guid);
        Assert.Equal("3.0.0", metadata?.Version);
    }

    [Theory]
    [MemberData(nameof(Levels))]
    public void ADependencyWhoseKeyIsNotALiteral_GivesNoDependencies(OptimizationLevel level)
    {
        var metadata = Build("""
            public static class Other { public static readonly string Guid = "com.other"; public const string Suffix = "x"; }
            public record ModMetadata : IModMetadata
            {
                public string ModGuid { get; init; } = "com.mod";
                public string Name { get; init; } = "Mod";
                public string Author { get; init; } = "Me";
                public Version Version { get; init; } = new("1.0.0");
                public Range SptVersion { get; init; } = new("~4.1.0");
                public Dictionary<string, Range> ModDependencies { get; init; } = new() { { Other.Guid, new Range("~3.0.3") }, { "com.lit", new Range("~1.0.0") } };
            }
            """, level);

        Assert.Equal("com.mod", metadata?.Guid);
        Assert.Empty(metadata!.Dependencies);
    }

    [Theory]
    [MemberData(nameof(Levels))]
    public void ADefaultInABaseClass_ThatTheClassOverrides_IsNotTakenForTheValue(OptimizationLevel level)
    {
        var metadata = Build("""
            public abstract record BaseMeta : IModMetadata
            {
                public virtual string ModGuid { get; init; } = "com.base.default";
                public string Name { get; init; } = "Shared name";
                public string Author { get; init; } = "Me";
                public virtual Version Version { get; init; } = new("0.0.1");
                public Range SptVersion { get; init; } = new("~4.1.0");
                public Dictionary<string, Range> ModDependencies { get; init; }
            }
            public record RealMeta : BaseMeta
            {
                public override string ModGuid { get; init; } = "com.real";
                public override Version Version { get; init; } = new("3.1.0");
            }
            """, level);

        // Set in two places: which one SPT sees is run-time order, so neither is guessed.
        Assert.NotNull(metadata);
        Assert.Null(metadata.Guid);
        Assert.Null(metadata.Version);

        // Set in one place, in the base class: read.
        Assert.Equal("Shared name", metadata.Name);
    }

    [Theory]
    [MemberData(nameof(Levels))]
    public void AValueTheConstructorReplaces_IsNotTakenFromTheInitialiser(OptimizationLevel level)
    {
        var metadata = Build("""
            public record ModMetadata : IModMetadata
            {
                public ModMetadata() { ModGuid = "com.real.guid"; }
                public string ModGuid { get; init; } = "com.template.guid";
                public string Name { get; init; } = "Mod";
                public string Author { get; init; } = "Me";
                public Version Version { get; init; } = new(typeof(ModMetadata).Assembly.GetName().Version.ToString(3));
                public Range SptVersion { get; init; } = new("~4.1.0");
                public Dictionary<string, Range> ModDependencies { get; init; }
            }
            """, level);

        Assert.NotNull(metadata);
        Assert.Null(metadata.Guid);
        Assert.Null(metadata.Version);
        Assert.Equal("Mod", metadata.Name);
    }

    [Fact]
    public void TwoMetadataClassesInOneDll_SayNothing()
    {
        var metadata = Build("""
            public record A : IModMetadata { public string ModGuid { get; init; } = "com.a"; public string Name { get; init; } public string Author { get; init; } public Version Version { get; init; } public Range SptVersion { get; init; } public Dictionary<string, Range> ModDependencies { get; init; } }
            public record B : IModMetadata { public string ModGuid { get; init; } = "com.b"; public string Name { get; init; } public string Author { get; init; } public Version Version { get; init; } public Range SptVersion { get; init; } public Dictionary<string, Range> ModDependencies { get; init; } }
            """, OptimizationLevel.Release);

        Assert.Null(metadata);
    }

    [Theory]
    [MemberData(nameof(Levels))]
    public void AKeyBuiltFromALiteral_IsNotTakenAsItBegan(OptimizationLevel level)
    {
        foreach (var key in new[] { "\"COM.UP\".ToLowerInvariant()", "\"com.b\" + Other.P", "$\"com.{Other.P}\"" })
        {
            var metadata = Build($$"""
                public static class Other { public static string P = "x"; }
                public record ModMetadata : IModMetadata
                {
                    public string ModGuid { get; init; } = "com.mod";
                    public string Name { get; init; } = "Mod";
                    public string Author { get; init; } = "Me";
                    public Version Version { get; init; } = new("1.0.0");
                    public Range SptVersion { get; init; } = new("~4.1.0");
                    public Dictionary<string, Range> ModDependencies { get; init; } = new() { { {{key}}, new Range("~1.0.0") } };
                }
                """, level);

            Assert.Equal("com.mod", metadata?.Guid);
            Assert.Empty(metadata!.Dependencies);
        }
    }

    [Theory]
    [MemberData(nameof(Levels))]
    public void ASetterOfAnotherType_OrAStoreMadeOnlySometimes_IsNotTheModsValue(OptimizationLevel level)
    {
        var metadata = Build("""
            public static class Log { public static string Author { get; set; } }
            public record ModMetadata : IModMetadata
            {
                public ModMetadata()
                {
                    Log.Author = "logger-tag";
                    if (System.Environment.TickCount > 0) { Festive = true; Name = "Sometimes"; }
                }
                public bool Festive { get; set; }
                public string ModGuid { get; init; } = "com.mod";
                public string Name { get; init; }
                public string Author { get; init; }
                public Version Version { get; init; } = new("1.0.0");
                public Range SptVersion { get; init; } = new("~4.1.0");
                public Dictionary<string, Range> ModDependencies { get; init; }
            }
            """, level);

        Assert.Equal("com.mod", metadata?.Guid);
        Assert.Equal("1.0.0", metadata?.Version);
        Assert.Null(metadata?.Author);
        Assert.Null(metadata?.Name);
    }

    [Theory]
    [MemberData(nameof(Levels))]
    public void ABranchInOneInitialiser_LeavesTheOthersRead(OptimizationLevel level)
    {
        var metadata = Build("""
            public record ModMetadata : IModMetadata
            {
                public static bool Beta;
                public string Name { get; init; } = Beta ? "T (beta)" : "T";
                public System.Func<string> Label { get; init; } = () => "x";
                public string Author { get; init; } = Beta switch { true => "b", _ => "a" };
                public string ModGuid { get; init; } = "com.after.branches";
                public Version Version { get; init; } = new("1.2.3");
                public Range SptVersion { get; init; } = new("~4.1.0");
                public Dictionary<string, Range> ModDependencies { get; init; } = new() { { "com.dep", new Range("~1.0.0") } };
            }
            """, level);

        Assert.Equal("com.after.branches", metadata?.Guid);
        Assert.Equal("1.2.3", metadata?.Version);
        Assert.Equal([new ModDependencyRef("com.dep", false)], metadata!.Dependencies);
        Assert.Null(metadata.Name);
        Assert.Null(metadata.Author);
    }
}
