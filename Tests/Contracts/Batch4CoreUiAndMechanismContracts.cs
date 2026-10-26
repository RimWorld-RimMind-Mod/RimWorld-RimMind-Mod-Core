#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using RimMind.Application.Common.Constants;
using RimMind.Application.Common.Interfaces.Mechanisms;
using RimMind.Application.Common.Models.Mechanisms;
using RimMind.Application.Common.Models.Tools;
using RimMind.Domain.Enums;
using RimMind.Domain.ValueObjects;
using RimMind.Infrastructure.Mechanisms;
using RimMind.Presentation.UI;
using UnityEngine;
using Verse;
using Xunit;

#if !NET48 && !V1_6
namespace Verse
{
    public sealed class LoadedLanguage
    {
        public string FolderName { get; set; } = "English";
    }

    public static class LanguageDatabase
    {
        public static LoadedLanguage? activeLanguage = new LoadedLanguage();
    }
}

namespace RimMind.Presentation.UI
{
    public static partial class UiText
    {
        private static readonly Dictionary<string, string> Cache = new Dictionary<string, string>(StringComparer.Ordinal);
        private static object? _cachedLanguage;

        internal static Func<string, bool>? CanTranslateOverride { get; set; }
        internal static Func<string, string>? TranslateOverride { get; set; }
        internal static Func<string, object[], string>? TranslateArgsOverride { get; set; }

        public static int CacheCount => Cache.Count;

        public static void ClearCache()
        {
            Cache.Clear();
            _cachedLanguage = null;
        }

        public static string Get(string key, string? fallback = null)
        {
            if (string.IsNullOrEmpty(key))
            {
                return fallback ?? string.Empty;
            }

            if (!UnityData.IsInMainThread)
            {
                return fallback ?? key;
            }

            EnsureCacheValid();

            if (Cache.TryGetValue(key, out var cached))
            {
                return cached;
            }

            if (!CanTranslateKey(key))
            {
                return fallback ?? key;
            }

            var translated = TranslateKey(key);
            Cache[key] = translated;
            return translated;
        }

        public static string Format(string key, params object[] args)
        {
            if (string.IsNullOrEmpty(key))
            {
                return string.Empty;
            }

            if (args == null || args.Length == 0)
            {
                return Get(key);
            }

            if (!UnityData.IsInMainThread || !CanTranslateKey(key))
            {
                return key;
            }

            return TranslateKeyWithArgs(key, args);
        }

        private static void EnsureCacheValid()
        {
            var activeLanguage = LanguageDatabase.activeLanguage;
            if (!ReferenceEquals(_cachedLanguage, activeLanguage))
            {
                Cache.Clear();
                _cachedLanguage = activeLanguage;
            }
        }

        private static bool CanTranslateKey(string key)
        {
            if (CanTranslateOverride != null)
            {
                return CanTranslateOverride(key);
            }

            return key.CanTranslate();
        }

        private static string TranslateKey(string key)
        {
            if (TranslateOverride != null)
            {
                return TranslateOverride(key);
            }

            return key.Translate().ToString();
        }

        private static string TranslateKeyWithArgs(string key, object[] args)
        {
            if (TranslateArgsOverride != null)
            {
                return TranslateArgsOverride(key, args);
            }

#pragma warning disable CS0618
            return key.Translate(args).ToString();
#pragma warning restore CS0618
        }
    }
}
#endif

namespace RimMindCore.Tests.Contracts
{
    [Collection("UI drawing")]
    public sealed class Batch4CoreUiAndMechanismContracts : IDisposable
    {
        public Batch4CoreUiAndMechanismContracts()
        {
            UnityData.IsInMainThread = true;
            LanguageDatabase.activeLanguage = new LoadedLanguage { FolderName = "English" };
            UiText.CanTranslateOverride = null;
            UiText.TranslateOverride = null;
            UiText.TranslateArgsOverride = null;
            UiText.ClearCache();
        }

        public void Dispose()
        {
            UnityData.IsInMainThread = true;
            UiText.CanTranslateOverride = null;
            UiText.TranslateOverride = null;
            UiText.TranslateArgsOverride = null;
            UiText.ClearCache();
            Widgets.ResetDrawing();
        }

        [Fact]
        public void UiText_Get_CachesStaticTranslationAndInvalidatesOnLanguageChange()
        {
            int translateCount = 0;
            UiText.CanTranslateOverride = _ => true;
            UiText.TranslateOverride = key =>
            {
                translateCount++;
                return $"[{LanguageDatabase.activeLanguage?.FolderName}:{key}]";
            };

            string first = UiText.Get("RimMind.Settings.Tab.Api");
            string second = UiText.Get("RimMind.Settings.Tab.Api");

            Assert.Equal("[English:RimMind.Settings.Tab.Api]", first);
            Assert.Equal(first, second);
            Assert.Equal(1, translateCount);
            Assert.Equal(1, UiText.CacheCount);

            LanguageDatabase.activeLanguage = new LoadedLanguage { FolderName = "ChineseSimplified" };

            string afterSwitch = UiText.Get("RimMind.Settings.Tab.Api");
            Assert.Equal("[ChineseSimplified:RimMind.Settings.Tab.Api]", afterSwitch);
            Assert.Equal(2, translateCount);
        }

        [Fact]
        public void UiText_Get_And_Format_RespectCanTranslateAndMainThreadGuards()
        {
            UiText.CanTranslateOverride = _ => false;
            Assert.Equal("Fallback Label", UiText.Get("RimMind.MissingKey", "Fallback Label"));
            Assert.Equal("RimMind.MissingKey", UiText.Get("RimMind.MissingKey"));
            Assert.Equal("RimMind.MissingFormat", UiText.Format("RimMind.MissingFormat", 42));
            Assert.Equal(0, UiText.CacheCount);

            UiText.CanTranslateOverride = _ => true;
            UiText.TranslateOverride = key => $"TR:{key}";
            UiText.TranslateArgsOverride = (key, args) => $"{key}={string.Join(",", args)}";

            UnityData.IsInMainThread = false;
            Assert.Equal("OffThreadFallback", UiText.Get("RimMind.Key", "OffThreadFallback"));
            Assert.Equal("RimMind.Key", UiText.Format("RimMind.Key", 7));

            UnityData.IsInMainThread = true;
            Assert.Equal("RimMind.Key=7", UiText.Format("RimMind.Key", 7));
        }

        [Fact]
        public void SettingsUIDrawer_DrawSelectableButton_HandlesSelectedAndUnselectedStatesWithTooltip()
        {
            Widgets.ResetDrawing();
            Text.Anchor = TextAnchor.UpperLeft;

            var selectedRect = new Rect(10f, 10f, 100f, 28f);
            Widgets.ClickLabel = "selected";
            bool selectedClicked = SettingsUIDrawer.DrawSelectableButton(selectedRect, "Responsive", selected: true, tooltip: "Responsive preset");

            Assert.True(selectedClicked);
            Assert.Equal(TextAnchor.UpperLeft, Text.Anchor);
            Assert.Contains(Widgets.Draws, entry => entry.Kind == "DrawAtlas");
            Assert.Contains(Widgets.Draws, entry => entry.Kind == "Label" && entry.Label == "Responsive");
            Assert.Contains(TooltipHandler.Tips, tip => tip.Text == "Responsive preset");

            Widgets.ResetDrawing();
            var unselectedRect = new Rect(120f, 10f, 100f, 28f);
            Widgets.ClickLabel = "Balanced";
            bool unselectedClicked = SettingsUIDrawer.DrawSelectableButton(unselectedRect, "Balanced", selected: false, tooltip: "Balanced preset");

            Assert.True(unselectedClicked);
            Assert.Contains(Widgets.Draws, entry => entry.Kind == "ButtonText" && entry.Label == "Balanced");
            Assert.Contains(TooltipHandler.Tips, tip => tip.Text == "Balanced preset");
        }

        [Fact]
        public void MechanismToolHandler_SinglePassJsonParsing_ExtractsAliasesAndTypedValuesOnce()
        {
            var metadata = new StubMechanismMetadata();
            var handler = new MechanismToolHandler(
                new StubMechanismReader(),
                new StubMechanismWriter(),
                new StubMechanismTrigger(),
                metadata,
                MechanismOperationType.Set);

            var json = @"{
                ""map_id"": 2,
                ""work_type"": ""Construction"",
                ""action"": ""assign_work"",
                ""priority"": 3,
                ""target_id"": 99,
                ""count"": 5,
                ""level"": 8,
                ""enabled"": true,
                ""drafted"": false,
                ""forced"": 1,
                ""x"": 14,
                ""z"": 27
            }";

            var root = MechanismToolHandler.TryParseArguments(json);
            Assert.NotNull(root);
            Assert.Equal(2, MechanismToolHandler.ExtractNullableInt(root, "map_id"));
            Assert.Equal(3, MechanismToolHandler.ExtractInt(root, "priority"));
            Assert.Equal(5, MechanismToolHandler.ExtractInt(root, "count"));
            Assert.Equal(8, MechanismToolHandler.ExtractInt(root, "level"));
            Assert.True(MechanismToolHandler.ExtractBool(root, "enabled"));
            Assert.False(MechanismToolHandler.ExtractBool(root, "drafted", defaultValue: true));
            Assert.True(MechanismToolHandler.ExtractBool(root, "forced"));
            Assert.Equal(14f, MechanismToolHandler.ExtractFloat(root, "x"));
            Assert.Equal(27f, MechanismToolHandler.ExtractFloat(root, "z"));

            var writeArgs = handler.BuildWriteArgs(new ToolCallArgs
            {
                ToolCallId = "call-1",
                ToolName = "pawn.work.set",
                ArgumentsJson = json,
                PawnId = 42,
                TraceId = "trace-1"
            });

            Assert.Equal(42, writeArgs.PawnId);
            Assert.Equal(2, writeArgs.MapId);
            Assert.Equal("Construction", writeArgs.DefName);
            Assert.Equal("assign_work", writeArgs.Action);
            Assert.Equal("3", writeArgs.ValueJson);
            Assert.NotNull(writeArgs.Params);
            Assert.Equal("Construction", writeArgs.Params!["work_type"]);
            Assert.Equal("99", writeArgs.Params["target_id"]);
            Assert.Equal("99", writeArgs.Params["target_pawn_id"]);
            Assert.Equal("14", writeArgs.Params["cell_x"]);
            Assert.Equal("27", writeArgs.Params["cell_z"]);
            Assert.Equal("5", writeArgs.Params["count"]);
            Assert.Equal("8", writeArgs.Params["level"]);
        }

        [Fact]
        public void Batch4_OwnedFiles_EnforceUiTextSelectableButtonAndSinglePassJObjectParse()
        {
            string mechanismSource = File.ReadAllText(ResolveSourcePath("Infrastructure", "Mechanisms", "MechanismToolHandler.cs"));
            Assert.Single(Regex.Matches(mechanismSource, @"JObject\.Parse\s*\("));
            Assert.DoesNotContain("MechanismToolHelper.", mechanismSource, StringComparison.Ordinal);

            string settingsDrawerSource = File.ReadAllText(ResolveSourcePath("Presentation", "UI", "SettingsUIDrawer.cs"));
            Assert.Contains("public static bool DrawSelectableButton(", settingsDrawerSource, StringComparison.Ordinal);
            Assert.Single(Regex.Matches(settingsDrawerSource, @"new Color\(0\.14f,\s*0\.17f,\s*0\.24f,\s*0\.75f\)"));

            Assert.Contains("SettingsUIDrawer.DrawSelectableButton", File.ReadAllText(ResolveSourcePath("Presentation", "UI", "ApiTabDrawer.cs")), StringComparison.Ordinal);
            Assert.Contains("SettingsUIDrawer.DrawSelectableButton", File.ReadAllText(ResolveSourcePath("Presentation", "UI", "ContextTabDrawer.cs")), StringComparison.Ordinal);
            Assert.Contains("SettingsUIDrawer.DrawSelectableButton", File.ReadAllText(ResolveSourcePath("Presentation", "UI", "PromptsTabDrawer.cs")), StringComparison.Ordinal);

            string[] uiFiles =
            {
                ResolveSourcePath("Presentation", "UI", "SettingsUIDrawer.cs"),
                ResolveSourcePath("Presentation", "UI", "ApiTabDrawer.cs"),
                ResolveSourcePath("Presentation", "UI", "ContextTabDrawer.cs"),
                ResolveSourcePath("Presentation", "UI", "PromptsTabDrawer.cs"),
                ResolveSourcePath("Presentation", "UI", "QueueTabDrawer.cs"),
                ResolveSourcePath("Presentation", "UI", "RemoteSyncSettingsUI.cs"),
                ResolveSourcePath("Presentation", "Runtime", "RimMindRuntimeGameComponent.cs")
            };

            foreach (string filePath in uiFiles)
            {
                string content = File.ReadAllText(filePath);
                Assert.Contains("UiText.", content, StringComparison.Ordinal);
                Assert.DoesNotContain(".Translate(", content, StringComparison.Ordinal);
            }
        }

        private static string ResolveSourcePath(params string[] segments)
        {
            var all = new string[segments.Length + 5];
            all[0] = AppContext.BaseDirectory;
            all[1] = "..";
            all[2] = "..";
            all[3] = "..";
            all[4] = "..";
            var prefix = Path.Combine(all[0], all[1], all[2], all[3], all[4], "Source");
            return Path.GetFullPath(Path.Combine(prefix, Path.Combine(segments)));
        }

        private sealed class StubMechanismMetadata : IMechanismMetadata
        {
            public string Id => MechanismId;
            public string OwnerModId => RimMindOwnerConsts.CoreModId;
            public string MechanismId => "pawn.work";
            public MechanismScope Scope => MechanismScope.Pawn;
            public MechanismRisk Risk => MechanismRisk.Moderate;
            public IReadOnlyList<MechanismOperationType> SupportedOperations { get; } =
                new[] { MechanismOperationType.Query, MechanismOperationType.Set };
            public MechanismDocs Docs { get; } = new MechanismDocs { Summary = "Stub work mechanism" };
            public MechanismRisk GetRiskForOperation(MechanismOperationType operation) => Risk;
            public IReadOnlyList<MechanismActionInfo>? GetWriteActions() => null;
        }

        private sealed class StubMechanismReader : IMechanismReader
        {
            public Task<Result<string, RimMindError>> ExecuteQueryAsync(MechanismReadArgs args, CancellationToken ct) =>
                Task.FromResult(Result<string, RimMindError>.Ok("{}"));

            public Task<Result<IReadOnlyList<MechanismEnumResult>, RimMindError>> ExecuteListAsync(int? pawnId, CancellationToken ct) =>
                Task.FromResult(Result<IReadOnlyList<MechanismEnumResult>, RimMindError>.Ok(Array.Empty<MechanismEnumResult>()));
        }

        private sealed class StubMechanismWriter : IMechanismWriter
        {
            public Task<Result<bool, RimMindError>> ExecuteSetAsync(MechanismWriteArgs args, CancellationToken ct) =>
                Task.FromResult(Result<bool, RimMindError>.Ok(true));

            public Task<Result<bool, RimMindError>> ExecuteAddAsync(MechanismWriteArgs args, CancellationToken ct) =>
                Task.FromResult(Result<bool, RimMindError>.Ok(true));

            public Task<Result<bool, RimMindError>> ExecuteRemoveAsync(MechanismWriteArgs args, CancellationToken ct) =>
                Task.FromResult(Result<bool, RimMindError>.Ok(true));
        }

        private sealed class StubMechanismTrigger : IMechanismTrigger
        {
            public Task<Result<bool, RimMindError>> ExecuteToggleAsync(MechanismWriteArgs args, CancellationToken ct) =>
                Task.FromResult(Result<bool, RimMindError>.Ok(true));

            public Task<Result<bool, RimMindError>> ExecuteTriggerAsync(MechanismWriteArgs args, CancellationToken ct) =>
                Task.FromResult(Result<bool, RimMindError>.Ok(true));

            public Task<Result<bool, RimMindError>> ExecuteWatchAsync(MechanismWriteArgs args, CancellationToken ct) =>
                Task.FromResult(Result<bool, RimMindError>.Ok(true));
        }
    }
}
