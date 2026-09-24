using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace RimMind.Domain.Llm
{
    public sealed class GameStateInfo
    {
        private readonly List<GameStateSection> _sections = new();

        public GameStateInfo AddSection(string tag, string content)
        {
            if (!string.IsNullOrEmpty(content))
                _sections.Add(new GameStateSection(tag, content));
            return this;
        }

        public bool ContainsSection(string tag) => _sections.Any(s => s.Tag == tag);

        public string ToXml()
        {
            if (_sections.Count == 0) return "";
            var sb = new StringBuilder();
            foreach (var section in _sections)
            {
                sb.AppendLine($"<{section.Tag}>");
                sb.AppendLine(section.Content);
                sb.AppendLine($"</{section.Tag}>");
            }
            return sb.ToString();
        }

        public override string ToString() => ToXml();

        public static implicit operator string?(GameStateInfo? gsi) => gsi?.ToXml();
    }
}
