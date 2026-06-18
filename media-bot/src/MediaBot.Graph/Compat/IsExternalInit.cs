// See MediaBot.Core/Compat/IsExternalInit.cs. Each assembly needs its own internal copy
// for records to compile on net472. This project is net472-only, so it is always present.
#if NETFRAMEWORK
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit { }
}
#endif
