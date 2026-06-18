// net472 has no System.Runtime.CompilerServices.IsExternalInit, which the C# compiler
// requires to emit `init` accessors (every record in this assembly uses them). Supplying
// it lets records compile on .NET Framework. No-op on net8.0, where the BCL provides it.
#if NETFRAMEWORK
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit { }
}
#endif
