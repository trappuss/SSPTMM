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
    /// metadata class, or more than one (not a server mod's main DLL, not managed code, unreadable).</summary>
    public static ServerModMetadata? Read(string dllPath)
    {
        try
        {
            using var stream = File.OpenRead(dllPath);
            using var pe = new PEReader(stream);
            if (!pe.HasMetadata) return null;

            var reader = pe.GetMetadataReader();

            // The class SPT makes: concrete, and mod metadata itself or through a base class of its
            // own. Two such classes (or none) and which one counts cannot be told: nothing is read.
            var candidates = reader.TypeDefinitions
                .Where(h => !reader.GetTypeDefinition(h).Attributes.HasFlag(System.Reflection.TypeAttributes.Abstract)
                    && IsModMetadata(reader, h, depth: 0))
                .ToList();

            return candidates.Count == 1 ? ReadChain(pe, reader, candidates[0]) : null;
        }
        catch (Exception ex)
        {
            // Never a reason for the scan to stop: whatever this DLL is, it is read as saying nothing.
            AppLog.Debug("Scan", $"couldn't read server mod metadata from {Path.GetFileName(dllPath)}: {ex.Message}");
            return null;
        }
    }

    private static bool IsModMetadata(MetadataReader reader, TypeDefinitionHandle handle, int depth)
    {
        if (depth > 8) return false;

        var type = reader.GetTypeDefinition(handle);
        foreach (var implementation in type.GetInterfaceImplementations())
        {
            if (NameOf(reader, reader.GetInterfaceImplementation(implementation).Interface) is ("IModMetadata", var ns)
                && ns == SptModNamespace)
            {
                return true;
            }
        }

        if (NameOf(reader, type.BaseType) is ("AbstractModMetadata", var baseNs) && baseNs == SptModNamespace) return true;

        // Through a base class in the same DLL.
        return type.BaseType.Kind == HandleKind.TypeDefinition && !type.BaseType.IsNil
            && IsModMetadata(reader, (TypeDefinitionHandle)type.BaseType, depth + 1);
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

    //
    // Every place a property is given a value, in the class and the base classes it has in the same
    // DLL: an initialiser or a constructor assignment (a store to the property's backing field, or
    // a call of its setter), or an expression-bodied getter. A property given a value in exactly one
    // place is read from it; given one in more than one place (a base class's default the class
    // overrides, an initialiser the constructor then replaces), which one wins is a matter of run
    // time order - it is left unread rather than guessed.
    //
    private static ServerModMetadata ReadChain(PEReader pe, MetadataReader reader, TypeDefinitionHandle handle)
    {
        var writes = new Dictionary<string, List<List<Instruction>>>(StringComparer.Ordinal);

        void Add(string property, List<Instruction> value)
        {
            if (!writes.TryGetValue(property, out var list)) writes[property] = list = [];
            list.Add(value);
        }

        // The class and its base classes in this DLL: only their own setters are stores to it.
        var chain = new HashSet<TypeDefinitionHandle>();
        for (var (step, depth) = (handle, 0); depth <= 8; depth++)
        {
            chain.Add(step);
            var baseType = reader.GetTypeDefinition(step).BaseType;
            if (baseType.IsNil || baseType.Kind != HandleKind.TypeDefinition) break;
            step = (TypeDefinitionHandle)baseType;
        }

        var current = handle;
        for (var depth = 0; depth <= 8; depth++)
        {
            var type = reader.GetTypeDefinition(current);

            foreach (var methodHandle in type.GetMethods())
            {
                var method = reader.GetMethodDefinition(methodHandle);
                if (method.RelativeVirtualAddress == 0) continue;

                var name = reader.GetString(method.Name);
                var il = Decode(reader, pe.GetMethodBody(method.RelativeVirtualAddress).GetILReader());

                // The parameterless constructor, where the initialisers are. A record also has a copy
                // constructor, which only copies fields and says nothing.
                if (name == ".ctor" && ParameterCount(reader, method) == 0)
                {
                    var start = 0;
                    for (var i = 0; i < il.Count; i++)
                    {
                        var property = il[i].Op == Stfld ? BackingFieldOf(reader, il[i].Token)
                            : il[i].Op is Call or Callvirt ? SetterOf(reader, il[i].Token, chain)
                            : null;

                        if (property is not null)
                        {
                            var region = il.GetRange(start, i - start);

                            // A store to this object ("ldarg.0; <value>; stfld/call"), made exactly once:
                            // one a branch can skip or repeat may not be, and one to another object is
                            // not this one's. Either still counts as a place the property is set, with
                            // no value.
                            Add(property, !MaybeSkipped(il, i) && region is [{ Op: Ldarg0 }, ..] ? region : []);
                        }

                        // Each store, and the call of the base constructor, ends the one before.
                        if (property is not null || il[i].Op == Stfld
                            || (il[i].Op == Call && MethodName(reader, il[i].Token).Member == ".ctor"))
                        {
                            start = i + 1;
                        }
                    }
                }
                else if (name.StartsWith("get_", StringComparison.Ordinal) && il is [.., { Op: Ret }]
                         && il is not [{ Op: Ldarg0 }, { Op: Ldfld }, { Op: Ret }])
                {
                    // An expression-bodied property ("public override string Name => \"x\";").
                    Add(name[4..], il.GetRange(0, il.Count - 1));
                }
            }

            if (type.BaseType.IsNil || type.BaseType.Kind != HandleKind.TypeDefinition) break;
            current = (TypeDefinitionHandle)type.BaseType;
        }

        List<Instruction>? Only(string property) =>
            writes.TryGetValue(property, out var list) && list.Count == 1 ? list[0] : null;

        string? Text(string property) => Only(property) is { } il ? StringValue(il) : null;

        string? Wrapped(string property, string typeName) => Only(property) is { } il ? WrappedValue(reader, il, typeName) : null;

        var dependencies = Only("ModDependencies") is { } deps ? Dependencies(reader, deps) : [];

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
    // A dictionary filled where it is made, one entry at a time:
    //     newobj Dictionary::.ctor; { dup; ldstr "guid"; <a Range>; callvirt Add | set_Item }*
    // Each entry's key is the literal straight after its dup. Anything not of exactly that shape -
    // a key that is not a literal, a dictionary filled some other way - gives nothing rather than
    // a wrong list.
    //
    private static List<ModDependencyRef> Dependencies(MetadataReader reader, List<Instruction> il)
    {
        var body = WithoutThis(il);
        if (body.Count == 0 || body[0].Op != Newobj) return [];

        var found = new List<ModDependencyRef>();
        var i = 1;
        while (i < body.Count)
        {
            // dup; ldstr "guid"
            if (body[i].Op != Dup || i + 1 >= body.Count || body[i + 1] is not { Op: Ldstr, Text: { } key } || string.IsNullOrWhiteSpace(key))
                return [];

            // Then the value, straight away: ldstr "~1.0"; [ldc.i4 loose]; newobj Range | call Range::Parse -
            // so a key that goes on to be changed ("com." + x, "X".ToLower()) is not taken as it began.
            var j = i + 2;
            if (j >= body.Count || body[j].Op != Ldstr) return [];
            j++;
            if (j < body.Count && body[j].IsInt) j++;
            if (j >= body.Count || !IsRange(reader, body[j])) return [];
            j++;

            // And the entry's own Add.
            if (j >= body.Count || !(body[j].Op is Call or Callvirt && MethodName(reader, body[j].Token).Member is "Add" or "set_Item"))
                return [];

            found.Add(new ModDependencyRef(key.Trim(), IsSoft: false));
            i = j + 1;
        }

        return found;
    }

    private static bool IsRange(MetadataReader reader, Instruction instruction) =>
        instruction.Op is Newobj or Call
        && MethodName(reader, instruction.Token) is ("Range", var member)
        && member == (instruction.Op == Newobj ? ".ctor" : "Parse");

    private static List<Instruction> WithoutThis(List<Instruction> il) =>
        il is [{ Op: Ldarg0 }, ..] ? il.GetRange(1, il.Count - 1) : il;

    // The property a backing field stands for ("<Version>k__BackingField" -> "Version").
    private static string? BackingFieldOf(MetadataReader reader, int token)
    {
        var handle = MetadataTokens.EntityHandle(token);
        var name = handle.Kind switch
        {
            HandleKind.FieldDefinition => reader.GetString(reader.GetFieldDefinition((FieldDefinitionHandle)handle).Name),
            HandleKind.MemberReference => reader.GetString(reader.GetMemberReference((MemberReferenceHandle)handle).Name),
            _ => null,
        };

        const string suffix = ">k__BackingField";
        return name is not null && name.StartsWith('<') && name.EndsWith(suffix, StringComparison.Ordinal)
            ? name[1..^suffix.Length]
            : null;
    }

    // The property a setter call stands for ("set_ModGuid" -> "ModGuid") - a setter of the class
    // itself or a base class of it in this DLL, or of SPT's metadata base - never another type's:
    // filling the dependencies dictionary calls its set_Item, and a static "Log.Author = ..." is
    // not the mod's author.
    private static string? SetterOf(MetadataReader reader, int token, HashSet<TypeDefinitionHandle> chain)
    {
        var handle = MetadataTokens.EntityHandle(token);
        var own = handle.Kind switch
        {
            HandleKind.MethodDefinition => chain.Contains(reader.GetMethodDefinition((MethodDefinitionHandle)handle).GetDeclaringType()),
            HandleKind.MemberReference => reader.GetMemberReference((MemberReferenceHandle)handle).Parent is var parent
                && parent.Kind is HandleKind.TypeReference
                && NameOf(reader, (EntityHandle)parent) is ("AbstractModMetadata" or "IModMetadata", SptModNamespace),
            _ => false,
        };

        return own && MethodName(reader, token).Member is { } member && member.StartsWith("set_", StringComparison.Ordinal)
            ? member[4..]
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

    private const int Nop = 0x00, Ldarg0 = 0x02, Ldnull = 0x14, Dup = 0x25, Ldstr = 0x72, Ldfld = 0x7B, Stfld = 0x7D,
        Newobj = 0x73, Call = 0x28, Callvirt = 0x6F, Ret = 0x2A;

    // One IL instruction: where it starts, and for a branch, where it can go.
    private readonly record struct Instruction(int Op, int Token, string? Text, int? Int, int Offset = 0, int[]? Targets = null)
    {
        public bool IsInt => Int is not null;
    }

    private static List<Instruction> Decode(MetadataReader metadata, BlobReader il)
    {
        var instructions = new List<Instruction>();
        var reader = il;

        while (reader.RemainingBytes > 0)
        {
            var at = reader.Offset;
            int op = reader.ReadByte();

            // A debug build's padding, between every step: nothing to read, and it would only split
            // the shapes looked for above.
            if (op == Nop) continue;

            if (op == 0xFE)
            {
                op = 0xFE00 | reader.ReadByte();
                SkipTwoByteOperand(ref reader, op);
                instructions.Add(new Instruction(op, 0, null, null, at));
                continue;
            }

            switch (op)
            {
                case Ldstr:
                    var token = reader.ReadInt32();
                    instructions.Add(new Instruction(op, token, metadata.GetUserString(MetadataTokens.UserStringHandle(token & 0x00FFFFFF)), null, at));
                    break;
                case >= 0x15 and <= 0x1E: // ldc.i4.m1 .. ldc.i4.8
                    instructions.Add(new Instruction(op, 0, null, op - 0x16, at));
                    break;
                case 0x1F: // ldc.i4.s
                    instructions.Add(new Instruction(op, 0, null, reader.ReadSByte(), at));
                    break;
                case 0x20: // ldc.i4
                    instructions.Add(new Instruction(op, 0, null, reader.ReadInt32(), at));
                    break;
                case (>= 0x2B and <= 0x37) or 0xDE: // short branches, leave.s
                {
                    var delta = reader.ReadSByte();
                    instructions.Add(new Instruction(op, 0, null, null, at, [reader.Offset + delta]));
                    break;
                }
                case (>= 0x38 and <= 0x44) or 0xDD: // branches, leave
                {
                    var delta = reader.ReadInt32();
                    instructions.Add(new Instruction(op, 0, null, null, at, [reader.Offset + delta]));
                    break;
                }
                case 0x45: // switch: relative to the end of the whole instruction
                {
                    var count = (int)reader.ReadUInt32();
                    var deltas = new int[count];
                    for (var i = 0; i < count; i++) deltas[i] = reader.ReadInt32();
                    var next = reader.Offset;
                    instructions.Add(new Instruction(op, 0, null, null, at, [.. deltas.Select(d => next + d)]));
                    break;
                }
                default:
                    var size = OperandSize(op);
                    var operand = size == 4 ? reader.ReadInt32() : 0;
                    if (size is not (0 or 4)) reader.Offset += size;
                    instructions.Add(new Instruction(op, operand, null, null, at));
                    break;
            }
        }

        return instructions;
    }

    //
    // Whether the instruction at <paramref name="index"/> might not run, or might run more than once:
    // a branch before it jumps past it, or one after it jumps back to or before it (a loop). A branch
    // that only lands on it, as a ternary's does, still leaves it running once.
    //
    private static bool MaybeSkipped(List<Instruction> il, int index)
    {
        var at = il[index].Offset;
        foreach (var branch in il)
        {
            if (branch.Targets is not { } targets) continue;
            if (branch.Offset < at && targets.Any(t => t > at)) return true;
            if (branch.Offset > at && targets.Any(t => t <= at)) return true;
        }

        return false;
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
