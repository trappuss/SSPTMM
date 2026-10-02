using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace TCFModManager.Core.Tests;

//
// Writes a small mod DLL for ModAssemblyMetadata to read, with the exact IL and attributes a real
// mod's compiler output has - built directly with System.Reflection.Metadata, so the test project
// needs no compiler package.
//
// SPT, SemanticVersioning and BepInEx are referenced by name only (TypeReferences into assemblies
// that never exist on disk), which is how a shipped mod refers to them and all the reader looks at.
//
internal sealed class ModMetadataFixture
{
    public enum Kind
    {
        // A record deriving from SPT 4.0's AbstractModMetadata.
        Spt40,

        // A record implementing SPT 4.1's IModMetadata.
        Spt41,

        // A plain class - for BepInEx attributes, or a DLL with no metadata type.
        Plain,
    }

    private readonly MetadataBuilder _md = new();
    private readonly BlobBuilder _ilStream = new();
    private readonly MethodBodyStreamEncoder _bodies;
    private readonly Kind _kind;

    private readonly TypeReferenceHandle _object;
    private readonly TypeReferenceHandle _baseOrInterface;
    private readonly TypeReferenceHandle _version;
    private readonly TypeReferenceHandle _range;
    private readonly TypeReferenceHandle _dictionary;
    private readonly AssemblyReferenceHandle _bepInEx;

    private readonly Dictionary<string, FieldDefinitionHandle> _fields = new(StringComparer.Ordinal);
    private readonly List<(MemberReferenceHandle Constructor, BlobHandle Value)> _attributes = [];
    private FieldDefinitionHandle? _firstField;
    private MethodDefinitionHandle? _firstMethod;

    public ModMetadataFixture(Kind kind)
    {
        _kind = kind;
        _bodies = new MethodBodyStreamEncoder(_ilStream);

        var name = "Fixture" + Guid.NewGuid().ToString("N");
        _md.AddAssembly(_md.GetOrAddString(name), new Version(1, 0, 0, 0), default, default, default, AssemblyHashAlgorithm.None);
        _md.AddModule(0, _md.GetOrAddString(name + ".dll"), _md.GetOrAddGuid(Guid.NewGuid()), default, default);

        var runtime = AssemblyReference("System.Runtime");
        var collections = AssemblyReference("System.Collections");
        var spt = AssemblyReference("SPTarkov.Server.Core");
        var semver = AssemblyReference("SemanticVersioning");
        _bepInEx = AssemblyReference("BepInEx");

        _object = TypeReference(runtime, "System", "Object");
        _version = TypeReference(semver, "SemanticVersioning", "Version");
        _range = TypeReference(semver, "SemanticVersioning", "Range");
        _dictionary = TypeReference(collections, "System.Collections.Generic", "Dictionary`2");
        _baseOrInterface = kind == Kind.Spt40
            ? TypeReference(spt, "SPTarkov.Server.Core.Models.Spt.Mod", "AbstractModMetadata")
            : TypeReference(spt, "SPTarkov.Server.Core.Models.Spt.Mod", "IModMetadata");

        // <Module> owns no members, so it and the one real type both start their lists at row 1.
        _md.AddTypeDefinition(
            default, default, _md.GetOrAddString("<Module>"), default,
            MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));
    }

    // The metadata record's instance constructor. The base constructor call and ret are added here.
    public ModMetadataFixture Constructor(Action<Il> body)
    {
        var il = new Il(this);
        body(il);
        il.This().CallBaseConstructor().Ret();
        AddMethod(".ctor", ConstructorAttributes, VoidSignature(0), il.Encoder);
        return this;
    }

    // The compiler's record copy constructor: every backing field loaded from the other instance.
    public ModMetadataFixture CopyConstructor(params string[] properties)
    {
        var il = new Il(this);
        il.This().CallBaseConstructor();
        foreach (var property in properties) il.This().Arg1().LoadField(property).Set(property);
        il.Ret();
        AddMethod(".ctor", ConstructorAttributes, VoidSignature(1), il.Encoder);
        return this;
    }

    // An expression-bodied property: get_<property> returning whatever the body leaves on the stack.
    public ModMetadataFixture Getter(string property, Action<Il> body)
    {
        var il = new Il(this);
        body(il);
        il.Ret();

        var signature = new BlobBuilder();
        new BlobEncoder(signature).MethodSignature(isInstanceMethod: true)
            .Parameters(0, r => r.Type().Object(), _ => { });

        AddMethod(
            "get_" + property,
            MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.Virtual,
            _md.GetOrAddBlob(signature),
            il.Encoder);
        return this;
    }

    // A static method returning a string, standing in for a value only known at runtime.
    public ModMetadataFixture StaticStringMethod(string name)
    {
        var il = new Il(this).Str("computed").Ret();

        var signature = new BlobBuilder();
        new BlobEncoder(signature).MethodSignature().Parameters(0, r => r.Type().String(), _ => { });

        AddMethod(name, MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
            _md.GetOrAddBlob(signature), il.Encoder);
        return this;
    }

    // [BepInPlugin(guid, name, version)]
    public ModMetadataFixture Plugin(string guid, string name, string version)
    {
        var type = TypeReference(_bepInEx, "BepInEx", "BepInPlugin");
        var constructor = AttributeConstructor(type, p =>
        {
            p.AddParameter().Type().String();
            p.AddParameter().Type().String();
            p.AddParameter().Type().String();
        }, 3);

        _attributes.Add((constructor, AttributeValue(b =>
        {
            b.WriteSerializedString(guid);
            b.WriteSerializedString(name);
            b.WriteSerializedString(version);
        })));
        return this;
    }

    // [BepInDependency(guid, flags)] - 1 hard, 2 soft.
    public ModMetadataFixture Dependency(string guid, int flags)
    {
        var dependency = TypeReference(_bepInEx, "BepInEx", "BepInDependency");
        var flagsType = _md.AddTypeReference(dependency, default, _md.GetOrAddString("DependencyFlags"));
        var constructor = AttributeConstructor(dependency, p =>
        {
            p.AddParameter().Type().String();
            p.AddParameter().Type().Type(flagsType, isValueType: true);
        }, 2);

        _attributes.Add((constructor, AttributeValue(b =>
        {
            b.WriteSerializedString(guid);
            b.WriteInt32(flags);
        })));
        return this;
    }

    // [BepInDependency(guid, minimumVersion)]
    public ModMetadataFixture Dependency(string guid, string minimumVersion)
    {
        var dependency = TypeReference(_bepInEx, "BepInEx", "BepInDependency");
        var constructor = AttributeConstructor(dependency, p =>
        {
            p.AddParameter().Type().String();
            p.AddParameter().Type().String();
        }, 2);

        _attributes.Add((constructor, AttributeValue(b =>
        {
            b.WriteSerializedString(guid);
            b.WriteSerializedString(minimumVersion);
        })));
        return this;
    }

    // Writes the DLL into directory - under fileName when given, otherwise a unique name - and
    // returns its path.
    public string Write(string directory, string? fileName = null)
    {
        var baseType = _kind == Kind.Spt40 ? (EntityHandle)_baseOrInterface : _object;
        var type = _md.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Class,
            _md.GetOrAddString("ExampleMod"),
            _md.GetOrAddString("ModMetadata"),
            baseType,
            _firstField ?? MetadataTokens.FieldDefinitionHandle(_md.GetRowCount(TableIndex.Field) + 1),
            _firstMethod ?? MetadataTokens.MethodDefinitionHandle(_md.GetRowCount(TableIndex.MethodDef) + 1));

        if (_kind == Kind.Spt41) _md.AddInterfaceImplementation(type, _baseOrInterface);

        foreach (var (constructor, value) in _attributes) _md.AddCustomAttribute(type, constructor, value);

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(_md),
            _ilStream);

        var image = new BlobBuilder();
        pe.Serialize(image);

        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, fileName ?? Guid.NewGuid().ToString("N") + ".dll");
        File.WriteAllBytes(path, image.ToArray());
        return path;
    }

    private const MethodAttributes ConstructorAttributes =
        MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName;

    private AssemblyReferenceHandle AssemblyReference(string name) =>
        _md.AddAssemblyReference(_md.GetOrAddString(name), new Version(1, 0, 0, 0), default, default, default, default);

    private TypeReferenceHandle TypeReference(AssemblyReferenceHandle scope, string ns, string name) =>
        _md.AddTypeReference(scope, _md.GetOrAddString(ns), _md.GetOrAddString(name));

    private void AddMethod(string name, MethodAttributes attributes, BlobHandle signature, InstructionEncoder il)
    {
        var offset = _bodies.AddMethodBody(il, maxStack: 16);
        var handle = _md.AddMethodDefinition(
            attributes, MethodImplAttributes.IL, _md.GetOrAddString(name), signature, offset,
            MetadataTokens.ParameterHandle(_md.GetRowCount(TableIndex.Param) + 1));
        _firstMethod ??= handle;
    }

    private FieldDefinitionHandle Field(string property)
    {
        if (_fields.TryGetValue(property, out var existing)) return existing;

        var signature = new BlobBuilder();
        new BlobEncoder(signature).FieldSignature().Object();

        var handle = _md.AddFieldDefinition(
            FieldAttributes.Private, _md.GetOrAddString($"<{property}>k__BackingField"), _md.GetOrAddBlob(signature));
        _firstField ??= handle;
        _fields[property] = handle;
        return handle;
    }

    private BlobHandle VoidSignature(int objectParameters)
    {
        var signature = new BlobBuilder();
        new BlobEncoder(signature).MethodSignature(isInstanceMethod: true).Parameters(
            objectParameters,
            r => r.Void(),
            p => { for (var i = 0; i < objectParameters; i++) p.AddParameter().Type().Object(); });
        return _md.GetOrAddBlob(signature);
    }

    private MemberReferenceHandle InstanceMethod(EntityHandle parent, string name, int count, Action<ParametersEncoder> parameters)
    {
        var signature = new BlobBuilder();
        new BlobEncoder(signature).MethodSignature(isInstanceMethod: true).Parameters(count, r => r.Void(), parameters);
        return _md.AddMemberReference(parent, _md.GetOrAddString(name), _md.GetOrAddBlob(signature));
    }

    private MemberReferenceHandle AttributeConstructor(TypeReferenceHandle type, Action<ParametersEncoder> parameters, int count) =>
        InstanceMethod(type, ".ctor", count, parameters);

    private BlobHandle AttributeValue(Action<BlobBuilder> arguments)
    {
        var value = new BlobBuilder();
        value.WriteUInt16(1);
        arguments(value);
        value.WriteUInt16(0);
        return _md.GetOrAddBlob(value);
    }

    private EntityHandle DictionaryOfStringRange()
    {
        var signature = new BlobBuilder();
        var arguments = new BlobEncoder(signature).TypeSpecificationSignature().GenericInstantiation(_dictionary, 2, isValueType: false);
        arguments.AddArgument().String();
        arguments.AddArgument().Type(_range, isValueType: false);
        return _md.AddTypeSpecification(_md.GetOrAddBlob(signature));
    }

    //
    // The IL for one method body, one instruction per call.
    //
    public sealed class Il(ModMetadataFixture owner)
    {
        public InstructionEncoder Encoder { get; } = new(new BlobBuilder());

        public Il This() => Op(ILOpCode.Ldarg_0);

        public Il Arg1() => Op(ILOpCode.Ldarg_1);

        public Il Str(string value)
        {
            Encoder.LoadString(owner._md.GetOrAddUserString(value));
            return this;
        }

        public Il Int(int value)
        {
            Encoder.LoadConstantI4(value);
            return this;
        }

        public Il Null() => Op(ILOpCode.Ldnull);

        public Il Dup() => Op(ILOpCode.Dup);

        public Il Ret() => Op(ILOpCode.Ret);

        // stfld <property>k__BackingField
        public Il Set(string property) => Token(ILOpCode.Stfld, owner.Field(property));

        public Il LoadField(string property) => Token(ILOpCode.Ldfld, owner.Field(property));

        // this.<property> = "value"
        public Il SetString(string property, string value) => This().Str(value).Set(property);

        // new SemanticVersioning.Version(string input, bool loose) - takes the string and bool on the stack.
        public Il NewVersionFromString() => Token(ILOpCode.Newobj, owner.InstanceMethod(owner._version, ".ctor", 2, p =>
        {
            p.AddParameter().Type().String();
            p.AddParameter().Type().Boolean();
        }));

        // new SemanticVersioning.Version(int, int, int, string preRelease, string build)
        public Il NewVersionFromInts() => Token(ILOpCode.Newobj, owner.InstanceMethod(owner._version, ".ctor", 5, p =>
        {
            p.AddParameter().Type().Int32();
            p.AddParameter().Type().Int32();
            p.AddParameter().Type().Int32();
            p.AddParameter().Type().String();
            p.AddParameter().Type().String();
        }));

        // new SemanticVersioning.Range(string spec, bool loose)
        public Il NewRange() => Token(ILOpCode.Newobj, owner.InstanceMethod(owner._range, ".ctor", 2, p =>
        {
            p.AddParameter().Type().String();
            p.AddParameter().Type().Boolean();
        }));

        // this.<property> = new Version("text")
        public Il SetVersion(string property, string text) => This().Str(text).Int(0).NewVersionFromString().Set(property);

        // this.<property> = new Range("spec")
        public Il SetRange(string property, string spec) => This().Str(spec).Int(0).NewRange().Set(property);

        public Il NewDictionary() => Token(ILOpCode.Newobj, owner.InstanceMethod(owner.DictionaryOfStringRange(), ".ctor", 0, _ => { }));

        // dictionary.Add(key, value) - Dictionary<string, Range>.Add(!0, !1)
        public Il DictionaryAdd() => Token(ILOpCode.Callvirt, owner.InstanceMethod(owner.DictionaryOfStringRange(), "Add", 2, p =>
        {
            p.AddParameter().Type().GenericTypeParameter(0);
            p.AddParameter().Type().GenericTypeParameter(1);
        }));

        // dictionary[key] = value
        public Il DictionarySetItem() => Token(ILOpCode.Callvirt, owner.InstanceMethod(owner.DictionaryOfStringRange(), "set_Item", 2, p =>
        {
            p.AddParameter().Type().GenericTypeParameter(0);
            p.AddParameter().Type().GenericTypeParameter(1);
        }));

        // A call to this type's own static string method (see StaticStringMethod).
        public Il CallStatic(string name)
        {
            var signature = new BlobBuilder();
            new BlobEncoder(signature).MethodSignature().Parameters(0, r => r.Type().String(), _ => { });
            var method = owner._md.AddMemberReference(
                owner._object, owner._md.GetOrAddString(name), owner._md.GetOrAddBlob(signature));
            return Token(ILOpCode.Call, method);
        }

        public Il CallBaseConstructor()
        {
            var parent = owner._kind == Kind.Spt40 ? owner._baseOrInterface : owner._object;
            return Token(ILOpCode.Call, owner.InstanceMethod(parent, ".ctor", 0, _ => { }));
        }

        private Il Op(ILOpCode code)
        {
            Encoder.OpCode(code);
            return this;
        }

        private Il Token(ILOpCode code, EntityHandle handle)
        {
            Encoder.OpCode(code);
            Encoder.Token(handle);
            return this;
        }
    }
}
