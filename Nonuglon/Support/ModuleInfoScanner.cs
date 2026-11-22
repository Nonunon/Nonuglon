using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Nonuglon.Support;

/// <summary>Reads Boss Mod-style [ModuleInfo] attributes straight from a DLL's
/// metadata, without loading or running it. Used for BossModReborn, which
/// can't be loaded alongside vbm.</summary>
public static class ModuleInfoScanner
{
    public readonly record struct Entry(string GroupType, uint GroupId, int Maturity, string MaturityName);

    private const string AttributeName = "BossMod.ModuleInfoAttribute";
    private const string InfoClass = "BossMod.BossModuleInfo";

    public static List<Entry> Scan(string path)
    {
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        var md = pe.GetMetadataReader();
        var enums = ReadEnums(md);
        var provider = new TypeProvider(enums);
        enums.TryGetValue($"{InfoClass}+GroupType", out var groupTypes);
        enums.TryGetValue($"{InfoClass}+Maturity", out var maturities);

        var result = new List<Entry>();
        foreach (var handle in md.CustomAttributes)
        {
            var attr = md.GetCustomAttribute(handle);
            if (attr.Parent.Kind != HandleKind.TypeDefinition || AttributeTypeName(md, attr) != AttributeName) continue;

            var value = attr.DecodeValue(provider);
            var maturity = value.FixedArguments.Length > 0 ? Convert.ToInt32(value.FixedArguments[0].Value) : -1;
            var groupType = 0;
            uint groupId = 0;
            foreach (var arg in value.NamedArguments)
            {
                if (arg.Name == "GroupType") groupType = Convert.ToInt32(arg.Value);
                else if (arg.Name == "GroupID") groupId = Convert.ToUInt32(arg.Value);
            }
            result.Add(new(Name(groupTypes, groupType), groupId, maturity, Name(maturities, maturity)));
        }
        return result;
    }

    private static string Name(EnumInfo? e, long value) =>
        e != null && e.Names.TryGetValue(value, out var name) ? name : value.ToString();

    private static string? AttributeTypeName(MetadataReader md, CustomAttribute attr) => attr.Constructor.Kind switch
    {
        HandleKind.MethodDefinition => FullName(md, md.GetMethodDefinition((MethodDefinitionHandle)attr.Constructor).GetDeclaringType()),
        HandleKind.MemberReference => md.GetMemberReference((MemberReferenceHandle)attr.Constructor).Parent is { Kind: HandleKind.TypeReference } p
            ? FullName(md, (TypeReferenceHandle)p) : null,
        _ => null,
    };

    private static string FullName(MetadataReader md, TypeDefinitionHandle handle)
    {
        var t = md.GetTypeDefinition(handle);
        var declaring = t.GetDeclaringType();
        return declaring.IsNil
            ? $"{md.GetString(t.Namespace)}.{md.GetString(t.Name)}"
            : $"{FullName(md, declaring)}+{md.GetString(t.Name)}";
    }

    private static string FullName(MetadataReader md, TypeReferenceHandle handle)
    {
        var t = md.GetTypeReference(handle);
        return t.ResolutionScope.Kind == HandleKind.TypeReference
            ? $"{FullName(md, (TypeReferenceHandle)t.ResolutionScope)}+{md.GetString(t.Name)}"
            : $"{md.GetString(t.Namespace)}.{md.GetString(t.Name)}";
    }

    private sealed class EnumInfo
    {
        public PrimitiveTypeCode Underlying = PrimitiveTypeCode.Int32;
        public readonly Dictionary<long, string> Names = [];
    }

    // Underlying type of every enum (attribute decoding needs it), value names only for BossModuleInfo's.
    private static Dictionary<string, EnumInfo> ReadEnums(MetadataReader md)
    {
        var enums = new Dictionary<string, EnumInfo>();
        foreach (var handle in md.TypeDefinitions)
        {
            var t = md.GetTypeDefinition(handle);
            if (t.BaseType.Kind != HandleKind.TypeReference || FullName(md, (TypeReferenceHandle)t.BaseType) != "System.Enum") continue;

            var name = FullName(md, handle);
            var withNames = name.StartsWith(InfoClass + "+", StringComparison.Ordinal);
            EnumInfo? info = null;
            foreach (var fh in t.GetFields())
            {
                var f = md.GetFieldDefinition(fh);
                if (md.GetString(f.Name) == "value__")
                {
                    var sig = md.GetBlobReader(f.Signature);
                    sig.ReadByte(); // FIELD calling convention
                    info ??= new EnumInfo();
                    info.Underlying = (PrimitiveTypeCode)sig.ReadByte();
                    if (!withNames) break;
                }
                else if (withNames && !f.GetDefaultValue().IsNil)
                {
                    var c = md.GetConstant(f.GetDefaultValue());
                    var v = md.GetBlobReader(c.Value).ReadConstant(c.TypeCode);
                    info ??= new EnumInfo();
                    info.Names[Convert.ToInt64(v)] = md.GetString(f.Name);
                }
            }
            if (info != null) enums[name] = info;
        }
        return enums;
    }

    private sealed class TypeProvider(Dictionary<string, EnumInfo> enums) : ICustomAttributeTypeProvider<string>
    {
        public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode.ToString();
        public string GetSystemType() => "System.Type";
        public string GetSZArrayType(string elementType) => elementType + "[]";
        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) => FullName(reader, handle);
        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) => FullName(reader, handle);
        public string GetTypeFromSerializedName(string? name) => name?.Split(',')[0].Trim() ?? "";
        public bool IsSystemType(string type) => type == "System.Type";
        public PrimitiveTypeCode GetUnderlyingEnumType(string type) =>
            enums.TryGetValue(type, out var e) ? e.Underlying : PrimitiveTypeCode.Int32;
    }
}
