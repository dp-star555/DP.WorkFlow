using System.Text;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace ScriptEngine.Compilation;

/// <summary>为动态脚本补齐现代 C# 编译器在旧 .NET Framework 中需要的标记类型。</summary>
internal static class NetFrameworkCompatibilitySource
{
#if NETFRAMEWORK
    private static readonly CompatibilityType[] Types =
    {
        new("System.Runtime.CompilerServices.IsExternalInit", "namespace System.Runtime.CompilerServices { internal static class IsExternalInit { } }"),
        new("System.Runtime.CompilerServices.RequiredMemberAttribute", "namespace System.Runtime.CompilerServices { [System.AttributeUsage(System.AttributeTargets.All, Inherited = false)] internal sealed class RequiredMemberAttribute : System.Attribute { } }"),
        new("System.Runtime.CompilerServices.CompilerFeatureRequiredAttribute", "namespace System.Runtime.CompilerServices { [System.AttributeUsage(System.AttributeTargets.All, AllowMultiple = true, Inherited = false)] internal sealed class CompilerFeatureRequiredAttribute : System.Attribute { public CompilerFeatureRequiredAttribute(string featureName) { FeatureName = featureName; } public string FeatureName { get; } public bool IsOptional { get; init; } } }"),
        new("System.Diagnostics.CodeAnalysis.SetsRequiredMembersAttribute", "namespace System.Diagnostics.CodeAnalysis { [System.AttributeUsage(System.AttributeTargets.Constructor, Inherited = false)] internal sealed class SetsRequiredMembersAttribute : System.Attribute { } }"),
        new("System.Runtime.CompilerServices.RefSafetyRulesAttribute", "namespace System.Runtime.CompilerServices { [System.AttributeUsage(System.AttributeTargets.Module, Inherited = false)] internal sealed class RefSafetyRulesAttribute : System.Attribute { public RefSafetyRulesAttribute(int version) { Version = version; } public int Version { get; } } }"),
        new("System.Runtime.CompilerServices.ScopedRefAttribute", "namespace System.Runtime.CompilerServices { [System.AttributeUsage(System.AttributeTargets.All, Inherited = false)] internal sealed class ScopedRefAttribute : System.Attribute { } }"),
        new("System.Runtime.CompilerServices.InlineArrayAttribute", "namespace System.Runtime.CompilerServices { [System.AttributeUsage(System.AttributeTargets.Struct, Inherited = false)] internal sealed class InlineArrayAttribute : System.Attribute { public InlineArrayAttribute(int length) { Length = length; } public int Length { get; } } }"),
        new("System.Runtime.CompilerServices.CollectionBuilderAttribute", "namespace System.Runtime.CompilerServices { [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Struct | System.AttributeTargets.Interface, Inherited = false)] internal sealed class CollectionBuilderAttribute : System.Attribute { public CollectionBuilderAttribute(System.Type builderType, string methodName) { BuilderType = builderType; MethodName = methodName; } public System.Type BuilderType { get; } public string MethodName { get; } } }"),
    };
#endif

    public static CSharpCompilation AddMissingTypes(CSharpCompilation compilation, CSharpParseOptions parseOptions)
    {
#if NETFRAMEWORK
        var missingSources = Types
            .Where(item => compilation.GetTypeByMetadataName(item.MetadataName) is null)
            .Select(item => item.Source)
            .ToArray();
        if (missingSources.Length == 0) return compilation;
        var source = string.Join(Environment.NewLine, missingSources);
        var tree = CSharpSyntaxTree.ParseText(
            SourceText.From(source, Encoding.UTF8),
            parseOptions,
            "ScriptEngine.Compatibility.g.cs");
        return compilation.AddSyntaxTrees(tree);
#else
        return compilation;
#endif
    }

#if NETFRAMEWORK
    private sealed record CompatibilityType(string MetadataName, string Source);
#endif
}
