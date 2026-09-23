namespace RimMind.Domain.Llm
{
    public class StructuredTool
    {
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string? Parameters { get; set; }
        public string? ToolChoice { get; set; }
    }

    public class StructuredToolCall
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Arguments { get; set; } = "";

        public StructuredToolCallFunction? Function
        {
            get => string.IsNullOrEmpty(Name) && string.IsNullOrEmpty(Arguments) ? null : new StructuredToolCallFunction { Name = Name, Arguments = Arguments };
            set
            {
                if (value != null)
                {
                    if (!string.IsNullOrEmpty(value.Name)) Name = value.Name;
                    if (!string.IsNullOrEmpty(value.Arguments)) Arguments = value.Arguments;
                }
            }
        }
    }

    public class StructuredToolCallFunction
    {
        public string Name { get; set; } = "";
        public string Arguments { get; set; } = "";
    }
}
