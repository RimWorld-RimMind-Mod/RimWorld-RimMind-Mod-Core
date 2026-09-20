using System;

namespace RimMind.Application.Common.Models.Prompt
{
    public class PromptSection
    {
        public const int PriorityCore = 0;
        public const int PriorityKeyState = 10;
        public const int PriorityCurrentInput = 20;
        public const int PriorityAuxiliary = 30;

        public string Name { get; set; } = "";
        public string Content { get; set; } = "";
        public int Priority { get; set; }
        public int EstimatedTokens { get; set; }
        private bool? _isCompressible;
        public bool IsCompressible
        {
            get => _isCompressible ?? (Compress != null);
            set => _isCompressible = value;
        }
        public Func<string, string>? Compress { get; set; }
        public string? LayerTag { get; set; }

        public PromptSection() { }

        public PromptSection(string name, string content, int priority = PriorityAuxiliary)
        {
            Name = name;
            Content = content;
            Priority = priority;
            EstimatedTokens = EstimateTokens(content);
        }

        public PromptSection Clone()
        {
            return new PromptSection
            {
                Name = Name,
                Content = Content,
                Priority = Priority,
                EstimatedTokens = EstimatedTokens,
                _isCompressible = _isCompressible,
                Compress = Compress,
                LayerTag = LayerTag
            };
        }

        public static int EstimateTokens(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            return (int)(text.Length / 3.5);
        }
    }
}
