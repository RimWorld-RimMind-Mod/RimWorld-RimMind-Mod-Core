using System.Collections.Generic;

namespace RimMind.Domain.ValueObjects
{
    public class ContextEntry
    {
        public string? SourceKey;
        public string Content = null!;
        public float[]? Embedding;
        public string? Tag;
        public Dictionary<string, string>? Metadata { get; set; }

        public ContextEntry() { }

        public ContextEntry(string content, string? tag = null, float[]? embedding = null, Dictionary<string, string>? metadata = null)
        {
            Content = content;
            Tag = tag;
            Embedding = embedding;
            Metadata = metadata;
        }
    }
}
