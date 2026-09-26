using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

/// <summary>What an SPT 4 server mod's DLL says about itself - the same values the SPT server reads
/// and logs when it loads the mod ("Mod: Dynamic Maps version: 1.2.1 (GUID: com.mpstark.dynamicmaps
/// | targets SPT: ~4.1.0)"). Each value is null when the DLL does not state it as a plain literal.</summary>
/// <param name="Dependencies">Its ModDependencies: other server mods' GUIDs, all of them hard.</param>
public sealed record ServerModMetadata(
    string? Guid,
    string? Name,
    string? Author,
    string? Version,
    string? SptVersion,
    IReadOnlyList<ModDependencyRef> Dependencies);

//
// An SPT 4 server mod is a .NET DLL with no package.json. It describes itself with a class of its
// own - implementing SPTarkov.Server.Core.Models.Spt.Mod.IModMetadata (4.1), or deriving from
// AbstractModMetadata (4.0) - whose properties are set to literals:
//
//     public override string ModGuid { get; init; } = "com.mpstark.dynamicmaps";
//     public override Version Version { get; init; } = new("1.2.1");
//     public override Dictionary<string, Range>? ModDependencies { get; init; } = new() { { "com.wtt.commonlib", new("~3.0.3") } };
//
// The compiler turns each of those into a few instructions in the class's constructor - load the
// literal, maybe wrap it in a Version or Range, store it in the property's backing field - and that
// is read here, from the DLL's metadata and IL, without loading or running it.
//
// Only a value set that plainly is taken. Anything worked out at run time (a version read from the
// assembly, a string built up) is left null, and the caller falls back to what it had before - the
// DLL's file version, which for the mod checked here was not the mod's version at all (Dynamic
// Maps' server DLL reports 1.0.4.0; the mod, and SPT, say 1.2.1).
//
public static class ServerModMetadataReader
{
    private const string SptModNamespace = "SPTarkov.Server.Core.Models.Spt.Mod";

    /// <summary>The metadata in <paramref name="dllPath"/>, or null when it holds no SPT mod
    /// metadata class (not a server mod's main DLL, not managed code, unreadable).</summary>
    public static ServerModMetadata? Read(string dllPath)
    {
        try
        {
            using var stream = File.OpenRead(dllPath);
            using var pe = new PEReader(stream);
            if (!pe.HasMetadata) return null;

            var reader = pe.GetMetadataReader();
            foreach (var handle in reader.TypeDefinitions)
            {
                var type = reader.GetTypeDefinition(handle);
                if (!IsModMetadata(reader, type)) continue;

                return ReadType(pe, reader, handle, type);
            }

            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or BadImageFormatException
                                       or InvalidOperationException or ArgumentException)
        {
            return null;
        }
    }

    private static bool IsModMetadata(MetadataReader reader, TypeDefinition type)
    {
        foreach (var implementation in type.GetInterfaceImplementations())
        {
            if (NameOf(reader, reader.GetInterfaceImplementation(implementation).Interface) is ("IModMetadata", var ns)
                && ns == SptModNamespace)
            {
                return true;
            }
        }

        return NameOf(reader, type.BaseType) is ("AbstractModMetadata", var baseNs) && baseNs == SptModNamespace;
    }

    private static (string Name, string Namespace)? NameOf(MetadataReader reader, EntityHandle handle)
    {
        // <Module> and interfaces have no base type.
        if (handle.IsNil) return null;

        switch (handle.Kind)
        {
            case HandleKind.TypeReference:
                var reference = reader.GetTypeReference((TypeReferenceHandle)handle);
                return (reader.GetString(reference.Name), reader.GetString(reference.Namespace));
            case HandleKind.TypeDefinition:
                var definition = reader.GetTypeDefinition((TypeDefinitionHandle)handle);
                return (reader.GetString(definition.Name), reader.GetString(definition.Namespace));
            default:
                return null;
        }
    }

    private static ServerModMetadata ReadType(PEReader pe, MetadataReader reader, TypeDefinitionHandle handle, TypeDefinition type)
    {
        var values = new Dictionary<string, List<Instruction>>(StringComparer.Ordinal);

        foreach (var methodHandle in type.GetMethods())
        {
            var method = reader.GetMethodDefinition(methodHandle);
            if (method.RelativeVirtualAddress == 0) continue;

            var name = reader.GetString(method.Name);

            // The parameterless constructor, where the initialisers are. A record also has a copy
            // constructor, which only copies fields and says nothing.
            if (name == ".ctor" && ParameterCount(reader, method) == 0)
            {
                var il = Decode(reader, pe.GetMethodBody(method.RelativeVirtualAddress).GetILReader());
                var start = 0;
                for (var i = 0; i < il.Count; i++)
                {
                    if (il[i].Op != Stfld) continue;

                    if (BackingFieldOf(reader, handle, il[i].Token) is { } property)
                        values.TryAdd(property, il.GetRange(start, i - start));
                    start = i + 1;
                }
            }
        }

        // An expression-bodied property ("public string Name => \"x\";") has no backing field; its
        // getter returns the value instead. Only where the constructor gave nothing.
        foreach (var methodHandle in type.GetMethods())
        {
            var method = reader.GetMethodDefinition(methodHandle);
            var name = reader.GetString(method.Name);
            if (method.RelativeVirtualAddress == 0 || !name.StartsWith("get_", StringComparison.Ordinal)) continue;

            var property = name[4..];
            if (values.ContainsKey(property)) continue;

            var il = Decode(reader, pe.GetMethodBody(method.RelativeVirtualAddress).GetILReader());
            if (il.Count > 0 && il[^1].Op == Ret) values[property] = il.GetRange(0, il.Count - 1);
        }

        string? Text(string property) =>
            values.TryGetValue(property, out var il) ? StringValue(il) : null;

        string? Wrapped(string property, string typeName) =>
            values.TryGetValue(property, out var il) ? WrappedValue(reader, il, typeName) : null;

        var dependencies = values.TryGetValue("ModDependencies", out var deps) ? Dependencies(reader, deps) : [];

        return new ServerModMetadata(
            Blank(Text("ModGuid")),
            Blank(Text("Name")),
            Blank(Text("Author")),
            Blank(Wrapped("Version", "Version")),
            Blank(Wrapped("SptVersion", "Range")),
            dependencies);
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    // "ldarg.0; ldstr "x"" before the store: the literal. Anything else was worked out.
    private static string? StringValue(List<Instruction> il)
    {
        var body = WithoutThis(il);
        return body is [{ Op: Ldstr } only] ? only.Text : null;
    }

    //
    // A Version or Range made from a literal:
    //     ldstr "1.2.1"; [ldc.i4 loose]; newobj Version::.ctor(string, bool)
    //     ldstr "1.2.1"; [ldc.i4 loose]; call Version::Parse(string, bool)
    //     ldc.i4 1; ldc.i4 2; ldc.i4 1; [ldnull|ldstr pre-release; ldnull|ldstr build]; newobj Version::.ctor(int, int, int, ...)
    //
    private static string? WrappedValue(MetadataReader reader, List<Instruction> il, string typeName)
    {
        var body = WithoutThis(il);
        if (body.Count < 2 || body[^1].Op is not (Newobj or Call)) return null;

        var (declaring, member) = MethodName(reader, body[^1].Token);
        if (declaring != typeName) return null;
        if (body[^1].Op == Newobj ? member != ".ctor" : member != "Parse") return null;

        var arguments = body.GetRange(0, body.Count - 1);

        if (arguments is [{ Op: Ldstr }] or [{ Op: Ldstr }, { IsInt: true }])
            return arguments[0].Text;

        if (typeName == "Version" && body[^1].Op == Newobj
            && arguments.Count is >= 3 and <= 5
            && arguments.Take(3).All(a => a.IsInt)
            && arguments.Skip(3).All(a => a.Op is Ldnull or Ldstr))
        {
            var version = $"{arguments[0].Int}.{arguments[1].Int}.{arguments[2].Int}";
            if (arguments.Count > 3 && arguments[3] is { Op: Ldstr, Text: { Length: > 0 } pre }) version += "-" + pre;
            if (arguments.Count > 4 && arguments[4] is { Op: Ldstr, Text: { Length: > 0 } build }) version += "+" + build;
            return version;
        }

        return null;
    }

    //
    // A dictionary filled where it is made:
    //     newobj Dictionary::.ctor; { dup; ldstr "guid"; <a Range>; callvirt Add | set_Item }*
    // Each Add or indexer store takes the first string loaded since the one before it: the GUID.
    // Anything not of that shape gives nothing rather than a wrong list.
    //
    private static List<ModDependencyRef> Dependencies(MetadataReader reader, List<Instruction> il)
    {
        var body = WithoutThis(il);
        if (body.Count == 0 || body[0].Op != Newobj) return [];

        var found = new List<ModDependencyRef>();
        string? key = null;
        var sawAny = false;

        for (var i = 1; i < body.Count; i++)
        {
            var instruction = body[i];
            if (instruction.Op == Ldstr)
            {
                key ??= instruction.Text;
                continue;
            }

            if (instruction.Op is Call or Callvirt && MethodName(reader, instruction.Token).Member is "Add" or "set_Item")
            {
                if (string.IsNullOrWhiteSpace(key)) return [];

                found.Add(new ModDependencyRef(key.Trim(), IsSoft: false));
                key = null;
                sawAny = true;
            }
        }

        return sawAny && key is null ? found : [];
    }

    private static List<Instruction> WithoutThis(List<Instruction> il) =>
        il is [{ Op: Ldarg0 }, ..] ? il.GetRange(1, il.Count - 1) : il;

    // The property a backing field of this type stands for ("<Version>k__BackingField" -> "Version").
    private static string? BackingFieldOf(MetadataReader reader, TypeDefinitionHandle owner, int token)
    {
        var handle = MetadataTokens.EntityHandle(token);
        if (handle.Kind != HandleKind.FieldDefinition) return null;

        var field = reader.GetFieldDefinition((FieldDefinitionHandle)handle);
        if (field.GetDeclaringType() != owner) return null;

        var name = reader.GetString(field.Name);
        const string suffix = ">k__BackingField";
        return name.StartsWith('<') && name.EndsWith(suffix, StringComparison.Ordinal)
            ? name[1..^suffix.Length]
            : null;
    }

    private static (string? Declaring, string? Member) MethodName(MetadataReader reader, int token)
    {
        var handle = MetadataTokens.EntityHandle(token);
        switch (handle.Kind)
        {
            case HandleKind.MemberReference:
                var reference = reader.GetMemberReference((MemberReferenceHandle)handle);
                var parent = reference.Parent.Kind is HandleKind.TypeReference or HandleKind.TypeDefinition
                    ? NameOf(reader, (EntityHandle)reference.Parent)?.Name
                    : null;
                return (parent, reader.GetString(reference.Name));
            case HandleKind.MethodDefinition:
                var definition = reader.GetMethodDefinition((MethodDefinitionHandle)handle);
                return (reader.GetString(reader.GetTypeDefinition(definition.GetDeclaringType()).Name), reader.GetString(definition.Name));
            case HandleKind.MethodSpecification:
                var specification = reader.GetMethodSpecification((MethodSpecificationHandle)handle);
                return MethodName(reader, MetadataTokens.GetToken(specification.Method));
            default:
                return (null, null);
        }
    }

    private static int ParameterCount(MetadataReader reader, MethodDefinition method)
    {
        var signature = reader.GetBlobReader(method.Signature);
        var header = signature.ReadSignatureHeader();
        if (header.IsGeneric) signature.ReadCompressedInteger();
        return signature.ReadCompressedInteger();
    }

    // ---- IL ----

    private const int Ldarg0 = 0x02, Ldnull = 0x14, Ldstr = 0x72, Stfld = 0x7D, Newobj = 0x73,
        Call = 0x28, Callvirt = 0x6F, Ret = 0x2A;

    private readonly record struct Instruction(int Op, int Token, string? Text, int? Int)
    {
        public bool IsInt => Int is not null;
    }

    private static List<Instruction> Decode(MetadataReader metadata, BlobReader il)
    {
        var instructions = new List<Instruction>();
        var reader = il;

        while (reader.RemainingBytes > 0)
        {
            int op = reader.ReadByte();
            if (op == 0xFE)
            {
                op = 0xFE00 | reader.ReadByte();
                SkipTwoByteOperand(ref reader, op);
                instructions.Add(new Instruction(op, 0, null, null));
                continue;
            }

            switch (op)
            {
                case Ldstr:
                    var token = reader.ReadInt32();
                    instructions.Add(new Instruction(op, token, metadata.GetUserString(MetadataTokens.UserStringHandle(token & 0x00FFFFFF)), null));
                    break;
                case >= 0x15 and <= 0x1E: // ldc.i4.m1 .. ldc.i4.8
                    instructions.Add(new Instruction(op, 0, null, op - 0x16));
                    break;
                case 0x1F: // ldc.i4.s
                    instructions.Add(new Instruction(op, 0, null, reader.ReadSByte()));
                    break;
                case 0x20: // ldc.i4
                    instructions.Add(new Instruction(op, 0, null, reader.ReadInt32()));
                    break;
                case 0x45: // switch
                    var count = reader.ReadUInt32();
                    for (var i = 0; i < count; i++) reader.ReadInt32();
                    instructions.Add(new Instruction(op, 0, null, null));
                    break;
                default:
                    var size = OperandSize(op);
                    var operand = size == 4 ? reader.ReadInt32() : 0;
                    if (size is not (0 or 4)) reader.Offset += size;
                    instructions.Add(new Instruction(op, operand, null, null));
                    break;
            }
        }

        return instructions;
    }

    private static int OperandSize(int op) => op switch
    {
        >= 0x0E and <= 0x13 => 1,       // ldarg.s .. stloc.s
        0x21 => 8,                      // ldc.i8
        0x22 => 4,                      // ldc.r4
        0x23 => 8,                      // ldc.r8
        0x27 or 0x28 or 0x29 => 4,      // jmp, call, calli
        >= 0x2B and <= 0x37 => 1,       // short branches
        >= 0x38 and <= 0x44 => 4,       // branches
        0x6F or 0x70 or 0x71 or 0x72 or 0x73 or 0x74 or 0x75 or 0x79 => 4,
        >= 0x7B and <= 0x81 => 4,       // field access, stobj
        0x8C or 0x8D or 0x8F => 4,      // box, newarr, ldelema
        0xA3 or 0xA4 or 0xA5 => 4,      // ldelem, stelem, unbox.any
        0xC2 or 0xC6 or 0xD0 => 4,      // refanyval, mkrefany, ldtoken
        0xDD => 4,                      // leave
        0xDE => 1,                      // leave.s
        _ => 0,
    };

    private static void SkipTwoByteOperand(ref BlobReader reader, int op)
    {
        var size = op switch
        {
            0xFE06 or 0xFE07 or 0xFE15 or 0xFE16 or 0xFE1C => 4, // ldftn, ldvirtftn, initobj, constrained., sizeof
            >= 0xFE09 and <= 0xFE0E => 2,                        // ldarg .. stloc
            0xFE12 or 0xFE19 => 1,                               // unaligned., no.
            _ => 0,
        };
        reader.Offset += size;
    }
}
