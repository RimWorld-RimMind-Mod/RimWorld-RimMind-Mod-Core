namespace RimMind.Presentation.UI.Framework
{
    public sealed class TabbedPageTabModel
    {
        public TabbedPageTabModel(string id, string label, string labelKey, bool selected, bool enabled, string? tooltipKey)
        {
            Id = id;
            Label = label;
            LabelKey = labelKey;
            Selected = selected;
            Enabled = enabled;
            TooltipKey = tooltipKey;
        }

        public string Id { get; }
        public string Label { get; }
        public string LabelKey { get; }
        public bool Selected { get; }
        public bool Enabled { get; }
        public string? TooltipKey { get; }
    }
}
