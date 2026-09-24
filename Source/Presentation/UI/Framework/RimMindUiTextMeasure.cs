namespace RimMind.Presentation.UI.Framework
{
    public static class RimMindUiTextMeasure
    {
        public static float ApproximateWidth(string text)
        {
            if (string.IsNullOrEmpty(text))
                return 0f;

            float width = 0f;
            foreach (char ch in text)
                width += ch <= 127 ? 7f : 14f;
            return width;
        }
    }
}
