using System;
using Verse;

namespace RimMind.Core.Extensions
{
    /// <summary>
    /// Definition of an Agent Tool exposed by a Sensor.
    /// These are converted to StructuredTool for AI tool calling.
    /// </summary>
    public class AgentToolDefinition
    {
        public string Name = null!;
        public string Description = null!;
        public string Parameters = "{}";
        /// <summary>Execute function returns a string result for the AI.</summary>
        public Func<Pawn, string> Execute = null!;
    }
}
