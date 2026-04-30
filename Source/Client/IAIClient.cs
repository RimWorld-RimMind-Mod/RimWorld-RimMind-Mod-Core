using System.Collections.Generic;
using System.Threading.Tasks;

namespace RimMind.Core.Client
{
    public interface IAIClient
    {
        Task<AIResponse> SendAsync(AIRequest request);

        Task<AIResponse> SendStructuredAsync(AIRequest request, string? jsonSchema, List<StructuredTool>? tools);

        bool IsConfigured();

        bool IsLocalEndpoint { get; }
    }
}
