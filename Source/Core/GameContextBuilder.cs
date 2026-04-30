using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimMind.Core.Npc;
using RimMind.Core.Prompt;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace RimMind.Core.Internal
{
    /// <summary>
    /// 构建游戏状态上下文文本，供各模块组装 AI Prompt。
    /// 根据 RimMindCoreSettings.Context 过滤器选择性输出字段。
    /// 所有方法均在主线程调用。
    /// </summary>
    public static class GameContextBuilder
    {
        // ── 地图级上下文 ──────────────────────────────────────────────────────

        /// <summary>
        /// 构建地图状态摘要（约 100~300 token）。
        /// brief=true 时只输出关键字段，适合 AIAdvisor 子模块使用。
        /// </summary>
        public static string BuildMapContext(Map map, bool brief = false)
        {
            if (map == null) return string.Empty;

            var ctx = RimMindCoreMod.Settings.Context;
            var sb = new StringBuilder();
            sb.AppendLine("RimMind.Core.Prompt.MapStatusHeader".Translate());

            // 游戏时间
            if (ctx.IncludeGameTime)
            {
                long ticks = Find.TickManager.TicksAbs;
                Vector2 longLat = Find.WorldGrid.LongLatOf(map.Tile);
                int hour = GenDate.HourOfDay(ticks, longLat.x);
                string dateStr = GenDate.DateFullStringAt(ticks, longLat);
                sb.AppendLine("RimMind.Core.Prompt.TimeFormat".Translate(dateStr, $"{hour:D2}"));
            }

            // 殖民者数量 + 名单
            if (ctx.IncludeColonistCount)
            {
                var colonists = map.mapPawns.FreeColonistsSpawned;
                if (ctx.IncludeColonistNames && colonists.Count > 0)
                {
                    var names = colonists.Select(p => p.Name.ToStringShort);
                    string nameList = string.Join(", ", names);
                    sb.AppendLine("RimMind.Core.Prompt.ColonistCount".Translate(colonists.Count, nameList));
                }
                else
                {
                    sb.AppendLine("RimMind.Core.Prompt.ColonistCountBrief".Translate(colonists.Count));
                }
            }

            // 犯人名单
            if (ctx.IncludeColonistNames)
            {
                var prisoners = map.mapPawns.PrisonersOfColonySpawned;
                if (prisoners.Count > 0)
                {
                    var names = prisoners.Select(p => p.Name.ToStringShort);
                    string nameList = string.Join(", ", names);
                    sb.AppendLine("RimMind.Core.Prompt.PrisonerCount".Translate(prisoners.Count, nameList));
                }
            }

            // 威胁/财富
            if (ctx.IncludeWealth)
            {
                float wealth = map.wealthWatcher.WealthTotal;
                string threat = ThreatLabel(wealth);
                sb.AppendLine("RimMind.Core.Prompt.WealthWithThreat".Translate($"{wealth:F0}", threat));
            }

            if (ctx.IncludeThreats)
            {
                float wealth = map.wealthWatcher.WealthTotal;
                string threat = ThreatLabel(wealth);
                sb.AppendLine("RimMind.Core.Prompt.ThreatLevel".Translate(threat));
            }

            // 食物
            if (ctx.IncludeFood)
            {
                float foodNutrition = 0f;
                for (int i = 0; i < DefDatabase<ThingDef>.AllDefsListForReading.Count; i++)
                {
                    var def = DefDatabase<ThingDef>.AllDefsListForReading[i];
                    if (def.IsNutritionGivingIngestible && def.ingestible != null
                        && def.ingestible.HumanEdible && !def.IsCorpse)
                    {
                        foodNutrition += map.resourceCounter.GetCount(def) * def.ingestible.CachedNutrition;
                    }
                }
                sb.AppendLine("RimMind.Core.Prompt.FoodStorage".Translate($"{foodNutrition:F0}"));
            }

            // 季节/天气
            if (ctx.IncludeSeason)
                sb.Append("RimMind.Core.Prompt.Season".Translate(GenLocalDate.Season(map).Label()));
            if (ctx.IncludeWeather)
                sb.AppendLine("RimMind.Core.Prompt.Weather".Translate(map.weatherManager.curWeather.label));
            else if (ctx.IncludeSeason)
                sb.AppendLine();

            return sb.ToString().TrimEnd();
        }

        // ── Pawn 级上下文 ─────────────────────────────────────────────────────

        /// <summary>
        /// 构建小人客观游戏状态（不含扩展 mod 注入内容）。
        /// 完整上下文请用 ContextEngine.BuildSnapshot。
        /// </summary>
        public static string BuildPawnContext(Pawn pawn)
        {
            if (pawn == null) return string.Empty;

            var ctx = RimMindCoreMod.Settings.Context;
            var sb = new StringBuilder();
            sb.Append("RimMind.Core.Prompt.PawnStatusHeader".Translate(pawn.Name.ToStringShort) + "  ");

            var basics = new List<string>();
            if (ctx.IncludeAge) basics.Add("RimMind.Core.Prompt.AgeFormat".Translate(pawn.ageTracker.AgeBiologicalYears));
            if (ctx.IncludeGender) basics.Add(pawn.gender.GetLabel());
            if (ctx.IncludeRace)
            {
                if (ModsConfig.BiotechActive && pawn.genes?.Xenotype != null)
                    basics.Add(pawn.genes.XenotypeLabel);
                else
                    basics.Add(pawn.def.label);
            }
            if (basics.Count > 0) sb.AppendLine(string.Join("  ", basics));
            else sb.AppendLine();

            // 基因（需要 Biotech DLC）
            if (ctx.IncludeGenes && ModsConfig.BiotechActive && pawn.genes?.GenesListForReading != null)
            {
                var notableGenes = pawn.genes.GenesListForReading
                    .Where(g => g.def.biostatMet != 0 || g.def.biostatCpx != 0)
                    .OrderByDescending(g => Mathf.Abs(g.def.biostatMet) + g.def.biostatCpx)
                    .Take(5)
                    .Select(g => g.def.LabelCap);
                if (notableGenes.Any())
                    sb.AppendLine("RimMind.Core.Prompt.Genes".Translate(string.Join(", ", notableGenes)));
            }

            // 背景故事（童年/成年职称）
            if (ctx.IncludeBackstory && pawn.story != null)
            {
                var parts = new List<string>();
                if (pawn.story.Childhood != null)
                    parts.Add("RimMind.Core.Prompt.Childhood".Translate(pawn.story.Childhood.TitleCapFor(pawn.gender)));
                if (pawn.story.Adulthood != null)
                    parts.Add("RimMind.Core.Prompt.Adulthood".Translate(pawn.story.Adulthood.TitleCapFor(pawn.gender)));
                if (parts.Count > 0)
                    sb.AppendLine("RimMind.Core.Prompt.Backstory".Translate(string.Join("  ", parts)));
            }

            // 意识形态（需要 Ideology DLC）
            if (ctx.IncludeIdeology && ModsConfig.IdeologyActive && pawn.ideo?.Ideo != null)
            {
                var ideo = pawn.ideo.Ideo;
                var memes = ideo.memes?
                    .Where(m => m != null)
                    .Select(m => m.LabelCap.Resolve())
                    .Where(s => !string.IsNullOrEmpty(s))
                    .ToList();
                string memeStr = memes?.Count > 0 ? $" [{string.Join(", ", memes)}]" : "";
                sb.AppendLine("RimMind.Core.Prompt.IdeologyFormat".Translate(ideo.name, memeStr));
            }

            // 心情（含精神崩溃状态）
            if (ctx.IncludeMood && pawn.needs?.mood != null)
            {
                var mood = pawn.needs.mood;
                if (pawn.InMentalState)
                    sb.AppendLine("RimMind.Core.Prompt.MoodBreak".Translate(mood.MoodString, pawn.MentalState?.InspectLine));
                else if (pawn.Downed)
                    sb.AppendLine("RimMind.Core.Prompt.MoodDowned".Translate(mood.MoodString));
                else
                    sb.AppendLine("RimMind.Core.Prompt.MoodPercent".Translate(mood.MoodString, $"{mood.CurLevelPercentage * 100f:F0}"));
            }

            // 心情因子（显著 Thought）
            if (ctx.IncludeMoodThoughts && pawn.needs?.mood?.thoughts != null)
            {
                var allThoughts = new List<Thought>();
                pawn.needs.mood.thoughts.GetAllMoodThoughts(allThoughts);
                var factors = new List<string>();
                foreach (var t in allThoughts)
                {
                    float offset = t.MoodOffset();
                    if (Mathf.Abs(offset) >= 1f)
                        factors.Add($"{t.LabelCap}({offset:+0;-0})");
                }
                if (factors.Count > 0)
                    sb.AppendLine("RimMind.Core.Prompt.MoodFactors".Translate(string.Join(", ", factors.Take(8))));
            }

            // 主要健康问题（格式：身体部位：病症名称，与游戏健康面板一致）
            if (ctx.IncludeHealth)
            {
                var hediffs = pawn.health?.hediffSet?.hediffs;
                if (hediffs != null)
                {
                    var notable = new List<string>();
                    foreach (var h in hediffs)
                    {
                        if (!h.def.isBad || h.Severity < 0.05f || !h.Visible) continue;
                        string partLabel = h.Part?.Label ?? "RimMind.Core.Prompt.FullBody".Translate();
                        string hediffLabel = h.LabelCap;
                        notable.Add($"{partLabel}: {hediffLabel}");
                    }
                    if (notable.Count > 0)
                        sb.AppendLine("RimMind.Core.Prompt.HealthIssues".Translate(string.Join(", ", notable.Take(8))));
                }
            }

            // 行动能力（跳过精确 100% 的项，高于或低于均显示）
            if (ctx.IncludeCapacities && pawn.health?.capacities != null)
            {
                var low = new List<string>();
                foreach (var cap in DefDatabase<PawnCapacityDef>.AllDefsListForReading)
                {
                    if (!cap.showOnHumanlikes) continue;
                    float level = pawn.health.capacities.GetLevel(cap);
                    if (level >= 0.995f && level <= 1.005f) continue;
                    string pct = $"{level * 100f:F0}%";
                    low.Add($"{cap.LabelCap}{pct}");
                }
                if (low.Count > 0)
                    sb.AppendLine("RimMind.Core.Prompt.Capacities".Translate(string.Join(", ", low)));
            }

            // 技能
            if (ctx.IncludeSkills && pawn.skills != null)
            {
                var skills = new List<string>();
                foreach (var skill in pawn.skills.skills)
                {
                    if (skill.levelInt >= ctx.MinSkillLevel)
                        skills.Add($"{skill.def.label}({skill.levelInt})");
                }
                if (skills.Count > 0)
                    sb.AppendLine("RimMind.Core.Prompt.Skills".Translate(string.Join("  ", skills)));
            }

            // 当前任务（用 GetReport() 获取含目标的描述）
            if (ctx.IncludeCurrentJob)
            {
                string jobLabel = pawn.jobs?.curDriver?.GetReport()
                    ?? pawn.CurJob?.def?.label
                    ?? "RimMind.Core.Prompt.None".Translate();
                sb.AppendLine("RimMind.Core.Prompt.CurrentJob".Translate(jobLabel));
            }

            // 工作分配（已启用工种，按优先级排序）
            if (ctx.IncludeWorkPriorities && pawn.workSettings != null)
            {
                var enabled = new List<(int pri, string label)>();
                foreach (var wt in DefDatabase<WorkTypeDef>.AllDefsListForReading)
                {
                    int pri = pawn.workSettings.GetPriority(wt);
                    if (pri > 0)
                        enabled.Add((pri, wt.labelShort));
                }
                enabled.Sort((a, b) => a.pri.CompareTo(b.pri));
                if (enabled.Count > 0)
                    sb.AppendLine("RimMind.Core.Prompt.WorkPriorities".Translate(string.Join("  ", enabled.Select(e => $"{e.label}({e.pri})"))));
            }

            // 特性
            if (ctx.IncludeTraits && pawn.story?.traits != null)
            {
                var traits = new List<string>();
                foreach (var t in pawn.story.traits.allTraits)
                {
                    traits.Add(t.LabelCap);
                    if (traits.Count >= 5) break;
                }
                if (traits.Count > 0)
                    sb.AppendLine("RimMind.Core.Prompt.Traits".Translate(string.Join(", ", traits)));
            }

            // 装备（武器 + 服装）
            if (ctx.IncludeEquipment)
            {
                var parts = new List<string>();

                var weapon = pawn.equipment?.Primary;
                if (weapon != null)
                    parts.Add("RimMind.Core.Prompt.Weapon".Translate(LabelWithQuality(weapon)));

                if (pawn.apparel?.WornApparel != null)
                {
                    var apparel = new List<string>();
                    foreach (var a in pawn.apparel.WornApparel)
                        apparel.Add(LabelWithQuality(a));
                    if (apparel.Count > 0)
                        parts.Add("RimMind.Core.Prompt.Apparel".Translate(string.Join(", ", apparel)));
                }

                if (parts.Count > 0)
                    sb.AppendLine(string.Join("  ", parts));
            }

            if (ctx.IncludeInventory)
            {
                var innerContainer = pawn.inventory?.innerContainer;
                if (innerContainer != null && innerContainer.Count > 0)
                {
                    var items = new Dictionary<string, int>();
                    foreach (var thing in innerContainer)
                    {
                        string key = thing.def?.defName ?? thing.Label;
                        if (!items.ContainsKey(key))
                            items[key] = 0;
                        items[key] += thing.stackCount;
                    }
                    var itemStrs = items.OrderByDescending(kv => kv.Value)
                        .Take(8)
                        .Select(kv =>
                        {
                            var def = DefDatabase<ThingDef>.GetNamedSilentFail(kv.Key);
                            string label = def?.LabelCap ?? kv.Key;
                            return kv.Value > 1 ? $"{label}×{kv.Value}" : label;
                        });
                    sb.AppendLine("RimMind.Core.Prompt.Inventory".Translate(string.Join(", ", itemStrs)));
                }
            }

            // 位置（房间类型 + 温度）
            if (ctx.IncludeLocation && pawn.Map != null)
            {
                var room = pawn.GetRoom();
                string roomLabel = (room != null && !room.PsychologicallyOutdoors)
                    ? room.Role?.label ?? "RimMind.Core.Prompt.Room.Indoors".Translate()
                    : "RimMind.Core.Prompt.Room.Outdoors".Translate();
                int temp = Mathf.RoundToInt(pawn.Position.GetTemperature(pawn.Map));
                sb.AppendLine("RimMind.Core.Prompt.Location".Translate(roomLabel, $"{temp}"));
            }

            // 社交关系
            if (ctx.IncludeRelations && pawn.relations?.DirectRelations != null)
            {
                var relParts = new List<string>();
                foreach (var rel in pawn.relations.DirectRelations)
                {
                    if (rel.otherPawn == null || rel.def == null) continue;
                    string otherName = rel.otherPawn.Name?.ToStringShort ?? rel.otherPawn.LabelShort;
                    relParts.Add($"{rel.def.label}({otherName})");
                    if (relParts.Count >= 6) break;
                }
                if (relParts.Count > 0)
                    sb.AppendLine("RimMind.Core.Prompt.Relations".Translate(string.Join(", ", relParts)));
            }

            // 战斗状态
            if (ctx.IncludeCombatStatus)
            {
                if (pawn.Map != null)
                {
                    bool inCombat = pawn.mindState?.enemyTarget != null
                                 || (pawn.CurJob != null && pawn.CurJob.def == JobDefOf.AttackMelee)
                                 || (pawn.CurJob != null && pawn.CurJob.def == JobDefOf.AttackStatic);
                    if (inCombat)
                    {
                        string targetLabel = pawn.mindState?.enemyTarget is Pawn enemy
                            ? enemy.Name?.ToStringShort ?? enemy.LabelShort
                            : pawn.mindState?.enemyTarget?.Label ?? "RimMind.Core.Prompt.Unknown".Translate();
                        sb.AppendLine("RimMind.Core.Prompt.InCombat".Translate(targetLabel));
                    }
                }
                if (pawn.Drafted)
                    sb.AppendLine("RimMind.Core.Prompt.Drafted".Translate());
            }

            // 周围环境
            if (ctx.IncludeSurroundings && pawn.Map != null)
            {
                string surroundings = BuildSurroundings(pawn);
                if (!string.IsNullOrEmpty(surroundings))
                    sb.AppendLine("RimMind.Core.Prompt.Surroundings".Translate(surroundings));
            }

            return sb.ToString().TrimEnd();
        }

        // ── 历史事件上下文 ────────────────────────────────────────────────────

        /// <summary>
        /// 从 WorldComponent 获取最近 N 条 AIStoryteller 历史记录。
        /// AIStoryteller 未安装时返回空字符串。
        /// </summary>
        private static string BuildSurroundings(Pawn pawn, int radius = 5, int maxItems = 8)
        {
            var map = pawn.Map;
            var buildings = new List<string>();
            var items = new Dictionary<string, int>();
            var animals = new List<string>();

            foreach (var c in GenRadial.RadialCellsAround(pawn.Position, radius, true))
            {
                if (!c.InBounds(map)) continue;
                var room = pawn.GetRoom();
                if (room != null && !room.PsychologicallyOutdoors)
                {
                    var cRoom = c.GetRoom(map);
                    if (cRoom != room) continue;
                }

                var things = c.GetThingList(map);
                for (int i = 0; i < things.Count; i++)
                {
                    var thing = things[i];
                    if (thing.DestroyedOrNull() || thing == pawn) continue;

                    if (thing is Pawn otherPawn)
                    {
                        if (otherPawn.RaceProps.Animal && !otherPawn.Dead)
                            animals.Add(otherPawn.LabelShort ?? otherPawn.def.label);
                        continue;
                    }

                    if (thing.def.category == ThingCategory.Building)
                    {
                        if (buildings.Count < 5)
                            buildings.Add(thing.def.LabelCap);
                    }
                    else if (thing.def.category == ThingCategory.Item)
                    {
                        string key = thing.def.defName;
                        if (!items.ContainsKey(key))
                            items[key] = 0;
                        items[key] += thing.stackCount;
                    }
                }
            }

            var parts = new List<string>();
            if (buildings.Count > 0)
                parts.Add("RimMind.Core.Prompt.SurroundingsBuildings".Translate(string.Join(", ", buildings.Distinct().Take(5))));
            if (items.Count > 0)
            {
                var itemStrs = items.OrderByDescending(kv => kv.Value)
                    .Take(maxItems)
                    .Select(kv => $"{DefDatabase<ThingDef>.GetNamedSilentFail(kv.Key)?.LabelCap ?? kv.Key}×{kv.Value}");
                parts.Add("RimMind.Core.Prompt.SurroundingsItems".Translate(string.Join(", ", itemStrs)));
            }
            if (animals.Count > 0)
                parts.Add("RimMind.Core.Prompt.SurroundingsAnimals".Translate(string.Join(", ", animals.Distinct().Take(4))));

            return parts.Count > 0 ? string.Join("  ", parts) : string.Empty;
        }

        private static string LabelWithQuality(Thing thing)
        {
            string label = thing.LabelCap;
            if (thing.TryGetQuality(out QualityCategory qc))
                label += $"({qc.GetLabel()})";
            if (thing.def.useHitPoints && thing.HitPoints < thing.MaxHitPoints * 0.5f)
                label += " " + "RimMind.Core.Prompt.Damaged".Translate();
            return label;
        }

        private static string ThreatLabel(float wealth)
        {
            return wealth > 200000 ? "RimMind.Core.Prompt.Threat.Extreme".Translate()
                 : wealth > 100000 ? "RimMind.Core.Prompt.Threat.High".Translate()
                 : wealth > 50000 ? "RimMind.Core.Prompt.Threat.Medium".Translate()
                 : "RimMind.Core.Prompt.Threat.Low".Translate();
        }

        // ── 基础游戏状态收集（回退路径） ────────────────────────────────────

        /// <summary>
        /// 收集基础游戏状态文本，用于 ContextEngine 不可用时回退填充 game_state_info。
        /// 按 npcId 查找对应 Pawn，拼接地图上下文 + Pawn 上下文。
        /// </summary>
        public static string CollectBasicGameState(string npcId)
        {
            var sb = new StringBuilder();
            var pawn = NpcManager.FindPawnByNpcId(npcId);

            if (pawn != null)
            {
                if (pawn.Map != null)
                    sb.AppendLine(BuildMapContext(pawn.Map));
                sb.AppendLine(BuildPawnContext(pawn));
            }
            else
            {
                var map = Find.CurrentMap;
                if (map != null)
                    sb.AppendLine(BuildMapContext(map));
            }

            return sb.ToString().TrimEnd();
        }

        // ── PromptSection 版本 ──────────────────────────────────────────────

        public static PromptSection BuildMapContextSection(Map map, bool brief = false)
        {
            var section = new PromptSection("map_context", BuildMapContext(map, brief), PromptSection.PriorityKeyState);
            section.Compress = _ => BuildMapContext(map, brief: true);
            return section;
        }

        public static PromptSection BuildPawnContextSection(Pawn pawn)
        {
            var section = new PromptSection("pawn_context", BuildPawnContext(pawn), PromptSection.PriorityKeyState);
            section.Compress = _ => BuildCompactPawnContext(pawn);
            return section;
        }

        public static PromptSection BuildCompactPawnContextSection(Pawn pawn)
        {
            return new PromptSection("pawn_compact", BuildCompactPawnContext(pawn), PromptSection.PriorityKeyState);
        }

        // ── 精简 Pawn 上下文 ──────────────────────────────────────────────

        public static string BuildCompactPawnContext(Pawn pawn)
        {
            if (pawn == null) return string.Empty;

            var sb = new StringBuilder();
            sb.Append(pawn.Name.ToStringShort + "  ");

            var basics = new List<string>();
            basics.Add("RimMind.Core.Prompt.AgeFormat".Translate(pawn.ageTracker.AgeBiologicalYears));
            basics.Add(pawn.gender.GetLabel());
            basics.Add(pawn.def.label);
            sb.AppendLine(string.Join("  ", basics));

            if (pawn.needs?.mood != null)
            {
                string moodLabel = pawn.InMentalState ? "RimMind.Core.Prompt.CompactMentalBreak".Translate()
                    : pawn.Downed ? "RimMind.Core.Prompt.CompactDowned".Translate()
                    : $"{pawn.needs.mood.CurLevelPercentage * 100f:F0}%";
                sb.AppendLine("RimMind.Core.Prompt.CompactMood".Translate(moodLabel));
            }

            var hediffs = pawn.health?.hediffSet?.hediffs;
            if (hediffs != null)
            {
                var notable = new List<string>();
                foreach (var h in hediffs)
                {
                    if (!h.def.isBad || h.Severity < 0.05f || !h.Visible) continue;
                    string partLabel = h.Part?.Label ?? "RimMind.Core.Prompt.FullBody".Translate();
                    notable.Add($"{partLabel}:{h.LabelCap}");
                    if (notable.Count >= 3) break;
                }
                if (notable.Count > 0)
                    sb.AppendLine("RimMind.Core.Prompt.CompactHealth".Translate(string.Join(", ", notable)));
            }

            string jobLabel = pawn.jobs?.curDriver?.GetReport()
                ?? pawn.CurJob?.def?.label
                ?? "RimMind.Core.Prompt.None".Translate();
            sb.AppendLine("RimMind.Core.Prompt.CompactJob".Translate(jobLabel));

            if (pawn.Map != null)
            {
                var room = pawn.GetRoom();
                string roomLabel = (room != null && !room.PsychologicallyOutdoors)
                    ? "RimMind.Core.Prompt.Room.Indoors".Translate()
                    : "RimMind.Core.Prompt.Room.Outdoors".Translate();
                int temp = Mathf.RoundToInt(pawn.Position.GetTemperature(pawn.Map));
                sb.AppendLine("RimMind.Core.Prompt.CompactLocation".Translate(roomLabel, $"{temp}"));
            }

            var weapon = pawn.equipment?.Primary;
            if (weapon != null)
                sb.AppendLine("RimMind.Core.Prompt.CompactWeapon".Translate(LabelWithQuality(weapon)));

            if (pawn.Drafted)
                sb.AppendLine("RimMind.Core.Prompt.Drafted".Translate());
            if (pawn.mindState?.enemyTarget != null)
            {
                string targetLabel = pawn.mindState.enemyTarget is Pawn enemy
                    ? enemy.Name?.ToStringShort ?? enemy.LabelShort
                    : pawn.mindState.enemyTarget.Label ?? "RimMind.Core.Prompt.Unknown".Translate();
                sb.AppendLine("RimMind.Core.Prompt.InCombat".Translate(targetLabel));
            }

            return sb.ToString().TrimEnd();
        }

        // ── Per-Key 提取方法（供 ContextKeyRegistry 使用） ──────────────────

        public static string ExtractPawnBaseInfo(Pawn pawn)
        {
            if (pawn == null) return "";
            var parts = new List<string>();
            parts.Add(pawn.Name?.ToStringShort ?? pawn.LabelShort);
            parts.Add($"{pawn.ageTracker.AgeBiologicalYears}yo");
            parts.Add(pawn.gender.GetLabel());
            if (ModsConfig.BiotechActive && pawn.genes?.Xenotype != null)
                parts.Add(pawn.genes.XenotypeLabel);
            else
                parts.Add(pawn.def.label);
            if (pawn.story?.Childhood != null)
                parts.Add(pawn.story.Childhood.TitleCapFor(pawn.gender));
            if (pawn.story?.Adulthood != null)
                parts.Add(pawn.story.Adulthood.TitleCapFor(pawn.gender));
            if (pawn.story?.traits != null)
            {
                var traits = new List<string>();
                foreach (var t in pawn.story.traits.allTraits)
                {
                    traits.Add(t.LabelCap);
                    if (traits.Count >= 5) break;
                }
                if (traits.Count > 0) parts.Add($"Traits: {string.Join(", ", traits)}");
            }
            return string.Join(" | ", parts);
        }

        public static string ExtractFixedRelations(Pawn pawn)
        {
            if (pawn == null || pawn.relations?.DirectRelations == null) return "";
            var parts = new List<string>();
            foreach (var rel in pawn.relations.DirectRelations)
            {
                if (rel.otherPawn == null || rel.def == null) continue;
                string otherName = rel.otherPawn.Name?.ToStringShort ?? rel.otherPawn.LabelShort;
                parts.Add($"{rel.def.label}({otherName})");
                if (parts.Count >= 6) break;
            }
            return parts.Count > 0 ? string.Join(", ", parts) : "";
        }

        public static string ExtractIdeology(Pawn pawn)
        {
            if (pawn == null || !ModsConfig.IdeologyActive || pawn.ideo?.Ideo == null) return "";
            var ideo = pawn.ideo.Ideo;
            var memes = ideo.memes?.Where(m => m != null).Select(m => m.LabelCap.Resolve())
                .Where(s => !string.IsNullOrEmpty(s)).ToList();
            string memeStr = memes?.Count > 0 ? $" [{string.Join(", ", memes)}]" : "";
            return $"{ideo.name}{memeStr}";
        }

        public static string ExtractSkillsSummary(Pawn pawn)
        {
            if (pawn == null || pawn.skills == null) return "";
            var top = pawn.skills.skills
                .OrderByDescending(s => s.Level)
                .Take(5)
                .Select(s => $"{s.def.label}({s.Level})");
            return string.Join("  ", top);
        }

        public static string ExtractCurrentArea(Pawn pawn)
        {
            if (pawn == null || pawn.Map == null) return "";
            var room = pawn.GetRoom();
            string roomLabel = (room != null && !room.PsychologicallyOutdoors)
                ? room.Role?.label ?? "RimMind.Core.Prompt.Room.Indoors".Translate()
                : "RimMind.Core.Prompt.Room.Outdoors".Translate();
            int temp = Mathf.RoundToInt(pawn.Position.GetTemperature(pawn.Map));
            return $"{roomLabel}, {temp}°C";
        }

        public static string ExtractWeather(Pawn pawn)
        {
            if (pawn == null || pawn.Map == null) return "";
            return pawn.Map.weatherManager?.curWeather?.LabelCap ?? "Clear";
        }

        public static string ExtractTimeOfDay(Pawn pawn)
        {
            if (pawn == null || pawn.Map == null) return "";
            long ticks = Find.TickManager.TicksAbs;
            Vector2 longLat = Find.WorldGrid.LongLatOf(pawn.Map.Tile);
            int hour = GenDate.HourOfDay(ticks, longLat.x);
            string dateStr = GenDate.DateFullStringAt(ticks, longLat);
            return $"{dateStr} {hour:D2}:00";
        }

        public static string ExtractNearbyPawns(Pawn pawn)
        {
            if (pawn == null || pawn.Map == null) return "";
            var nearby = pawn.Map.mapPawns?.FreeColonistsSpawned?
                .Where(p => p != pawn && p.Position.DistanceTo(pawn.Position) < 15f)
                .Select(p => p.Name?.ToStringShort ?? p.LabelShort)
                .Take(5);
            return nearby != null && nearby.Any() ? string.Join(", ", nearby) : "";
        }

        public static string ExtractSeason(Pawn pawn)
        {
            if (pawn == null || pawn.Map == null) return "";
            return GenLocalDate.Season(pawn.Map).Label();
        }

        public static string ExtractColonyStatus(Pawn pawn)
        {
            if (pawn == null || pawn.Map == null) return "";
            var map = pawn.Map;
            int colonists = map.mapPawns?.FreeColonistsCount ?? 0;
            float wealth = map.wealthWatcher?.WealthTotal ?? 0f;
            int threatCount = map.mapPawns?.AllPawns?
                .Count(p => p.Faction != null && p.Faction.HostileTo(Faction.OfPlayer) && !p.Dead && !p.Downed) ?? 0;
            return "RimMind.Core.Prompt.Colony.Status".Translate(colonists, $"{wealth:F0}", threatCount);
        }

        public static string ExtractHealth(Pawn pawn)
        {
            if (pawn == null) return "";
            var hediffs = pawn.health?.hediffSet?.hediffs;
            if (hediffs == null) return "";
            var notable = new List<string>();
            foreach (var h in hediffs)
            {
                if (!h.Visible) continue;
                notable.Add(h.LabelCap);
                if (notable.Count >= 5) break;
            }
            return notable.Count > 0 ? string.Join(", ", notable) : "RimMind.Core.Prompt.Health.Healthy".Translate();
        }

        public static string ExtractMood(Pawn pawn)
        {
            if (pawn == null || pawn.needs?.mood == null) return "";
            var mood = pawn.needs.mood;
            if (pawn.InMentalState)
                return "RimMind.Core.Prompt.Mood.MentalBreak".Translate(pawn.MentalState?.InspectLine ?? "");
            return $"{mood.CurLevelPercentage * 100f:F0}%";
        }

        public static string ExtractCurrentJob(Pawn pawn)
        {
            if (pawn == null) return "";
            return pawn.jobs?.curDriver?.GetReport() ?? pawn.CurJob?.def?.label ?? "RimMind.Core.Prompt.Job.Idle".Translate();
        }

        public static string ExtractCombatStatus(Pawn pawn)
        {
            if (pawn == null) return "";
            var parts = new List<string>();
            if (pawn.Drafted) parts.Add("RimMind.Core.Prompt.Combat.Drafted".Translate());
            if (pawn.mindState?.enemyTarget != null)
            {
                string target = pawn.mindState.enemyTarget is Pawn enemy
                    ? enemy.Name?.ToStringShort ?? enemy.LabelShort
                    : pawn.mindState.enemyTarget.Label ?? "RimMind.Core.Prompt.Unknown".Translate();
                parts.Add("RimMind.Core.Prompt.Combat.Fighting".Translate(target));
            }
            return parts.Count > 0 ? string.Join(" | ", parts) : "RimMind.Core.Prompt.Combat.NotInCombat".Translate();
        }

        public static string ExtractTargetInfo(Pawn pawn)
        {
            if (pawn == null || pawn.mindState?.enemyTarget == null) return "";
            var target = pawn.mindState.enemyTarget;
            string label = target is Pawn p ? $"{p.Name?.ToStringShort ?? p.LabelShort} (HP:{p.health?.summaryHealth?.SummaryHealthPercent * 100f:F0}%)" : target.Label;
            return "RimMind.Core.Prompt.Target.Info".Translate(label);
        }
    }
}
