using System.Threading.Tasks;

namespace RimMind.Core.Client
{
    public interface IAIClient
    {
        /// <summary>发送请求，返回完整响应。线程安全，可在后台线程调用。</summary>
        Task<AIResponse> SendAsync(AIRequest request);

        /// <summary>API Key 等必要配置是否已填写。</summary>
        bool IsConfigured();
    }
}
