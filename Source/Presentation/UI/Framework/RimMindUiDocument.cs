using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RimMind.Presentation.UI.Framework
{
    public sealed class RimMindUiDocument
    {
        public RimMindUiDocument(string id, Rect viewport, IEnumerable<RimMindUiElement> elements)
        {
            Id = id;
            Viewport = viewport;
            Elements = elements.ToArray();
        }

        public string Id { get; }
        public Rect Viewport { get; }
        public IReadOnlyList<RimMindUiElement> Elements { get; }
    }
}
