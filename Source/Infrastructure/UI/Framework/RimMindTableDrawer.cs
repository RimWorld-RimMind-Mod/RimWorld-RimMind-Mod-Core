using System.Collections.Generic;
using RimMind.Infrastructure.UI.DebugTables;
using RimMind.Presentation.UI.Framework;
using RimMind.Presentation.UI.Layout;
using UnityEngine;
using Verse;

namespace RimMind.Infrastructure.UI.Framework
{
    public sealed class RimMindTableDrawer
    {
        private const int DebugTableColumnCount = 8;
        private const int CompactListColumnCount = 2;
        private const float StatusStripWidth = 4f;
        private const float CellPadding = 6f;

        private static readonly string[] DebugTableHeaderKeys =
        {
            "RimMind.UI.DebugTable.Header.Id",
            "RimMind.UI.DebugTable.Header.Status",
            "RimMind.UI.DebugTable.Header.Time",
            "RimMind.UI.DebugTable.Header.Scope",
            "RimMind.UI.DebugTable.Header.Actor",
            "RimMind.UI.DebugTable.Header.Channel",
            "RimMind.UI.DebugTable.Header.Model",
            "RimMind.UI.DebugTable.Header.Summary"
        };

        private static readonly string[] CompactListHeaderKeys =
        {
            "RimMind.UI.DebugTable.Header.Request",
            "RimMind.UI.DebugTable.Header.Summary"
        };

        public void Draw(Rect rect, DebugTableModel model, ref Vector2 scroll, RimMindLayoutScope scope)
        {
            TablePageLayoutResult layout = TablePageLayout.Calculate(rect, model.Rows.Count, columnCount: DebugTableColumnCount);
            scope.Record(layout.Toolbar, "Table:Toolbar");
            scope.Record(layout.Header, "Table:Header");
            scope.Record(layout.Body, "Table:Body");
            scope.Record(layout.BottomBar, "Table:BottomBar");

            DrawToolbar(layout.Toolbar, model.Title);
            if (model.Rows.Count == 0)
            {
                DrawEmptyTableBody(layout.Body);
                DrawDebugHeaders(layout, scroll.x);
                return;
            }

            DrawDebugRows(layout, model.Rows, ref scroll);
            DrawDebugHeaders(layout, scroll.x);
        }

        public string? DrawSelectable(Rect rect, DebugTableModel model, string? selectedId, ref Vector2 scroll, RimMindLayoutScope scope)
        {
            TablePageLayoutResult layout = TablePageLayout.Calculate(rect, model.Rows.Count, columnCount: DebugTableColumnCount);
            scope.Record(layout.Toolbar, "Table:Toolbar");
            scope.Record(layout.Header, "Table:Header");
            scope.Record(layout.Body, "Table:Body");
            scope.Record(layout.BottomBar, "Table:BottomBar");

            DrawToolbar(layout.Toolbar, model.Title);
            if (model.Rows.Count == 0)
            {
                DrawEmptyTableBody(layout.Body);
                DrawDebugHeaders(layout, scroll.x);
                return null;
            }

            string? nextSelectedId = DrawSelectableDebugRows(layout, model.Rows, selectedId, ref scroll);
            DrawDebugHeaders(layout, scroll.x);
            return nextSelectedId;
        }

        public string? DrawSelectableCompact(
            Rect rect,
            DebugTableModel model,
            string? selectedId,
            ref Vector2 scroll,
            RimMindLayoutScope scope)
        {
            TablePageLayoutResult layout = TablePageLayout.Calculate(
                rect,
                model.Rows.Count,
                columnCount: CompactListColumnCount);
            scope.Record(layout.Toolbar, "Table:Toolbar");
            scope.Record(layout.Header, "Table:Header");
            scope.Record(layout.Body, "Table:Body");
            scope.Record(layout.BottomBar, "Table:BottomBar");

            DrawToolbar(layout.Toolbar, model.Title);
            if (model.Rows.Count == 0)
            {
                DrawEmptyTableBody(layout.Body);
                DrawHeaders(layout, CompactListHeaderKeys, scroll.x);
                return null;
            }

            string? nextSelectedId = DrawSelectableCompactRows(
                layout,
                model.Rows,
                selectedId,
                ref scroll);
            DrawHeaders(layout, CompactListHeaderKeys, scroll.x);
            return nextSelectedId;
        }

        public void Draw(
            TablePageLayoutResult layout,
            IReadOnlyList<string> headers,
            IReadOnlyList<IReadOnlyList<string>> rows,
            ref Vector2 scroll,
            RimMindLayoutScope scope)
        {
            scope.Record(layout.Toolbar, "Table:Toolbar");
            scope.Record(layout.Header, "Table:Header");
            scope.Record(layout.Body, "Table:Body");
            scope.Record(layout.BottomBar, "Table:BottomBar");

            Widgets.BeginScrollView(layout.Body, ref scroll, layout.ViewRect);
            TableVisibleRowRange range = TablePageLayout.CalculateVisibleRowRange(
                rows.Count, scroll.y, layout.Body.height, RimMindUiMetrics.DebugRowHeight);
            float colWidth = layout.ViewRect.width / Mathf.Max(1, headers.Count);
            for (int r = range.FirstIndex; r < range.LastExclusive; r++)
            {
                Rect rowRect = new Rect(0f, r * RimMindUiMetrics.DebugRowHeight, layout.ViewRect.width, RimMindUiMetrics.DebugRowHeight);
                if (r % 2 == 0)
                    Widgets.DrawBoxSolid(rowRect, RimMindUI.ColorSectionBg);
                for (int c = 0; c < rows[r].Count; c++)
                {
                    Widgets.Label(new Rect(c * colWidth, rowRect.y, colWidth, rowRect.height), rows[r][c]);
                }
            }

            Widgets.EndScrollView();

            GUI.BeginGroup(layout.Header);
            for (int c = 0; c < headers.Count; c++)
            {
                Rect headerRect = TablePageLayout.CalculateColumnRect(
                    layout.ViewRect.width, c, headers.Count, 0f, layout.Header.height, scroll.x, CellPadding);
                Widgets.Label(headerRect, headers[c]);
            }
            GUI.EndGroup();
        }

        private static void DrawToolbar(Rect rect, string title)
        {
            Color oldColor = GUI.color;
            GUI.color = RimMindUI.ColorHeader;
            Widgets.Label(rect, title);
            GUI.color = oldColor;
        }

        private static void DrawEmptyTableBody(Rect body)
            => RimMindUI.DrawEmptyState(body, "RimMind.UI.DebugTable.Empty".Translate());

        private static void DrawDebugHeaders(TablePageLayoutResult layout, float horizontalScroll)
        {
            Color oldColor = GUI.color;
            GUI.color = RimMindUI.ColorKey;
            GUI.BeginGroup(layout.Header);
            for (int c = 0; c < DebugTableHeaderKeys.Length; c++)
            {
                Rect headerRect = TablePageLayout.CalculateColumnRect(
                    layout.ViewRect.width,
                    c,
                    DebugTableColumnCount,
                    0f,
                    layout.Header.height,
                    horizontalScroll,
                    CellPadding);
                Widgets.Label(headerRect, DebugTableHeaderKeys[c].Translate());
            }
            GUI.EndGroup();

            GUI.color = oldColor;
        }

        private static void DrawHeaders(
            TablePageLayoutResult layout,
            IReadOnlyList<string> headerKeys,
            float horizontalScroll)
        {
            Color oldColor = GUI.color;
            GUI.color = RimMindUI.ColorKey;
            GUI.BeginGroup(layout.Header);
            for (int c = 0; c < headerKeys.Count; c++)
            {
                Rect headerRect = TablePageLayout.CalculateColumnRect(
                    layout.ViewRect.width,
                    c,
                    headerKeys.Count,
                    0f,
                    layout.Header.height,
                    horizontalScroll,
                    CellPadding);
                Widgets.Label(headerRect, headerKeys[c].Translate());
            }
            GUI.EndGroup();
            GUI.color = oldColor;
        }

        private static void DrawDebugRows(TablePageLayoutResult layout, IReadOnlyList<DebugTableRow> rows, ref Vector2 scroll)
        {
            float colWidth = layout.ViewRect.width / DebugTableColumnCount;
            Widgets.BeginScrollView(layout.Body, ref scroll, layout.ViewRect);
            TableVisibleRowRange range = TablePageLayout.CalculateVisibleRowRange(
                rows.Count, scroll.y, layout.Body.height, RimMindUiMetrics.DebugRowHeight);
            for (int r = range.FirstIndex; r < range.LastExclusive; r++)
            {
                DebugTableRow row = rows[r];
                Rect rowRect = new Rect(0f, r * RimMindUiMetrics.DebugRowHeight, layout.ViewRect.width, RimMindUiMetrics.DebugRowHeight);
                DrawDebugRow(rowRect, colWidth, row, selected: false, alternateBackground: r % 2 == 0);
            }

            Widgets.EndScrollView();
        }

        private static string? DrawSelectableDebugRows(
            TablePageLayoutResult layout,
            IReadOnlyList<DebugTableRow> rows,
            string? selectedId,
            ref Vector2 scroll)
        {
            string? selectedRowId = ResolveSelectedRowId(rows, selectedId);
            float colWidth = layout.ViewRect.width / DebugTableColumnCount;
            Widgets.BeginScrollView(layout.Body, ref scroll, layout.ViewRect);
            TableVisibleRowRange range = TablePageLayout.CalculateVisibleRowRange(
                rows.Count, scroll.y, layout.Body.height, RimMindUiMetrics.DebugRowHeight);
            for (int r = range.FirstIndex; r < range.LastExclusive; r++)
            {
                DebugTableRow row = rows[r];
                Rect rowRect = new Rect(0f, r * RimMindUiMetrics.DebugRowHeight, layout.ViewRect.width, RimMindUiMetrics.DebugRowHeight);
                DrawDebugRow(rowRect, colWidth, row, row.Id == selectedRowId, r % 2 == 0);

                if (Widgets.ButtonInvisible(rowRect))
                    selectedRowId = row.Id;
            }

            Widgets.EndScrollView();
            return selectedRowId;
        }

        private static string? DrawSelectableCompactRows(
            TablePageLayoutResult layout,
            IReadOnlyList<DebugTableRow> rows,
            string? selectedId,
            ref Vector2 scroll)
        {
            string? selectedRowId = ResolveSelectedRowId(rows, selectedId);
            Widgets.BeginScrollView(layout.Body, ref scroll, layout.ViewRect);
            TableVisibleRowRange range = TablePageLayout.CalculateVisibleRowRange(
                rows.Count, scroll.y, layout.Body.height, RimMindUiMetrics.DebugRowHeight);
            for (int r = range.FirstIndex; r < range.LastExclusive; r++)
            {
                DebugTableRow row = rows[r];
                Rect rowRect = new Rect(
                    0f,
                    r * RimMindUiMetrics.DebugRowHeight,
                    layout.ViewRect.width,
                    RimMindUiMetrics.DebugRowHeight);
                if (r % 2 == 0)
                    Widgets.DrawBoxSolid(rowRect, RimMindUI.ColorSectionBg);
                if (row.Id == selectedRowId)
                    Widgets.DrawHighlight(rowRect);

                Widgets.DrawBoxSolid(
                    new Rect(rowRect.x, rowRect.y, StatusStripWidth, rowRect.height),
                    ColorFor(row.StatusColorName));

                Rect requestCell = TablePageLayout.CalculateColumnRect(
                    layout.ViewRect.width, 0, CompactListColumnCount, rowRect.y, rowRect.height, 0f, CellPadding);
                requestCell.x += StatusStripWidth;
                requestCell.width = Mathf.Max(0f, requestCell.width - StatusStripWidth);
                Rect summaryCell = TablePageLayout.CalculateColumnRect(
                    layout.ViewRect.width, 1, CompactListColumnCount, rowRect.y, rowRect.height, 0f, CellPadding);
                DrawCell(requestCell, row.Id);
                DrawCell(summaryCell, row.Summary);

                if (Widgets.ButtonInvisible(rowRect))
                    selectedRowId = row.Id;
            }

            Widgets.EndScrollView();
            return selectedRowId;
        }

        private static string? ResolveSelectedRowId(IReadOnlyList<DebugTableRow> rows, string? selectedId)
        {
            if (rows.Count == 0)
                return null;

            if (!string.IsNullOrEmpty(selectedId))
            {
                for (int i = 0; i < rows.Count; i++)
                {
                    if (rows[i].Id == selectedId)
                        return selectedId;
                }
            }

            return rows[0].Id;
        }

        private static void DrawDebugRow(
            Rect rowRect,
            float colWidth,
            DebugTableRow row,
            bool selected,
            bool alternateBackground)
        {
            if (alternateBackground)
                Widgets.DrawBoxSolid(rowRect, RimMindUI.ColorSectionBg);

            if (selected)
                Widgets.DrawHighlight(rowRect);

            Widgets.DrawBoxSolid(new Rect(rowRect.x, rowRect.y, StatusStripWidth, rowRect.height), ColorFor(row.StatusColorName));
            DrawDebugCells(rowRect, colWidth, row);
        }

        private static void DrawDebugCells(Rect rowRect, float colWidth, DebugTableRow row)
        {
            string[] cells =
            {
                row.Id,
                StatusLabelFor(row.Status),
                row.Time,
                row.Scope,
                row.Actor,
                row.Channel,
                row.Model,
                string.IsNullOrWhiteSpace(row.Duration) ? row.Summary : row.Summary + " / " + row.Duration
            };

            Color oldColor = GUI.color;
            GUI.color = RimMindUI.ColorValue;
            for (int c = 0; c < cells.Length; c++)
            {
                Rect cell = TablePageLayout.CalculateColumnRect(
                    colWidth * DebugTableColumnCount,
                    c,
                    DebugTableColumnCount,
                    rowRect.y,
                    rowRect.height,
                    0f,
                    CellPadding);
                if (c == 0)
                {
                    cell.x += StatusStripWidth;
                    cell.width = Mathf.Max(0f, cell.width - StatusStripWidth);
                }
                DrawCell(cell, cells[c]);
            }

            GUI.color = oldColor;
        }

        private static void DrawCell(Rect rect, string? value)
        {
            // Row height is fixed; let Verse measure the current font/scale instead
            // of truncating by character count. Keep the full value discoverable.
            string text = DebugTableText.Preview(value, int.MaxValue);
            Widgets.LabelEllipses(rect, text);
            if (text.Length > 0)
                TooltipHandler.TipRegion(rect, text);
        }

        private static string StatusLabelFor(DebugTableStatus status)
        {
            string key = status switch
            {
                DebugTableStatus.Waiting => "RimMind.UI.DebugTable.Status.Waiting",
                DebugTableStatus.Streaming => "RimMind.UI.DebugTable.Status.Streaming",
                DebugTableStatus.Completed => "RimMind.UI.DebugTable.Status.Completed",
                DebugTableStatus.Failed => "RimMind.UI.DebugTable.Status.Failed",
                DebugTableStatus.Cancelled => "RimMind.UI.DebugTable.Status.Cancelled",
                _ => "RimMind.UI.DebugTable.Status.Cancelled"
            };

            return key.Translate();
        }

        private static Color ColorFor(string colorName)
        {
            return (colorName ?? string.Empty).ToLowerInvariant() switch
            {
                "orange" => new Color(1f, 0.65f, 0.2f),
                "blue" => new Color(0.35f, 0.65f, 1f),
                "green" => RimMindUI.ColorActive,
                "red" => RimMindUI.ColorError,
                "gray" => RimMindUI.ColorMuted,
                _ => RimMindUI.ColorMuted
            };
        }
    }
}
