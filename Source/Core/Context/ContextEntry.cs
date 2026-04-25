namespace RimMind.Core.Context
{
    public class ContextEntry
    {
        public string Content = null!;
        public float[]? Embedding;
        public string? Tag;

        public ContextEntry() { }

        public ContextEntry(string content, string? tag = null, float[]? embedding = null)
        {
            Content = content;
            Tag = tag;
            Embedding = embedding;
        }
    }
}
