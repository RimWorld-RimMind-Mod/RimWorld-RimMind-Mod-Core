using RimMind.Application.Common.Interfaces.Client;

namespace RimMind.Application.Common.Interfaces.Internal
{
    public interface IClientManager
    {
        IAIClient? GetClient();
        void InvalidateCache();
        IAIClient? GetPlayer2Client();
    }
}
