using System.Threading;
using RimMind.Application.Common.Interfaces.Memory;

namespace RimMind.Application.Features.Memory
{
    /// <summary>Process-wide holder for the optional Memory mod implementation.</summary>
    public static class MemoryBridgeRegistry
    {
        private static IMemoryBridge _current = new NullMemoryBridge();

        public static IMemoryBridge Current => Volatile.Read(ref _current);

        public static void Register(IMemoryBridge bridge)
        {
            Interlocked.Exchange(ref _current, bridge ?? new NullMemoryBridge());
        }
    }
}
