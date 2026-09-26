using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace TCFModManager.Core.Tests;

//
// Writes small .NET DLLs for the scanner to read - never loaded or run, only read the way the app
// reads a mod's DLL (metadata and IL). Each carries what a real mod's does, down to where each type
// is referenced from: an SPT 4 server mod's metadata class implements SPTarkov.Server.Core's
// IModMetadata (a type reference into that assembly, as in a real mod), and a plugin's
// [BepInPlugin] comes from BepInEx.
//
// The IL of a server mod's constructor is laid down in the same shapes the C# compiler emits for
// property initialisers, checked against real mods (ORBIT, Dynamic Maps, SAIN, RCTA Peely...).
//
internal static class TestDlls
{
    /// <summary>One property of the metadata class.</summary>
    public abstract record Init(string Property);

    /// <summary><c>Property { get; init; } = "value";</c></summary>
    public sealed record Str(string Property, string Value) : Init(Property);

    /// <summary><c>= new("value")</c> on a SemanticVersioning Version or Range - or its Parse.</summary>
    public sealed record Wrapped(string Property, string TypeName, string Value, bool ViaParse = false) : Init(Property);

    /// <summary><c>= new Version(1, 2, 3, pre)</c>.</summary>
    public sealed record IntVersion(string Property, int Major, int Minor, int Patch, string? Pre = null) : Init(Property);

    /// <summary>A Version worked out at run time: <c>= new(Helper.Compute())</c>.</summary>
    public sealed record Computed(string Property) : Init(Property);

    /// <summary><c>= new() { { "guid", new Range("~1.0") } }</c> (or <c>["guid"] = ...</c>).</summary>
    public sealed record Deps(string Property, bool Indexer, params (string Guid, string Range)[] Entries) : Init(Property);

    /// <summary><c>Property => "value";</c> - no backing field.</summary>
    public sealed record Getter(string Property, string Value) : Init(Property);

    public static void ServerMod(string path, params Init[] inits) => ServerMod(path, viaBaseClass: false, inits);

    public static void ServerMod(string path, bool viaBaseClass, params Init[] inits)
    {
        var w = new Writer(Path.GetFileNameWithoutExtension(path));

        var sptCore = w.AssemblyRef("SPTarkov.Server.Core");
        var semver = w.AssemblyRef("SemanticVersioning");
        var modNs = "SPTarkov.Server.Core.Models.Spt.Mod";
        var contract = w.Md.AddTypeReference(sptCore, w.Md.GetOrAddString(modNs),
            w.Md.GetOrAddString(viaBaseClass ? "AbstractModMetadata" : "IModMetadata"));
        var version = w.Md.AddTypeReference(semver, w.Md.GetOrAddString("SemanticVersioning"), w.Md.GetOrAddString("Version"));
        var range = w.Md.AddTypeReference(semver, w.Md.GetOrAddString("SemanticVersioning"), w.Md.GetOrAddString("Range"));
        var helper = w.Md.AddTypeReference(w.Runtime, w.Md.GetOrAddString("Helpers"), w.Md.GetOrAddString("Helper"));

        var baseCtor = w.Ctor(viaBaseClass ? contract : w.Object, 0, p => { });
        var versionFromString = w.Ctor(version, 2, p => { p.AddParameter().Type().String(); p.AddParameter().Type().Boolean(); });
        var versionFromInts = w.Ctor(version, 5, p =>
        {
            p.AddParameter().Type().Int32();
            p.AddParameter().Type().Int32();
            p.AddParameter().Type().Int32();
            p.AddParameter().Type().String();
            p.AddParameter().Type().String();
        });
        var rangeFromString = w.Ctor(range, 2, p => { p.AddParameter().Type().String(); p.AddParameter().Type().Boolean(); });
        var compute = w.Method(helper, "Compute", instance: false, r => r.Type().String(), 0, p => { });

        MemberReferenceHandle Parse(EntityHandle type) => w.Method(type, "Parse", instance: false,
            r => r.Type().Type(type, isValueType: false), 2,
            p => { p.AddParameter().Type().String(); p.AddParameter().Type().Boolean(); });

        // Dictionary<string, Range>, as a generic instantiation - the parent real mods call Add on.
        var dictionaryOpen = w.Md.AddTypeReference(w.Runtime, w.Md.GetOrAddString("System.Collections.Generic"), w.Md.GetOrAddString("Dictionary`2"));
        var spec = new BlobBuilder();
        var arguments = new BlobEncoder(spec).TypeSpecificationSignature().GenericInstantiation(dictionaryOpen, 2, isValueType: false);
        arguments.AddArgument().String();
        arguments.AddArgument().Type(range, isValueType: false);
        var dictionary = w.Md.AddTypeSpecification(w.Md.GetOrAddBlob(spec));
        var dictionaryCtor = w.Ctor(dictionary, 0, p => { });
        MemberReferenceHandle DictionaryMethod(string name) => w.Method(dictionary, name, instance: true, r => r.Void(), 2, p =>
        {
            p.AddParameter().Type().GenericTypeParameter(0);
            p.AddParameter().Type().GenericTypeParameter(1);
        });
        var add = DictionaryMethod("Add");
        var setItem = DictionaryMethod("set_Item");

        // The backing fields, then the constructor and any expression-bodied getters.
        var firstField = MetadataTokens.FieldDefinitionHandle(w.Md.GetRowCount(TableIndex.Field) + 1);
        var fields = new Dictionary<string, FieldDefinitionHandle>();
        foreach (var init in inits.Where(i => i is not Getter))
        {
            var signature = new BlobBuilder();
            new BlobEncoder(signature).FieldSignature().Object();
            fields[init.Property] = w.Md.AddFieldDefinition(FieldAttributes.Private,
                w.Md.GetOrAddString($"<{init.Property}>k__BackingField"), w.Md.GetOrAddBlob(signature));
        }

        var il = new InstructionEncoder(new BlobBuilder());
        foreach (var init in inits.Where(i => i is not Getter))
        {
            il.LoadArgument(0);
            switch (init)
            {
                case Str s:
                    il.LoadString(w.Md.GetOrAddUserString(s.Value));
                    break;
                case Wrapped x:
                    var type = x.TypeName == "Range" ? range : version;
                    il.LoadString(w.Md.GetOrAddUserString(x.Value));
                    il.LoadConstantI4(0);
                    if (x.ViaParse)
                    {
                        il.Call(Parse(type));
                    }
                    else
                    {
                        il.OpCode(ILOpCode.Newobj);
                        il.Token(x.TypeName == "Range" ? rangeFromString : versionFromString);
                    }

                    break;
                case IntVersion v:
                    il.LoadConstantI4(v.Major);
                    il.LoadConstantI4(v.Minor);
                    il.LoadConstantI4(v.Patch);
                    if (v.Pre is null) il.OpCode(ILOpCode.Ldnull);
                    else il.LoadString(w.Md.GetOrAddUserString(v.Pre));
                    il.OpCode(ILOpCode.Ldnull);
                    il.OpCode(ILOpCode.Newobj);
                    il.Token(versionFromInts);
                    break;
                case Computed:
                    il.Call(compute);
                    il.LoadConstantI4(0);
                    il.OpCode(ILOpCode.Newobj);
                    il.Token(versionFromString);
                    break;
                case Deps d:
                    il.OpCode(ILOpCode.Newobj);
                    il.Token(dictionaryCtor);
                    foreach (var (guid, constraint) in d.Entries)
                    {
                        il.OpCode(ILOpCode.Dup);
                        il.LoadString(w.Md.GetOrAddUserString(guid));
                        il.LoadString(w.Md.GetOrAddUserString(constraint));
                        il.LoadConstantI4(0);
                        il.OpCode(ILOpCode.Newobj);
                        il.Token(rangeFromString);
                        il.OpCode(ILOpCode.Callvirt);
                        il.Token(d.Indexer ? setItem : add);
                    }

                    break;
            }

            il.OpCode(ILOpCode.Stfld);
            il.Token(fields[init.Property]);
        }

        il.LoadArgument(0);
        il.Call(baseCtor);
        il.OpCode(ILOpCode.Ret);

        var firstMethod = w.MethodDef(".ctor", MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
            instance: true, r => r.Void(), il);

        foreach (var getter in inits.OfType<Getter>())
        {
            var body = new InstructionEncoder(new BlobBuilder());
            body.LoadString(w.Md.GetOrAddUserString(getter.Value));
            body.OpCode(ILOpCode.Ret);
            w.MethodDef("get_" + getter.Property, MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.Virtual,
                instance: true, r => r.Type().String(), body);
        }

        var metadataType = w.Md.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Class,
            w.Md.GetOrAddString("SomeMod"),
            w.Md.GetOrAddString("ModMetadata"),
            viaBaseClass ? contract : w.Object,
            firstField,
            firstMethod);
        if (!viaBaseClass) w.Md.AddInterfaceImplementation(metadataType, contract);

        w.Save(path);
    }

    /// <summary>A BepInEx plugin: [BepInPlugin(guid, name, version)], and an AssemblyFileVersion
    /// when <paramref name="fileVersion"/> is given.</summary>
    public static void Plugin(string path, string guid, string name, string version, string? fileVersion = null)
    {
        var w = new Writer(Path.GetFileNameWithoutExtension(path));

        var bepinex = w.AssemblyRef("BepInEx");
        var pluginAttribute = w.Md.AddTypeReference(bepinex, w.Md.GetOrAddString("BepInEx"), w.Md.GetOrAddString("BepInPlugin"));
        var pluginCtor = w.Ctor(pluginAttribute, 3, p =>
        {
            p.AddParameter().Type().String();
            p.AddParameter().Type().String();
            p.AddParameter().Type().String();
        });

        var il = new InstructionEncoder(new BlobBuilder());
        il.LoadArgument(0);
        il.Call(w.Ctor(w.Object, 0, p => { }));
        il.OpCode(ILOpCode.Ret);
        var ctor = w.MethodDef(".ctor", MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
            instance: true, r => r.Void(), il);

        var type = w.Md.AddTypeDefinition(TypeAttributes.Public | TypeAttributes.Class, w.Md.GetOrAddString("SomePlugin"),
            w.Md.GetOrAddString("Plugin"), w.Object, MetadataTokens.FieldDefinitionHandle(1), ctor);

        w.Md.AddCustomAttribute(type, pluginCtor, w.Md.GetOrAddBlob(AttributeValue(guid, name, version)));

        if (fileVersion is not null)
        {
            var fileVersionAttribute = w.Md.AddTypeReference(w.Runtime, w.Md.GetOrAddString("System.Reflection"),
                w.Md.GetOrAddString("AssemblyFileVersionAttribute"));
            var fileVersionCtor = w.Ctor(fileVersionAttribute, 1, p => p.AddParameter().Type().String());
            w.Md.AddCustomAttribute(EntityHandle.AssemblyDefinition, fileVersionCtor, w.Md.GetOrAddBlob(AttributeValue(fileVersion)));
        }

        w.Save(path);
    }

    private static BlobBuilder AttributeValue(params string[] values)
    {
        var blob = new BlobBuilder();
        blob.WriteUInt16(1);
        foreach (var value in values) blob.WriteSerializedString(value);
        blob.WriteUInt16(0);
        return blob;
    }

    private sealed class Writer
    {
        public MetadataBuilder Md { get; } = new();

        public AssemblyReferenceHandle Runtime { get; }

        public TypeReferenceHandle Object { get; }

        private readonly MethodBodyStreamEncoder _bodies = new(new BlobBuilder());

        public Writer(string name)
        {
            Md.AddModule(0, Md.GetOrAddString(name + ".dll"), Md.GetOrAddGuid(Guid.NewGuid()), default, default);
            Md.AddAssembly(Md.GetOrAddString(name), new Version(1, 0, 0, 0), default, default, 0, AssemblyHashAlgorithm.None);
            Runtime = AssemblyRef("System.Runtime");
            Object = Md.AddTypeReference(Runtime, Md.GetOrAddString("System"), Md.GetOrAddString("Object"));

            // <Module>, which owns nothing: the next type's fields and methods start at row 1.
            Md.AddTypeDefinition(default, default, Md.GetOrAddString("<Module>"), default,
                MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));
        }

        public AssemblyReferenceHandle AssemblyRef(string name) =>
            Md.AddAssemblyReference(Md.GetOrAddString(name), new Version(1, 0, 0, 0), default, default, default, default);

        public MemberReferenceHandle Ctor(EntityHandle type, int count, Action<ParametersEncoder> parameters) =>
            Method(type, ".ctor", instance: true, r => r.Void(), count, parameters);

        public MemberReferenceHandle Method(EntityHandle type, string name, bool instance,
            Action<ReturnTypeEncoder> returns, int count, Action<ParametersEncoder> parameters)
        {
            var signature = new BlobBuilder();
            new BlobEncoder(signature).MethodSignature(isInstanceMethod: instance).Parameters(count, returns, parameters);
            return Md.AddMemberReference(type, Md.GetOrAddString(name), Md.GetOrAddBlob(signature));
        }

        public MethodDefinitionHandle MethodDef(string name, MethodAttributes attributes, bool instance,
            Action<ReturnTypeEncoder> returns, InstructionEncoder il)
        {
            var signature = new BlobBuilder();
            new BlobEncoder(signature).MethodSignature(isInstanceMethod: instance).Parameters(0, returns, _ => { });
            var offset = _bodies.AddMethodBody(il);
            return Md.AddMethodDefinition(attributes, MethodImplAttributes.IL, Md.GetOrAddString(name),
                Md.GetOrAddBlob(signature), offset, default);
        }

        public void Save(string path)
        {
            var pe = new ManagedPEBuilder(PEHeaderBuilder.CreateLibraryHeader(), new MetadataRootBuilder(Md), _bodies.Builder);
            var image = new BlobBuilder();
            pe.Serialize(image);

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var file = File.Create(path);
            image.WriteContentTo(file);
        }
    }
}
