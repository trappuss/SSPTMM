using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

//
// Reads what a mod DLL declares about itself - GUID, name, author, version, dependencies - by
// walking its raw ECMA-335 metadata. Nothing is loaded or executed, and nothing here throws: a file
// that isn't readable managed code simply declares nothing.
//
// Client plugins declare themselves through attributes, which are plain data in the metadata.
//
// SPT 4.x server mods declare themselves through a record: SPT 4.0 derives one from the abstract
// record AbstractModMetadata, SPT 4.1 implements the interface IModMetadata, both in
// SPTarkov.Server.Core.Models.Spt.Mod. The values are property initialisers, which the compiler
// turns into constructor IL - "ldstr; stfld <ModGuid>k__BackingField" and so on - so they are read
// by evaluating that IL over constants only (see ConstantEvaluator). A value computed any other way
// at runtime stays unknown rather than guessed.
//
public static class ModAssemblyMetadata
{
    private const string SptModNamespace = "SPTarkov.Server.Core.Models.Spt.Mod";

    // BepInEx.BepInDependency.DependencyFlags.SoftDependency.
    private const int SoftDependencyFlag = 2;

    //
    // [BepInPlugin(guid, name, version)] from the first type carrying one, and every
    // [BepInDependency] on any type. Guid is null when the DLL has no plugin attribute.
    //
    public static DeclaredMetadata ReadPlugin(string dllPath)
    {
        try
        {
            using var stream = File.OpenRead(dllPath);
            using var peReader = new PEReader(stream);
            if (!peReader.HasMetadata) return DeclaredMetadata.Empty;

            var reader = peReader.GetMetadataReader();
            string? guid = null, name = null, version = null;
            var dependencies = new List<ModDependencyRef>();

            foreach (var typeHandle in reader.TypeDefinitions)
            {
                var typeDef = reader.GetTypeDefinition(typeHandle);

                foreach (var attributeHandle in typeDef.GetCustomAttributes())
                {
                    var attribute = reader.GetCustomAttribute(attributeHandle);

                    if (guid is null && IsBepInExAttribute(reader, attribute, "BepInPlugin"))
                    {
                        (guid, name, version) = TryDecodePlugin(reader, attribute);
                        continue;
                    }

                    if (!IsBepInExAttribute(reader, attribute, "BepInDependency")) continue;

                    var dependency = TryDecodeDependency(reader, attribute);
                    if (dependency is not null) dependencies.Add(dependency);
                }
            }

            return new DeclaredMetadata
            {
                Guid = guid,
                Name = name,
                Version = version,
                Dependencies = dependencies,
            };
        }
        catch (Exception)
        {
            return DeclaredMetadata.Empty;
        }
    }

    //
    // The assembly's own name and version from its definition row, or nulls for a file that isn't a
    // managed assembly - a native DLL, a netmodule, or anything unreadable. Reads headers only.
    //
    public static (string? Name, string? Version) ReadIdentity(string dllPath)
    {
        try
        {
            using var stream = File.OpenRead(dllPath);
            using var peReader = new PEReader(stream);
            if (!peReader.HasMetadata) return (null, null);

            var reader = peReader.GetMetadataReader();
            if (!reader.IsAssembly) return (null, null);

            var definition = reader.GetAssemblyDefinition();
            return (reader.GetString(definition.Name), definition.Version.ToString());
        }
        catch (Exception)
        {
            return (null, null);
        }
    }

    //
    // The SPT 4.x server mod metadata record in this DLL, or null when there isn't one - a bundled
    // library, an SPT 3.x mod's helper DLL, or anything that isn't managed code.
    //
    public static DeclaredMetadata? ReadServer(string dllPath)
    {
        try
        {
            using var stream = File.OpenRead(dllPath);
            using var peReader = new PEReader(stream);
            if (!peReader.HasMetadata) return null;

            var reader = peReader.GetMetadataReader();

            foreach (var typeHandle in reader.TypeDefinitions)
            {
                var typeDef = reader.GetTypeDefinition(typeHandle);
                if (!IsModMetadataType(reader, typeDef)) continue;

                var values = new Dictionary<string, object>(StringComparer.Ordinal);

                foreach (var methodHandle in typeDef.GetMethods())
                {
                    var method = reader.GetMethodDefinition(methodHandle);
                    if (method.RelativeVirtualAddress == 0) continue;

                    var methodName = reader.GetString(method.Name);
                    var isConstructor = methodName == ".ctor";
                    var getterOf = methodName.StartsWith("get_", StringComparison.Ordinal) ? methodName[4..] : null;
                    if (!isConstructor && getterOf is null) continue;

                    var body = peReader.GetMethodBody(method.RelativeVirtualAddress);
                    new ConstantEvaluator(reader, values, getterOf).Run(body.GetILReader());
                }

                return ToServerMetadata(values);
            }

            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool IsModMetadataType(MetadataReader reader, TypeDefinition typeDef)
    {
        if (IsTypeReference(reader, typeDef.BaseType, SptModNamespace, "AbstractModMetadata")) return true;

        foreach (var implementationHandle in typeDef.GetInterfaceImplementations())
        {
            var implementation = reader.GetInterfaceImplementation(implementationHandle);
            if (IsTypeReference(reader, implementation.Interface, SptModNamespace, "IModMetadata")) return true;
        }

        return false;
    }

    private static bool IsTypeReference(MetadataReader reader, EntityHandle handle, string ns, string name)
    {
        if (handle.IsNil || handle.Kind != HandleKind.TypeReference) return false;

        var typeRef = reader.GetTypeReference((TypeReferenceHandle)handle);
        return reader.GetString(typeRef.Namespace) == ns && reader.GetString(typeRef.Name) == name;
    }

    private static DeclaredMetadata ToServerMetadata(Dictionary<string, object> values)
    {
        var dependencies = values.GetValueOrDefault("ModDependencies") is DictionaryValue declared
            ? declared.Entries
                .Where(e => !string.IsNullOrWhiteSpace(e.Key))
                .Select(e => new ModDependencyRef(e.Key.Trim(), IsSoft: false, NullIfBlank(e.Value)))
                .ToList()
            : [];

        return new DeclaredMetadata
        {
            Guid = NullIfBlank(values.GetValueOrDefault("ModGuid") as string),
            Name = NullIfBlank(values.GetValueOrDefault("Name") as string),
            Author = NullIfBlank(values.GetValueOrDefault("Author") as string),
            Version = NullIfBlank((values.GetValueOrDefault("Version") as VersionValue)?.Text),
            SptVersion = NullIfBlank((values.GetValueOrDefault("SptVersion") as RangeValue)?.Text),
            Dependencies = dependencies,
        };
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    // True if this custom attribute's constructor resolves to BepInEx.<name>, resolved by
    // namespace/name from the DLL's own TypeReference table without loading BepInEx.dll.
    private static bool IsBepInExAttribute(MetadataReader reader, CustomAttribute attribute, string name)
    {
        if (attribute.Constructor.Kind != HandleKind.MemberReference) return false;

        var memberRef = reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor);
        return IsTypeReference(reader, memberRef.Parent, "BepInEx", name);
    }

    // Custom attribute value blobs start with a fixed 2-byte prolog (0x0001) per ECMA-335 II.23.3.
    // BepInPlugin's only constructor is (string GUID, string Name, string Version).
    private static (string? Guid, string? Name, string? Version) TryDecodePlugin(
        MetadataReader reader, CustomAttribute attribute)
    {
        var blobReader = reader.GetBlobReader(attribute.Value);
        if (blobReader.ReadUInt16() != 1) return (null, null, null);

        var guid = NullIfBlank(blobReader.ReadSerializedString());
        if (guid is null) return (null, null, null);

        string? name = null, version = null;
        try
        {
            name = NullIfBlank(blobReader.ReadSerializedString());
            version = NullIfBlank(blobReader.ReadSerializedString());
        }
        catch (BadImageFormatException)
        {
        }

        return (guid, name, version);
    }

    //
    // Decodes a [BepInDependency] into its GUID and hardness. BepInEx has two constructors:
    // (string guid, DependencyFlags flags) and (string guid, string minimumVersion) - the second is
    // always a hard dependency, and its minimum version becomes a ">=" range. Which one was used is
    // read from the constructor's own signature, since the value blob alone can't tell an enum from
    // a string.
    //
    private static ModDependencyRef? TryDecodeDependency(MetadataReader reader, CustomAttribute attribute)
    {
        var memberRef = reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor);

        var blobReader = reader.GetBlobReader(attribute.Value);
        if (blobReader.ReadUInt16() != 1) return null;

        var guid = blobReader.ReadSerializedString();
        if (string.IsNullOrWhiteSpace(guid)) return null;

        return SecondParameter(reader, memberRef) switch
        {
            SecondParameterKind.Enum => new ModDependencyRef(guid, (blobReader.ReadInt32() & SoftDependencyFlag) != 0),
            SecondParameterKind.String => new ModDependencyRef(guid, false, MinimumVersionRange(ref blobReader)),
            _ => new ModDependencyRef(guid, false),
        };
    }

    private static string? MinimumVersionRange(ref BlobReader blobReader)
    {
        try
        {
            var minimum = NullIfBlank(blobReader.ReadSerializedString());
            return minimum is null ? null : ">=" + minimum;
        }
        catch (BadImageFormatException)
        {
            return null;
        }
    }

    private enum SecondParameterKind
    {
        None,
        Enum,
        String,
    }

    //
    // What a member reference's second parameter is: a value type (the DependencyFlags enum), a
    // string, or absent. Walks the raw method signature blob: calling convention, parameter count,
    // return type, then each parameter's element type.
    //
    private static SecondParameterKind SecondParameter(MetadataReader reader, MemberReference memberRef)
    {
        try
        {
            var signature = reader.GetBlobReader(memberRef.Signature);

            var callingConvention = signature.ReadByte();
            if ((callingConvention & 0x10) != 0) signature.ReadCompressedInteger();

            if (signature.ReadCompressedInteger() < 2) return SecondParameterKind.None;

            // Return type of a constructor is void, then the GUID string.
            signature.ReadSignatureTypeCode();
            signature.ReadSignatureTypeCode();

            return signature.ReadSignatureTypeCode() switch
            {
                SignatureTypeCode.TypeHandle => SecondParameterKind.Enum,
                SignatureTypeCode.String => SecondParameterKind.String,
                _ => SecondParameterKind.None,
            };
        }
        catch (Exception)
        {
            return SecondParameterKind.None;
        }
    }

    // The values the evaluator builds. Anything it can't represent is Unknown.
    private sealed record VersionValue(string Text);

    private sealed record RangeValue(string Text);

    private sealed class DictionaryValue
    {
        public List<KeyValuePair<string, string?>> Entries { get; } = [];
    }

    private sealed class Unknown
    {
        public static Unknown Value { get; } = new();
    }

    private sealed class Null
    {
        public static Null Value { get; } = new();
    }

    //
    // Symbolically executes one method body over constants only. It understands exactly what
    // property initialisers compile to - constants, SemanticVersioning.Version / Range construction,
    // a Dictionary built with Add, and stfld into an auto-property's backing field - and treats
    // everything else as producing an unknown value. Branches end the evaluation, since an
    // initialiser never branches and anything that does is not a constant.
    //
    // For a getter (getterOf set), the value on the stack at ret is that property's value, which is
    // what an expression-bodied property ("public string ModGuid => "x";") compiles to.
    //
    // A known value is never overwritten by an unknown one, so the record's copy constructor - which
    // loads every field from another instance - leaves what the real constructor set alone.
    //
    private sealed class ConstantEvaluator(MetadataReader reader, Dictionary<string, object> values, string? getterOf)
    {
        private readonly Stack<object> _stack = new();

        public void Run(BlobReader il)
        {
            while (il.RemainingBytes > 0)
            {
                int op = il.ReadByte();
                if (op == 0xFE) op = 0xFE00 | il.ReadByte();

                switch (op)
                {
                    case 0x00: // nop
                        break;
                    case >= 0x02 and <= 0x05: // ldarg.0-3
                    case >= 0x06 and <= 0x09: // ldloc.0-3
                        _stack.Push(Unknown.Value);
                        break;
                    case 0x0E: // ldarg.s
                    case 0x11: // ldloc.s
                        il.ReadByte();
                        _stack.Push(Unknown.Value);
                        break;
                    case >= 0x0A and <= 0x0D: // stloc.0-3
                        Pop();
                        break;
                    case 0x13: // stloc.s
                        il.ReadByte();
                        Pop();
                        break;
                    case 0x14: // ldnull
                        _stack.Push(Null.Value);
                        break;
                    case >= 0x15 and <= 0x1E: // ldc.i4.m1 .. ldc.i4.8
                        _stack.Push(op - 0x16);
                        break;
                    case 0x1F: // ldc.i4.s
                        _stack.Push((int)(sbyte)il.ReadByte());
                        break;
                    case 0x20: // ldc.i4
                        _stack.Push(il.ReadInt32());
                        break;
                    case 0x25: // dup
                        _stack.Push(_stack.Count > 0 ? _stack.Peek() : Unknown.Value);
                        break;
                    case 0x26: // pop
                        Pop();
                        break;
                    case 0x2A: // ret
                        if (getterOf is not null && _stack.Count > 0) Assign(getterOf, _stack.Peek());
                        return;
                    case 0x72: // ldstr
                        _stack.Push(reader.GetUserString(MetadataTokens.UserStringHandle(il.ReadInt32() & 0xFFFFFF)));
                        break;
                    case 0x73: // newobj
                        NewObject(MetadataTokens.EntityHandle(il.ReadInt32()));
                        break;
                    case 0x28: // call
                    case 0x6F: // callvirt
                        Call(MetadataTokens.EntityHandle(il.ReadInt32()));
                        break;
                    case 0x7B: // ldfld
                        il.ReadInt32();
                        Pop();
                        _stack.Push(Unknown.Value);
                        break;
                    case 0x7E: // ldsfld
                        il.ReadInt32();
                        _stack.Push(Unknown.Value);
                        break;
                    case 0x7D: // stfld
                        StoreField(MetadataTokens.EntityHandle(il.ReadInt32()));
                        break;
                    case >= 0x2B and <= 0x45: // every branch and switch
                        return;
                    default:
                        if (!SkipOperand(ref il, op)) return;
                        _stack.Clear();
                        break;
                }
            }
        }

        private object Pop() => _stack.Count > 0 ? _stack.Pop() : Unknown.Value;

        private object[] PopArguments(int count)
        {
            var arguments = new object[count];
            for (var i = count - 1; i >= 0; i--) arguments[i] = Pop();
            return arguments;
        }

        private void NewObject(EntityHandle constructor)
        {
            var (parent, signature) = MethodInfoOf(constructor);
            var arguments = PopArguments(signature.ParameterCount);

            object result = TypeNameOf(parent) switch
            {
                ("SemanticVersioning", "Version") => VersionFrom(arguments),
                ("SemanticVersioning", "Range") => arguments.Length > 0 && arguments[0] is string range
                    ? new RangeValue(range)
                    : Unknown.Value,
                ("System.Collections.Generic", "Dictionary`2") when arguments.Length == 0 => new DictionaryValue(),
                _ => Unknown.Value,
            };

            _stack.Push(result);
        }

        private static object VersionFrom(object[] arguments)
        {
            if (arguments.Length >= 1 && arguments[0] is string text) return new VersionValue(text);

            if (arguments.Length >= 3 && arguments[0] is int major && arguments[1] is int minor && arguments[2] is int patch)
            {
                var result = $"{major}.{minor}.{patch}";
                if (arguments.Length > 3 && arguments[3] is string preRelease && preRelease.Length > 0) result += "-" + preRelease;
                if (arguments.Length > 4 && arguments[4] is string build && build.Length > 0) result += "+" + build;
                return new VersionValue(result);
            }

            return Unknown.Value;
        }

        private void Call(EntityHandle method)
        {
            var (_, signature) = MethodInfoOf(method);
            var arguments = PopArguments(signature.ParameterCount);
            var target = signature.HasThis ? Pop() : null;

            if (target is DictionaryValue dictionary
                && MethodNameOf(method) is "Add" or "set_Item"
                && arguments.Length == 2
                && arguments[0] is string key)
            {
                var value = arguments[1] switch
                {
                    RangeValue range => range.Text,
                    string text => text,
                    _ => null,
                };

                dictionary.Entries.Add(new KeyValuePair<string, string?>(key, value));
            }

            if (!signature.ReturnsVoid) _stack.Push(Unknown.Value);
        }

        private void StoreField(EntityHandle field)
        {
            var value = Pop();
            Pop();

            var name = field.Kind switch
            {
                HandleKind.FieldDefinition => reader.GetString(reader.GetFieldDefinition((FieldDefinitionHandle)field).Name),
                HandleKind.MemberReference => reader.GetString(reader.GetMemberReference((MemberReferenceHandle)field).Name),
                _ => null,
            };

            // <ModGuid>k__BackingField is the compiler's name for an auto-property's storage.
            if (name is null || !name.StartsWith('<')) return;

            var end = name.IndexOf(">k__BackingField", StringComparison.Ordinal);
            if (end > 1) Assign(name[1..end], value);
        }

        private void Assign(string property, object value)
        {
            if (value is Unknown or Null) return;
            values[property] = value;
        }

        private string? MethodNameOf(EntityHandle handle) => handle.Kind switch
        {
            HandleKind.MemberReference => reader.GetString(reader.GetMemberReference((MemberReferenceHandle)handle).Name),
            HandleKind.MethodDefinition => reader.GetString(reader.GetMethodDefinition((MethodDefinitionHandle)handle).Name),
            HandleKind.MethodSpecification => MethodNameOf(reader.GetMethodSpecification((MethodSpecificationHandle)handle).Method),
            _ => null,
        };

        private (EntityHandle Parent, SignatureShape Signature) MethodInfoOf(EntityHandle handle)
        {
            switch (handle.Kind)
            {
                case HandleKind.MemberReference:
                {
                    var memberRef = reader.GetMemberReference((MemberReferenceHandle)handle);
                    return (memberRef.Parent, SignatureShape.Read(reader, memberRef.Signature));
                }
                case HandleKind.MethodDefinition:
                {
                    var method = reader.GetMethodDefinition((MethodDefinitionHandle)handle);
                    return (method.GetDeclaringType(), SignatureShape.Read(reader, method.Signature));
                }
                case HandleKind.MethodSpecification:
                    return MethodInfoOf(reader.GetMethodSpecification((MethodSpecificationHandle)handle).Method);
                default:
                    return (default, SignatureShape.Unreadable);
            }
        }

        // Namespace and name of a type handle, looking through a generic instantiation
        // (Dictionary<string, Range> is a TypeSpecification over Dictionary`2).
        private (string? Namespace, string? Name) TypeNameOf(EntityHandle handle)
        {
            switch (handle.Kind)
            {
                case HandleKind.TypeReference:
                {
                    var typeRef = reader.GetTypeReference((TypeReferenceHandle)handle);
                    return (reader.GetString(typeRef.Namespace), reader.GetString(typeRef.Name));
                }
                case HandleKind.TypeDefinition:
                {
                    var typeDef = reader.GetTypeDefinition((TypeDefinitionHandle)handle);
                    return (reader.GetString(typeDef.Namespace), reader.GetString(typeDef.Name));
                }
                case HandleKind.TypeSpecification:
                {
                    var blob = reader.GetBlobReader(reader.GetTypeSpecification((TypeSpecificationHandle)handle).Signature);
                    if (blob.ReadSignatureHeader().RawValue != (byte)SignatureTypeCode.GenericTypeInstance) return (null, null);
                    blob.ReadSignatureTypeCode();
                    return TypeNameOf(blob.ReadTypeHandle());
                }
                default:
                    return (null, null);
            }
        }

        // Skips the operand of an opcode Run doesn't evaluate. False if the opcode isn't known, since
        // the walk can't stay aligned past it.
        private static bool SkipOperand(ref BlobReader il, int op)
        {
            var size = op switch
            {
                0x0F or 0x10 or 0x12 => 1, // ldarga.s, starg.s, ldloca.s
                0x21 or 0x23 => 8, // ldc.i8, ldc.r8
                0x22 => 4, // ldc.r4
                0x27 or 0x29 or 0x70 or 0x71 or 0x74 or 0x75 or 0x79 or 0x7C or 0x7F or 0x80 or 0x81
                    or 0x8C or 0x8D or 0x8F or 0xA3 or 0xA4 or 0xA5 or 0xC2 or 0xC6 or 0xD0 => 4,
                0xFE06 or 0xFE07 or 0xFE15 or 0xFE16 or 0xFE1C => 4, // ldftn, ldvirtftn, initobj, constrained., sizeof
                0xFE09 or 0xFE0A or 0xFE0B or 0xFE0C or 0xFE0D or 0xFE0E => 2, // ldarg, ldarga, starg, ldloc, ldloca, stloc
                0xFE12 or 0xFE19 => 1, // unaligned., no.
                >= 0x46 and <= 0x6E => 0, // loads, stores, arithmetic, conversions
                >= 0x82 and <= 0x8B or >= 0x90 and <= 0xA2 or 0x8E => 0, // conversions, element access, ldlen
                >= 0xB3 and <= 0xBA or 0xC3 or >= 0xD1 and <= 0xDC or 0x01 or 0x24 or 0x78 or 0x7A => 0,
                0xFE00 or 0xFE01 or 0xFE02 or 0xFE03 or 0xFE04 or 0xFE05 or 0xFE0F or 0xFE11 or 0xFE13 or 0xFE14
                    or 0xFE17 or 0xFE18 or 0xFE1A or 0xFE1D or 0xFE1E => 0,
                _ => -1,
            };

            if (size < 0) return false;

            il.Offset += size;
            return true;
        }
    }

    private readonly record struct SignatureShape(bool HasThis, int ParameterCount, bool ReturnsVoid)
    {
        public static SignatureShape Unreadable { get; } = new(false, 0, false);

        public static SignatureShape Read(MetadataReader reader, BlobHandle handle)
        {
            var blob = reader.GetBlobReader(handle);
            var header = blob.ReadSignatureHeader();
            if (header.IsGeneric) blob.ReadCompressedInteger();

            var parameterCount = blob.ReadCompressedInteger();
            var returnsVoid = blob.ReadSignatureTypeCode() == SignatureTypeCode.Void;

            return new SignatureShape(header.IsInstance, parameterCount, returnsVoid);
        }
    }
}
