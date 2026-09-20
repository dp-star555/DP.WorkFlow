#if NET48
namespace System.Runtime.CompilerServices
{
    internal sealed class IsExternalInit;
}

namespace System.Diagnostics.CodeAnalysis
{
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Parameter | AttributeTargets.Property, Inherited = false)]
    internal sealed class AllowNullAttribute : Attribute;
}
#endif
