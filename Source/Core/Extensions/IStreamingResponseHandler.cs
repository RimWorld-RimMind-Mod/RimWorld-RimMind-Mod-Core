/// <summary>
/// 预留接口：流式响应处理器。当前未实现，保留用于未来流式响应功能。
/// </summary>
namespace RimMind.Core.Extensions
{
    public interface IStreamingResponseHandler
    {
        void OnChunkReceived(string chunk);
        void OnComplete(string fullResponse);
        void OnError(string error);
        string HandlerId { get; }
    }
}
