using System.Threading.Tasks;

namespace RimMind.Core.Client
{
    public interface IAIClient
    {
        Task<AIResponse> SendAsync(AIRequest request);

        bool IsConfigured();

        bool IsLocalEndpoint { get; }
    }
}
