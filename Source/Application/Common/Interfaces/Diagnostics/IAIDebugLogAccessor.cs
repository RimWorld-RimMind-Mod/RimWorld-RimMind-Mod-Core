using RimMind.Application.Common.Interfaces.Internal;

namespace RimMind.Application.Common.Interfaces.Diagnostics
{
    public interface IAIDebugLogAccessor
    {
        IAIDebugLog? Current { get; }
    }
}
