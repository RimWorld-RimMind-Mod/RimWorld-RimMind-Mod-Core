using System.Collections.Generic;

namespace RimMind.Presentation.UI.Framework
{
    public sealed class RimMindUiTextFitWarning
    {
        public RimMindUiTextFitWarning(string elementName, string text, float textWidth, float rectWidth)
        {
            ElementName = elementName;
            Text = text;
            TextWidth = textWidth;
            RectWidth = rectWidth;
        }

        public string ElementName { get; }
        public string Text { get; }
        public float TextWidth { get; }
        public float RectWidth { get; }
    }

    public static class RimMindUiTextFitAnalyzer
    {
        public static IReadOnlyList<RimMindUiTextFitWarning> Analyze(RimMindUiDocument document)
        {
            var warnings = new List<RimMindUiTextFitWarning>();
            foreach (RimMindUiElement element in document.Elements)
            {
                if (string.IsNullOrWhiteSpace(element.Text))
                    continue;

                float available = element.Rect.width - RimMindUiMetrics.Padding * 2f;
                float textWidth = RimMindUiTextMeasure.ApproximateWidth(element.Text);
                if (textWidth > available)
                    warnings.Add(new RimMindUiTextFitWarning(element.Name, element.Text, textWidth, available));
            }

            return warnings;
        }
    }
}
