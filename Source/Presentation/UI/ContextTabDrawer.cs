using System;
using RimMind.Application.Common.Interfaces;
using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Domain.Enums;
using RimMind.Presentation.UI.Framework;
using RimMind.Presentation.UI.Layout;
using RimMind.Presentation.Settings;
using UnityEngine;
using Verse;

namespace RimMind.Presentation.UI
{
    internal static class ContextTabDrawer
    {
        private static ContextPreset _selectedPreset = ContextPreset.Standard;
        private static Vector2 _contextScroll;

        public static void Draw(Rect inRect, ISettingsProvider s, RimMindLayoutScope? scope = null)
        {
            var ctx = s.Context;

            FormPageLayoutResult formLayout = FormPageLayout.Calculate(inRect, sectionCount: 4, rowsPerSection: 12);
            Rect viewRect = new Rect(
                0f,
                0f,
                formLayout.Viewport.width - RimMindUiMetrics.ScrollBarWidth,
                Mathf.Max(980f, formLayout.ContentHeight));
            Widgets.BeginScrollView(inRect, ref _contextScroll, viewRect);
            scope?.Record(formLayout.Viewport, "Settings:Context:Viewport");
            scope?.Record(viewRect, "Settings:Context:Content");

            var listing = new Listing_Standard();
            listing.Begin(viewRect);


            DrawPresetCards(listing, ctx);
            listing.Gap(12f);

            DrawPawnAndEnvironmentColumns(listing, ctx);

            DrawBudgetSection(listing, s, ctx);

            listing.Gap(12f);
            float btnW = 160f;
            float btnH = 30f;
            Rect btnRow = listing.GetRect(btnH);
            Rect centerBtn = new Rect(btnRow.x + (btnRow.width - btnW) / 2f, btnRow.y, btnW, btnH);
            if (Widgets.ButtonText(centerBtn, "RimMind.Context.ResetDefault".Translate()))
            {
                s.Context.ResetToDefault();
                _selectedPreset = ContextPreset.Standard;
            }
            TooltipHandler.TipRegion(centerBtn, "RimMind.Context.ResetDefault.Desc".Translate());

            listing.End();
            Widgets.EndScrollView();
        }

        private static void DrawPawnAndEnvironmentColumns(Listing_Standard listing, IContextSettings ctx)
        {
            float colW = (listing.ColumnWidth - 20f) / 2f;
            Rect anchor = listing.GetRect(0f);

            float leftH = DrawPawnColumn(anchor, colW, ctx);
            float rightH = DrawEnvironmentColumn(anchor, colW, ctx);

            listing.Gap(Mathf.Max(leftH, rightH) + 8f);
        }

        private static float DrawPawnColumn(Rect anchor, float colW, IContextSettings ctx)
        {
            var left = new Listing_Standard();
            left.Begin(new Rect(anchor.x, anchor.y, colW, 9999f));
            SettingsUIDrawer.DrawSectionHeader(left, "RimMind.Context.PawnInfo".Translate());

            DrawCheckbox(left, ctx, c => c.IncludeRace, (c, v) => c.IncludeRace = v, "RimMind.Context.IncludeRace", "RimMind.Context.IncludeRace.Desc");
            DrawCheckbox(left, ctx, c => c.IncludeAge, (c, v) => c.IncludeAge = v, "RimMind.Context.IncludeAge", "RimMind.Context.IncludeAge.Desc");
            DrawCheckbox(left, ctx, c => c.IncludeGender, (c, v) => c.IncludeGender = v, "RimMind.Context.IncludeGender", "RimMind.Context.IncludeGender.Desc");
            DrawCheckbox(left, ctx, c => c.IncludeBackstory, (c, v) => c.IncludeBackstory = v, "RimMind.Context.IncludeBackstory", "RimMind.Context.IncludeBackstory.Desc");
            DrawCheckbox(left, ctx, c => c.IncludeIdeology, (c, v) => c.IncludeIdeology = v, "RimMind.Context.IncludeIdeology", "RimMind.Context.IncludeIdeology.Desc");
            DrawCheckbox(left, ctx, c => c.IncludeTraits, (c, v) => c.IncludeTraits = v, "RimMind.Context.IncludeTraits", "RimMind.Context.IncludeTraits.Desc");

            var includeSkills = ctx.IncludeSkills;
            left.CheckboxLabeled("RimMind.Context.IncludeSkills".Translate(), ref includeSkills, "RimMind.Context.IncludeSkills.Desc".Translate());
            ctx.IncludeSkills = includeSkills;
            if (ctx.IncludeSkills)
            {
                string skillTip = "RimMind.Context.MinSkillLevel.Desc".Translate();
                left.LabelWithTooltip($"  {"RimMind.Context.MinSkillLevel".Translate()}: {ctx.MinSkillLevel}", skillTip);
                ctx.MinSkillLevel = (int)left.SliderWithTooltip(ctx.MinSkillLevel, 1f, 15f, skillTip);
            }

            DrawCheckbox(left, ctx, c => c.IncludeHealth, (c, v) => c.IncludeHealth = v, "RimMind.Context.IncludeHealth", "RimMind.Context.IncludeHealth.Desc");
            DrawCheckbox(left, ctx, c => c.IncludeCapacities, (c, v) => c.IncludeCapacities = v, "RimMind.Context.IncludeCapacities", "RimMind.Context.IncludeCapacities.Desc");
            DrawCheckbox(left, ctx, c => c.IncludeMood, (c, v) => c.IncludeMood = v, "RimMind.Context.IncludeMood", "RimMind.Context.IncludeMood.Desc");
            DrawCheckbox(left, ctx, c => c.IncludeMoodThoughts, (c, v) => c.IncludeMoodThoughts = v, "RimMind.Context.IncludeMoodThoughts", "RimMind.Context.IncludeMoodThoughts.Desc");
            DrawCheckbox(left, ctx, c => c.IncludeCurrentJob, (c, v) => c.IncludeCurrentJob = v, "RimMind.Context.IncludeCurrentJob", "RimMind.Context.IncludeCurrentJob.Desc");
            DrawCheckbox(left, ctx, c => c.IncludeWorkPriorities, (c, v) => c.IncludeWorkPriorities = v, "RimMind.Context.IncludeWorkPriorities", "RimMind.Context.IncludeWorkPriorities.Desc");
            DrawCheckbox(left, ctx, c => c.IncludeEquipment, (c, v) => c.IncludeEquipment = v, "RimMind.Context.IncludeEquipment", "RimMind.Context.IncludeEquipment.Desc");
            DrawCheckbox(left, ctx, c => c.IncludeInventory, (c, v) => c.IncludeInventory = v, "RimMind.Context.IncludeInventory", "RimMind.Context.IncludeInventory.Desc");
            DrawCheckbox(left, ctx, c => c.IncludeLocation, (c, v) => c.IncludeLocation = v, "RimMind.Context.IncludeLocation", "RimMind.Context.IncludeLocation.Desc");
            DrawCheckbox(left, ctx, c => c.IncludeRelations, (c, v) => c.IncludeRelations = v, "RimMind.Context.IncludeRelations", "RimMind.Context.IncludeRelations.Desc");
            DrawCheckbox(left, ctx, c => c.IncludeGenes, (c, v) => c.IncludeGenes = v, "RimMind.Context.IncludeGenes", "RimMind.Context.IncludeGenes.Desc");
            DrawCheckbox(left, ctx, c => c.IncludeCombatStatus, (c, v) => c.IncludeCombatStatus = v, "RimMind.Context.IncludeCombatStatus", "RimMind.Context.IncludeCombatStatus.Desc");
            DrawCheckbox(left, ctx, c => c.IncludeSurroundings, (c, v) => c.IncludeSurroundings = v, "RimMind.Context.IncludeSurroundings", "RimMind.Context.IncludeSurroundings.Desc");

            float leftH = left.CurHeight;
            left.End();
            return leftH;
        }

        private static void DrawCheckbox(Listing_Standard listing, IContextSettings ctx,
            Func<IContextSettings, bool> getter, Action<IContextSettings, bool> setter,
            string labelKey, string descKey)
        {
            var v = getter(ctx);
            listing.CheckboxLabeled(labelKey.Translate(), ref v, descKey.Translate());
            setter(ctx, v);
        }

        private static float DrawEnvironmentColumn(Rect anchor, float colW, IContextSettings ctx)
        {
            var right = new Listing_Standard();
            right.Begin(new Rect(anchor.x + colW + 20f, anchor.y, colW, 9999f));
            SettingsUIDrawer.DrawSectionHeader(right, "RimMind.Context.Environment".Translate());

            DrawCheckbox(right, ctx, c => c.IncludeGameTime, (c, v) => c.IncludeGameTime = v, "RimMind.Context.IncludeGameTime", "RimMind.Context.IncludeGameTime.Desc");
            DrawCheckbox(right, ctx, c => c.IncludeColonistCount, (c, v) => c.IncludeColonistCount = v, "RimMind.Context.IncludeColonistCount", "RimMind.Context.IncludeColonistCount.Desc");
            DrawCheckbox(right, ctx, c => c.IncludeColonistNames, (c, v) => c.IncludeColonistNames = v, "RimMind.Context.IncludeColonistNames", "RimMind.Context.IncludeColonistNames.Desc");
            DrawCheckbox(right, ctx, c => c.IncludeWealth, (c, v) => c.IncludeWealth = v, "RimMind.Context.IncludeWealth", "RimMind.Context.IncludeWealth.Desc");
            DrawCheckbox(right, ctx, c => c.IncludeFood, (c, v) => c.IncludeFood = v, "RimMind.Context.IncludeFood", "RimMind.Context.IncludeFood.Desc");
            DrawCheckbox(right, ctx, c => c.IncludeSeason, (c, v) => c.IncludeSeason = v, "RimMind.Context.IncludeSeason", "RimMind.Context.IncludeSeason.Desc");
            DrawCheckbox(right, ctx, c => c.IncludeWeather, (c, v) => c.IncludeWeather = v, "RimMind.Context.IncludeWeather", "RimMind.Context.IncludeWeather.Desc");
            DrawCheckbox(right, ctx, c => c.IncludeThreats, (c, v) => c.IncludeThreats = v, "RimMind.Context.IncludeThreats", "RimMind.Context.IncludeThreats.Desc");

            float rightH = right.CurHeight;
            right.End();
            return rightH;
        }

        private static void DrawBudgetSection(Listing_Standard listing, ISettingsProvider s, IContextSettings ctx)
        {
            SettingsUIDrawer.DrawSectionHeader(listing, "RimMind.Context.Budget".Translate());
            listing.LabelWithTooltip($"{"RimMind.Context.ContextBudget".Translate()}: {ctx.ContextBudget:F2} ({(int)(ctx.ContextBudget * 100)}%)", "RimMind.Context.ContextBudget.Desc".Translate());
            ctx.ContextBudget = listing.SliderWithTooltip(ctx.ContextBudget, 0.1f, 2.0f, "RimMind.Context.ContextBudget.Desc".Translate());

            listing.Gap(8f);

            listing.LabelWithTooltip($"{"RimMind.Settings.ContextDiffLifetime".Translate()}: {s.ContextDiffLifetimeTicks / 60f:F0}s ({s.ContextDiffLifetimeTicks} ticks)", "RimMind.Settings.ContextDiffLifetime.Desc".Translate());
            s.ContextDiffLifetimeTicks = (int)listing.SliderWithTooltip(s.ContextDiffLifetimeTicks, 300f, 3000f, "RimMind.Settings.ContextDiffLifetime.Desc".Translate());

            listing.Gap(6f);
            var calibrateSec = s.ContextCalibrateInterval / 60f;
            listing.LabelWithTooltip($"{"RimMind.Settings.CalibrateInterval".Translate()}: {calibrateSec:F0}s ({s.ContextCalibrateInterval} ticks)", "RimMind.Settings.CalibrateInterval.Desc".Translate());
            s.ContextCalibrateInterval = (int)listing.SliderWithTooltip(s.ContextCalibrateInterval, 5000f, 60000f, "RimMind.Settings.CalibrateInterval.Desc".Translate());
        }

        private static void DrawPresetCards(Listing_Standard listing, IContextSettings ctx)
        {
            SettingsUIDrawer.DrawSectionHeader(listing, "RimMind.Context.Presets".Translate(), "RimMind.Context.Desc".Translate());

            var presets = new[] { ContextPreset.Minimal, ContextPreset.Standard, ContextPreset.Full, ContextPreset.Custom };
            const float gap = 10f;
            const float h = 34f;
            float totalW = listing.ColumnWidth;
            float w = (totalW - gap * (presets.Length - 1)) / presets.Length;
            Rect row = listing.GetRect(h);

            for (int i = 0; i < presets.Length; i++)
            {
                var preset = presets[i];
                bool selected = _selectedPreset == preset;
                Rect box = new Rect(row.x + (w + gap) * i, row.y, w, h);
                string label = $"RimMind.Context.Preset.{preset}".Translate();

                if (selected)
                {
                    Widgets.DrawAtlas(box, Widgets.ButtonBGAtlasClick);
                    TextAnchor prevAnchor = Text.Anchor;
                    Text.Anchor = TextAnchor.MiddleCenter;
                    Widgets.Label(box, label);
                    Text.Anchor = prevAnchor;

                    if (Widgets.ButtonInvisible(box))
                    {
                        _selectedPreset = preset;
                        if (preset != ContextPreset.Custom)
                            ctx.ApplyPreset(preset);
                    }
                }
                else
                {
                    if (Widgets.ButtonText(box, label))
                    {
                        _selectedPreset = preset;
                        if (preset != ContextPreset.Custom)
                            ctx.ApplyPreset(preset);
                    }
                }

                TooltipHandler.TipRegion(box, $"RimMind.Context.Preset.{preset}.Desc".Translate());
            }
            listing.Gap(4f);
        }
    }
}
