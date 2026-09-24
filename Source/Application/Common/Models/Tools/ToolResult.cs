namespace RimMind.Application.Common.Models.Tools
{
    public sealed record ToolResult
    {
        public string Content { get; init; } = "";
        public bool IsError { get; init; }
        public string? ToolCallId { get; init; }
        public string? ToolName { get; init; }

        public static ToolResult Ok(string content, string? toolCallId = null, string? toolName = null) =>
            new ToolResult { Content = content, IsError = false, ToolCallId = toolCallId, ToolName = toolName };

        public static ToolResult Fail(string error, string? toolCallId = null, string? toolName = null) =>
            new ToolResult { Content = error, IsError = true, ToolCallId = toolCallId, ToolName = toolName };
    }
}
