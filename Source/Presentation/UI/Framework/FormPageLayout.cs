using System.Collections.Generic;
using UnityEngine;

namespace RimMind.Presentation.UI.Framework
{
    public readonly struct FormSectionRect
    {
        public FormSectionRect(Rect header, IReadOnlyList<Rect> rows)
        {
            Header = header;
            Rows = rows;
        }

        public Rect Header { get; }
        public IReadOnlyList<Rect> Rows { get; }
    }

    public sealed class FormPageLayoutResult
    {
        public FormPageLayoutResult(Rect viewport, float contentHeight, IReadOnlyList<FormSectionRect> sections)
        {
            Viewport = viewport;
            ContentHeight = contentHeight;
            Sections = sections;
        }

        public Rect Viewport { get; }
        public float ContentHeight { get; }
        public IReadOnlyList<FormSectionRect> Sections { get; }
    }

    public static class FormPageLayout
    {
        public static FormPageLayoutResult Calculate(Rect viewport, int sectionCount, int rowsPerSection)
        {
            var sections = new List<FormSectionRect>(sectionCount);
            float y = 0f;
            float width = Mathf.Max(0f, viewport.width - RimMindUiMetrics.ScrollBarWidth);
            for (int s = 0; s < sectionCount; s++)
            {
                Rect header = new Rect(0f, y, width, RimMindUiMetrics.RowHeight);
                y += RimMindUiMetrics.RowHeight + RimMindUiMetrics.Padding;
                var rows = new List<Rect>(rowsPerSection);
                for (int r = 0; r < rowsPerSection; r++)
                {
                    Rect row = new Rect(0f, y, width, RimMindUiMetrics.RowHeight);
                    rows.Add(row);
                    y += RimMindUiMetrics.RowHeight + RimMindUiMetrics.Padding;
                }
                y += RimMindUiMetrics.SectionGap;
                sections.Add(new FormSectionRect(header, rows));
            }
            return new FormPageLayoutResult(viewport, y, sections);
        }
    }
}
