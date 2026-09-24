using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

// Usage: sigdump <dll> <TypeName>[,TypeName...] [methodNameRegex]
var path = args[0];
var types = args[1].Split(',');
var nameRx = args.Length > 2 ? new System.Text.RegularExpressions.Regex(args[2]) : null;

using var fs = File.OpenRead(path);
using var pe = new PEReader(fs);
var md = pe.GetMetadataReader();
var provider = new SigProvider(md);

foreach (var th in md.TypeDefinitions)
{
    var td = md.GetTypeDefinition(th);
    var name = md.GetString(td.Name);
    var full = name;
    if (td.IsNested) full = md.GetString(md.GetTypeDefinition(td.GetDeclaringType()).Name) + "+" + name;
    bool all = types.Length == 1 && types[0] == "*";
    if (!all && !types.Contains(full)) continue;

    foreach (var mh in td.GetMethods())
    {
        var m = md.GetMethodDefinition(mh);
        var mname = md.GetString(m.Name);
        if (nameRx != null && !nameRx.IsMatch(mname)) continue;
        var sig = m.DecodeSignature(provider, null);
        var pnames = m.GetParameters().Select(p => md.GetString(md.GetParameter(p).Name)).ToList();
        var ps = sig.ParameterTypes.Select((t, i) => $"{t} {(i < pnames.Count ? pnames[i] : "p" + i)}");
        Console.WriteLine($"{sig.ReturnType} {full}::{mname}({string.Join(", ", ps)})");

        if (args.Length > 3 && args[3] == "--il" && m.RelativeVirtualAddress != 0)
        {
            Disassemble(md, pe.GetMethodBody(m.RelativeVirtualAddress).GetILBytes()!);
            continue;
        }

        if (args.Length > 3 && args[3] == "--calls" && m.RelativeVirtualAddress != 0)
        {
            var body = pe.GetMethodBody(m.RelativeVirtualAddress);
            var il = body.GetILBytes()!;
            var seen = new HashSet<string>();
            for (int i = 0; i + 4 < il.Length; i++)
            {
                byte op = il[i];
                if (op != 0x28 && op != 0x6F && op != 0x73 && op != 0x7B && op != 0x7D && op != 0x7E && op != 0x80) continue; // call, callvirt, newobj, ldfld, stfld, ldsfld, stsfld
                int token = BitConverter.ToInt32(il, i + 1);
                string? desc = null;
                try
                {
                    var h = MetadataTokens.EntityHandle(token);
                    switch (h.Kind)
                    {
                        case HandleKind.MethodDefinition:
                        {
                            var callee = md.GetMethodDefinition((MethodDefinitionHandle)h);
                            var ct = md.GetTypeDefinition(callee.GetDeclaringType());
                            desc = $"{md.GetString(ct.Name)}::{md.GetString(callee.Name)}";
                            break;
                        }
                        case HandleKind.MemberReference:
                        {
                            var mr = md.GetMemberReference((MemberReferenceHandle)h);
                            string parent = mr.Parent.Kind == HandleKind.TypeReference ? md.GetString(md.GetTypeReference((TypeReferenceHandle)mr.Parent).Name)
                                          : mr.Parent.Kind == HandleKind.TypeDefinition ? md.GetString(md.GetTypeDefinition((TypeDefinitionHandle)mr.Parent).Name) : "?";
                            desc = $"{parent}::{md.GetString(mr.Name)}";
                            break;
                        }
                        case HandleKind.FieldDefinition:
                        {
                            var fd = md.GetFieldDefinition((FieldDefinitionHandle)h);
                            var ct = md.GetTypeDefinition(fd.GetDeclaringType());
                            desc = $"fld {md.GetString(ct.Name)}.{md.GetString(fd.Name)}";
                            break;
                        }
                    }
                }
                catch { }
                if (desc != null && seen.Add(desc)) Console.WriteLine($"    -> {desc}");
                i += 4;
            }
        }
    }
}

// --il: print a method's IL with resolved member names and branch targets, enough to read control flow.
static void Disassemble(MetadataReader md, byte[] il)
{
    var one = new Dictionary<int, System.Reflection.Emit.OpCode>();
    var two = new Dictionary<int, System.Reflection.Emit.OpCode>();
    foreach (var f in typeof(System.Reflection.Emit.OpCodes).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
    {
        var op = (System.Reflection.Emit.OpCode)f.GetValue(null)!;
        if (op.Size == 1) one[(byte)op.Value] = op; else two[op.Value & 0xFF] = op;
    }

    string Member(int token)
    {
        try
        {
            if ((token >> 24) == 0x70) return "\"" + md.GetUserString(MetadataTokens.UserStringHandle(token & 0xFFFFFF)) + "\"";
            var h = MetadataTokens.EntityHandle(token);
            switch (h.Kind)
            {
                case HandleKind.MethodDefinition:
                {
                    var d = md.GetMethodDefinition((MethodDefinitionHandle)h);
                    return md.GetString(md.GetTypeDefinition(d.GetDeclaringType()).Name) + "::" + md.GetString(d.Name);
                }
                case HandleKind.MemberReference:
                {
                    var r = md.GetMemberReference((MemberReferenceHandle)h);
                    var parent = r.Parent.Kind == HandleKind.TypeReference ? md.GetString(md.GetTypeReference((TypeReferenceHandle)r.Parent).Name)
                               : r.Parent.Kind == HandleKind.TypeDefinition ? md.GetString(md.GetTypeDefinition((TypeDefinitionHandle)r.Parent).Name) : "?";
                    return parent + "::" + md.GetString(r.Name);
                }
                case HandleKind.FieldDefinition:
                {
                    var fd = md.GetFieldDefinition((FieldDefinitionHandle)h);
                    return md.GetString(md.GetTypeDefinition(fd.GetDeclaringType()).Name) + "." + md.GetString(fd.Name);
                }
                case HandleKind.TypeDefinition: return md.GetString(md.GetTypeDefinition((TypeDefinitionHandle)h).Name);
                case HandleKind.TypeReference: return md.GetString(md.GetTypeReference((TypeReferenceHandle)h).Name);
            }
        }
        catch { }
        return $"tok:{token:X8}";
    }

    int pos = 0;
    while (pos < il.Length)
    {
        int start = pos;
        System.Reflection.Emit.OpCode op;
        if (il[pos] == 0xFE) { op = two[il[pos + 1]]; pos += 2; } else { op = one[il[pos]]; pos += 1; }
        string operand = "";
        switch (op.OperandType)
        {
            case System.Reflection.Emit.OperandType.InlineNone: break;
            case System.Reflection.Emit.OperandType.ShortInlineBrTarget: { sbyte d = (sbyte)il[pos]; pos += 1; operand = $"IL_{pos + d:X4}"; break; }
            case System.Reflection.Emit.OperandType.InlineBrTarget: { int d = BitConverter.ToInt32(il, pos); pos += 4; operand = $"IL_{pos + d:X4}"; break; }
            case System.Reflection.Emit.OperandType.ShortInlineI: operand = ((sbyte)il[pos]).ToString(); pos += 1; break;
            case System.Reflection.Emit.OperandType.ShortInlineVar: operand = "V_" + il[pos]; pos += 1; break;
            case System.Reflection.Emit.OperandType.InlineVar: operand = "V_" + BitConverter.ToInt16(il, pos); pos += 2; break;
            case System.Reflection.Emit.OperandType.InlineI: operand = BitConverter.ToInt32(il, pos).ToString(); pos += 4; break;
            case System.Reflection.Emit.OperandType.InlineI8: operand = BitConverter.ToInt64(il, pos).ToString(); pos += 8; break;
            case System.Reflection.Emit.OperandType.ShortInlineR: operand = BitConverter.ToSingle(il, pos).ToString("0.####"); pos += 4; break;
            case System.Reflection.Emit.OperandType.InlineR: operand = BitConverter.ToDouble(il, pos).ToString("0.####"); pos += 8; break;
            case System.Reflection.Emit.OperandType.InlineSwitch:
            {
                int n = BitConverter.ToInt32(il, pos); pos += 4;
                var targets = new List<string>();
                int basePos = pos + 4 * n;
                for (int i = 0; i < n; i++) { targets.Add($"IL_{basePos + BitConverter.ToInt32(il, pos):X4}"); pos += 4; }
                operand = "(" + string.Join(", ", targets) + ")"; break;
            }
            default: operand = Member(BitConverter.ToInt32(il, pos)); pos += 4; break; // method/field/type/string/sig tokens
        }
        Console.WriteLine($"    IL_{start:X4}: {op.Name,-14} {operand}");
    }
}

class SigProvider : ISignatureTypeProvider<string, object?>
{
    readonly MetadataReader _md;
    public SigProvider(MetadataReader md) => _md = md;
    public string GetArrayType(string e, ArrayShape s) => e + "[]";
    public string GetByReferenceType(string e) => e + "&";
    public string GetFunctionPointerType(MethodSignature<string> s) => "fnptr";
    public string GetGenericInstantiation(string g, ImmutableArray<string> a) => g + "<" + string.Join(",", a) + ">";
    public string GetGenericMethodParameter(object? c, int i) => "!!" + i;
    public string GetGenericTypeParameter(object? c, int i) => "!" + i;
    public string GetModifiedType(string m, string u, bool r) => u;
    public string GetPinnedType(string e) => e;
    public string GetPointerType(string e) => e + "*";
    public string GetPrimitiveType(PrimitiveTypeCode c) => c.ToString();
    public string GetSZArrayType(string e) => e + "[]";
    public string GetTypeFromDefinition(MetadataReader r, TypeDefinitionHandle h, byte k) { var t = r.GetTypeDefinition(h); return r.GetString(t.Name); }
    public string GetTypeFromReference(MetadataReader r, TypeReferenceHandle h, byte k) { var t = r.GetTypeReference(h); return r.GetString(t.Name); }
    public string GetTypeFromSpecification(MetadataReader r, object? c, TypeSpecificationHandle h, byte k) => r.GetTypeSpecification(h).DecodeSignature(this, c);
}
