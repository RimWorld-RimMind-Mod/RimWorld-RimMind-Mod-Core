using System;

namespace RimMind.Core.Prompt
{
    public class PromptSection
    {
        public string Tag { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public int Priority { get; set; }
        public int EstimatedTokens { get; set; }
        public Func<string, string>? Compress { get; set; }
        public string? LayerTag { get; set; }

        public const int PriorityCore = 0;
        public const int PriorityCurrentInput = 1;
        public const int PriorityKeyState = 3;
        public const int PriorityMemory = 5;
        public const int PriorityAuxiliary = 8;
        public const int PriorityCustom = 10;

        public PromptSection() { }

        public PromptSection(string tag, string content, int priority = PriorityAuxiliary)
        {
            Tag = tag ?? string.Empty;
            Content = content ?? string.Empty;
            Priority = priority;
            EstimatedTokens = EstimateTokens(Content);
        }

        public static int EstimateTokens(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            int charCount = text.Length;
            int cjk = 0;
            foreach (char c in text)
            {
                if (c >= 0x4E00 && c <= 0x9FFF || c >= 0x3040 && c <= 0x30FF || c >= 0xAC00 && c <= 0xD7AF)
                    cjk++;
            }
            return (int)Math.Ceiling((charCount - cjk) / 4.0 + cjk / 1.5);
        }

        public bool IsTrimable => Priority > PriorityCore;

        public bool IsCompressible => Compress != null && IsTrimable;

        public PromptSection Clone()
        {
            return new PromptSection
            {
                Tag = Tag,
                Content = Content,
                Priority = Priority,
                EstimatedTokens = EstimatedTokens,
                Compress = Compress,
                LayerTag = LayerTag,
            };
        }

        public override string ToString() => $"[{Tag}] P{Priority} ~{EstimatedTokens}tok";
    }
}
