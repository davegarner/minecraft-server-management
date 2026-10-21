using System.Reflection;
using System.Reflection.Emit;
using MinecraftServerManager.Features.Server;

namespace MinecraftServerManager.Tests;

/// <summary>
/// Enforces the dependency rules described in docs/wiki/Architecture.md. References are read from signatures and from
/// method bodies (IL), so a call to a static helper counts as a dependency just like a constructor parameter.
/// </summary>
public class ArchitectureTests
{
    private const string Root = "MinecraftServerManager.";
    private static readonly Assembly App = typeof(ServerProcessService).Assembly;
    private static readonly Lazy<HashSet<(string From, string To, string Detail)>> Edges = new(ScanDependencies);

    [Fact]
    public void ApplicationCode_DoesNotDependOnTheUiOrHost()
    {
        var violations = Violations(from => from is not ("Components" or "Endpoints" or "Hosting" or "Root"),
            to => to is "Components" or "Endpoints" or "Hosting");
        Assert.Empty(violations);
    }

    [Fact]
    public void CoreInfrastructureAndUtil_DoNotDependOnFeatures()
    {
        var violations = Violations(from => from is "Core" or "Infrastructure" or "Util", to => to.StartsWith("Features.", StringComparison.Ordinal));
        Assert.Empty(violations);
    }

    [Fact]
    public void CoreAndUtil_DoNotDependOnInfrastructure()
    {
        var violations = Violations(from => from is "Core" or "Util", to => to == "Infrastructure");
        Assert.Empty(violations);
    }

    [Fact]
    public void Features_HaveNoDependencyCycles()
    {
        var graph = Edges.Value
            .Where(e => e.From.StartsWith("Features.", StringComparison.Ordinal) && e.To.StartsWith("Features.", StringComparison.Ordinal))
            .GroupBy(e => e.From)
            .ToDictionary(g => g.Key, g => g.Select(e => e.To).ToHashSet());

        var visiting = new List<string>();
        var done = new HashSet<string>();
        string? cycle = null;
        void Visit(string node)
        {
            if (cycle is not null || done.Contains(node)) return;
            if (visiting.Contains(node))
            {
                cycle = string.Join(" -> ", visiting.SkipWhile(n => n != node).Append(node));
                return;
            }
            visiting.Add(node);
            foreach (var next in graph.GetValueOrDefault(node) ?? []) Visit(next);
            visiting.Remove(node);
            done.Add(node);
        }
        foreach (var node in graph.Keys) Visit(node);

        Assert.True(cycle is null, $"Feature dependency cycle: {cycle}");
    }

    [Fact]
    public void Scanner_SeesSignatureAndMethodBodyReferences()
    {
        // Guards the rules above against passing vacuously: these are known, allowed dependencies.
        Assert.Contains(Edges.Value, e => e is { From: "Features.Worlds", To: "Features.Backups" });       // constructor parameter
        Assert.Contains(Edges.Value, e => e is { From: "Features.Backups", To: "Core" });                  // static call in a method body
        Assert.Contains(Edges.Value, e => e is { From: "Components", To: "Features.Server" });             // Razor component
    }

    private static List<string> Violations(Func<string, bool> fromFilter, Func<string, bool> toFilter) =>
        Edges.Value.Where(e => fromFilter(e.From) && toFilter(e.To)).Select(e => e.Detail).Order().ToList();

    /// <summary>"Features.Server", "Core", "Infrastructure", "Components"... for a type in this app; null otherwise.</summary>
    private static string? Area(Type type)
    {
        var ns = type.Namespace;
        if (ns is null || !ns.StartsWith(Root, StringComparison.Ordinal)) return ns == "MinecraftServerManager" ? "Root" : null;
        var parts = ns[Root.Length..].Split('.');
        return parts[0] == "Features" && parts.Length > 1 ? $"Features.{parts[1]}" : parts[0];
    }

    private static HashSet<(string, string, string)> ScanDependencies()
    {
        var edges = new HashSet<(string, string, string)>();
        foreach (var type in App.GetTypes())
        {
            if (Area(type) is not { } from) continue;
            foreach (var referenced in ReferencedTypes(type).SelectMany(Expand))
            {
                if (Area(referenced) is { } to && to != from)
                    edges.Add((from, to, $"{type.FullName} -> {referenced.FullName}"));
            }
        }
        return edges;
    }

    private static IEnumerable<Type> ReferencedTypes(Type type)
    {
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        if (type.BaseType is { } baseType) yield return baseType;
        foreach (var i in type.GetInterfaces()) yield return i;
        foreach (var field in type.GetFields(all)) yield return field.FieldType;
        foreach (var method in type.GetMethods(all).Cast<MethodBase>().Concat(type.GetConstructors(all)))
        {
            if (method is MethodInfo info) yield return info.ReturnType;
            foreach (var parameter in method.GetParameters()) yield return parameter.ParameterType;
            foreach (var used in TypesUsedInBody(method)) yield return used;
        }
    }

    /// <summary>The element types of arrays and by-refs, and the arguments of generic types.</summary>
    private static IEnumerable<Type> Expand(Type type)
    {
        if (type.HasElementType) return Expand(type.GetElementType()!);
        if (type.IsGenericType && !type.IsGenericTypeDefinition)
            return type.GetGenericArguments().SelectMany(Expand).Prepend(type.GetGenericTypeDefinition());
        return [type];
    }

    private static readonly OpCode[] OneByteOpCodes = new OpCode[256];
    private static readonly OpCode[] TwoByteOpCodes = new OpCode[256];

    static ArchitectureTests()
    {
        foreach (var field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            var opCode = (OpCode)field.GetValue(null)!;
            var value = (ushort)opCode.Value;
            if (value < 0x100) OneByteOpCodes[value] = opCode;
            else if ((value & 0xFF00) == 0xFE00) TwoByteOpCodes[value & 0xFF] = opCode;
        }
    }

    private static IEnumerable<Type> TypesUsedInBody(MethodBase method)
    {
        var il = method.GetMethodBody()?.GetILAsByteArray();
        if (il is null) yield break;
        var typeArguments = method.DeclaringType is { IsGenericType: true } declaring ? declaring.GetGenericArguments() : null;
        var methodArguments = method.IsGenericMethod ? method.GetGenericArguments() : null;

        for (var i = 0; i < il.Length;)
        {
            var opCode = il[i] == 0xFE ? TwoByteOpCodes[il[i + 1]] : OneByteOpCodes[il[i]];
            i += opCode.Size;
            switch (opCode.OperandType)
            {
                case OperandType.InlineMethod or OperandType.InlineField or OperandType.InlineType or OperandType.InlineTok:
                    var member = Resolve(method.Module, BitConverter.ToInt32(il, i), typeArguments, methodArguments);
                    if (member is Type usedType) yield return usedType;
                    else if (member?.DeclaringType is { } owner) yield return owner;
                    if (member is FieldInfo field) yield return field.FieldType;
                    if (member is MethodInfo called) yield return called.ReturnType;
                    i += 4;
                    break;
                case OperandType.InlineNone:
                    break;
                case OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar:
                    i += 1;
                    break;
                case OperandType.InlineVar:
                    i += 2;
                    break;
                case OperandType.InlineI8 or OperandType.InlineR:
                    i += 8;
                    break;
                case OperandType.InlineSwitch:
                    i += 4 + 4 * BitConverter.ToInt32(il, i);
                    break;
                default: // InlineBrTarget, InlineI, InlineSig, InlineString, ShortInlineR
                    i += 4;
                    break;
            }
        }
    }

    private static MemberInfo? Resolve(Module module, int token, Type[]? typeArguments, Type[]? methodArguments)
    {
        try { return module.ResolveMember(token, typeArguments, methodArguments); }
        catch (Exception ex) when (ex is ArgumentException or BadImageFormatException or TypeLoadException) { return null; }
    }
}
