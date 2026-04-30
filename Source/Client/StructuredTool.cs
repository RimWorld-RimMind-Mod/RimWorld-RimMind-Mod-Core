using System.Collections.Generic;

namespace RimMind.Core.Client
{
    public class StructuredTool
    {
        public string Name = "";
        public string Description = "";
        public string? Parameters;
        public string? ToolChoice;
    }

    public class StructuredToolCall
    {
        public string Id = "";
        public string Name = "";
        public string Arguments = "";
    }
}
