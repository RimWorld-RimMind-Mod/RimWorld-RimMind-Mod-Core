using UnityEngine;

namespace RimMind.Presentation.UI.Framework
{
    public enum RimMindUiElementKind
    {
        Panel,
        Label,
        Button,
        Tab,
        Input,
        ListRow,
        TableHeader,
        TableRow,
        StatusStrip
    }

    public sealed class RimMindUiElement
    {
        private RimMindUiElement(RimMindUiElementKind kind, string name, Rect rect, string text, bool selected)
        {
            Kind = kind;
            Name = name;
            Rect = rect;
            Text = text;
            Selected = selected;
        }

        public RimMindUiElementKind Kind { get; }
        public string Name { get; }
        public Rect Rect { get; }
        public string Text { get; }
        public bool Selected { get; }

        public static RimMindUiElement Panel(string name, Rect rect)
            => new(RimMindUiElementKind.Panel, name, rect, string.Empty, selected: false);

        public static RimMindUiElement Label(string name, Rect rect, string text)
            => new(RimMindUiElementKind.Label, name, rect, text, selected: false);

        public static RimMindUiElement Button(string name, Rect rect, string text)
            => new(RimMindUiElementKind.Button, name, rect, text, selected: false);

        public static RimMindUiElement Tab(string name, Rect rect, string text, bool selected)
            => new(RimMindUiElementKind.Tab, name, rect, text, selected);

        public static RimMindUiElement Input(string name, Rect rect, string text)
            => new(RimMindUiElementKind.Input, name, rect, text, selected: false);

        public static RimMindUiElement ListRow(string name, Rect rect, string text, bool selected)
            => new(RimMindUiElementKind.ListRow, name, rect, text, selected);

        public static RimMindUiElement TableHeader(string name, Rect rect, string text)
            => new(RimMindUiElementKind.TableHeader, name, rect, text, selected: false);

        public static RimMindUiElement TableRow(string name, Rect rect, string text)
            => new(RimMindUiElementKind.TableRow, name, rect, text, selected: false);

        public static RimMindUiElement TableRow(string name, Rect rect, string text, bool selected)
            => new(RimMindUiElementKind.TableRow, name, rect, text, selected);

        public static RimMindUiElement StatusStrip(string name, Rect rect, string text)
            => new(RimMindUiElementKind.StatusStrip, name, rect, text, selected: false);
    }
}
