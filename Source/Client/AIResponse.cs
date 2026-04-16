namespace RimMind.Core.Client
{
    public class AIResponse
    {
        public bool Success { get; set; }
        public string Content { get; set; } = string.Empty;
        public string Error { get; set; } = string.Empty;
        public int TokensUsed { get; set; }

        /// <summary>与对应 AIRequest.RequestId 相同，用于日志追踪。</summary>
        public string RequestId { get; set; } = string.Empty;

        public static AIResponse Failure(string requestId, string error) => new AIResponse
        {
            Success = false,
            Error = error,
            RequestId = requestId
        };

        public static AIResponse Ok(string requestId, string content, int tokens) => new AIResponse
        {
            Success = true,
            Content = content,
            TokensUsed = tokens,
            RequestId = requestId
        };
    }
}
