using System;
using System.IO;
using System.Linq;
using RimMind.Application.Common.Constants;
using RimMind.Application.Common.Helpers;
using RimMind.Application.Common.Models.UI;
using RimMind.Infrastructure.UI;
using RimMind.Infrastructure.UI.DebugCenter;
using RimMind.Presentation.UI.Framework;
using RimMind.Presentation.UI.Layout;
using RimMind.Testing;
using UnityEngine;
using Xunit;

namespace RimMind.Tests.Contracts
{
    [Collection("UI drawing")]
    public sealed class P5UiAndHarnessContracts
    {
        [Fact]
        public void DebugCenterPageRegistry_supports_custom_registration_ordering_owner_unregistration_and_reset()
        {
            ContractCaseRunner.Run(
                ("default built-in pages carry CoreModId and preserve ascending order", () =>
                {
                    DebugCenterPageRegistry.ResetToDefaults();
                    var defaults = DebugCenterPageRegistry.GetAll();

                    Assert.Equal(5, defaults.Count);
                    Assert.Equal("overview", DebugCenterPageRegistry.DefaultPageId);
                    Assert.All(defaults, d => Assert.Equal(RimMindOwnerConsts.CoreModId, d.OwnerModId));
                    Assert.Equal(
                        new[] { "overview", "agents", "ai_requests", "capabilities", "settings" },
                        defaults.Select(d => d.Id).ToArray());
                }),
                ("custom pages register in order, can be unregistered by OwnerModId, and ResetToDefaults restores built-ins", () =>
                {
                    try
                    {
                        DebugCenterPageRegistry.ResetToDefaults();

                        DebugCenterPageRegistry.Register(
                            new DebugCenterPageDescriptor(
                                "advisor_diagnostics",
                                "RimMind.Advisor.Hub.Tab",
                                25,
                                IsDefault: false,
                                OwnerModId: RimMindOwnerConsts.AdvisorModId),
                            () => new ProbeDebugCenterPageDrawer("advisor"));

                        DebugCenterPageRegistry.Register(
                            new DebugCenterPageDescriptor(
                                "storyteller_timeline",
                                "RimMind.Storyteller.Hub.Tab",
                                55,
                                IsDefault: false,
                                OwnerModId: RimMindOwnerConsts.StorytellerModId),
                            () => new ProbeDebugCenterPageDrawer("storyteller"));

                        var allAfterRegister = DebugCenterPageRegistry.GetAll();
                        Assert.Equal(7, allAfterRegister.Count);

                        int aiRequestsIdx = allAfterRegister.Select((d, i) => (d.Id, i)).First(x => x.Id == "ai_requests").i;
                        int advisorIdx = allAfterRegister.Select((d, i) => (d.Id, i)).First(x => x.Id == "advisor_diagnostics").i;
                        int capabilitiesIdx = allAfterRegister.Select((d, i) => (d.Id, i)).First(x => x.Id == "capabilities").i;
                        Assert.True(aiRequestsIdx < advisorIdx && advisorIdx < capabilitiesIdx);

                        var createdDrawer = Assert.IsType<ProbeDebugCenterPageDrawer>(DebugCenterPageRegistry.Create("advisor_diagnostics"));
                        Assert.Equal("advisor", createdDrawer.Tag);

                        int removedAdvisor = DebugCenterPageRegistry.UnregisterByOwner(RimMindOwnerConsts.AdvisorModId);
                        Assert.Equal(1, removedAdvisor);
                        Assert.Null(DebugCenterPageRegistry.Find("advisor_diagnostics"));
                        Assert.NotNull(DebugCenterPageRegistry.Find("storyteller_timeline"));
                        Assert.Equal(6, DebugCenterPageRegistry.GetAll().Count);

                        DebugCenterPageRegistry.ResetToDefaults();
                        Assert.Null(DebugCenterPageRegistry.Find("storyteller_timeline"));
                        Assert.Equal(5, DebugCenterPageRegistry.GetAll().Count);
                    }
                    finally
                    {
                        DebugCenterPageRegistry.ResetToDefaults();
                    }
                }),
                ("RimMindAPI.UI exposes DebugPages and UI registration surface", () =>
                {
                        string apiUiSource = ReadSource("Presentation/Api/RimMindAPI.UI.cs");
                    Assert.Contains("public static class DebugPages", apiUiSource, StringComparison.Ordinal);
                    Assert.Contains("DebugCenterPageRegistry.Register", apiUiSource, StringComparison.Ordinal);
                    Assert.Contains("DebugCenterPageRegistry.UnregisterByOwner", apiUiSource, StringComparison.Ordinal);
                    Assert.Contains("DebugCenterPageRegistry.ResetToDefaults", apiUiSource, StringComparison.Ordinal);
                }));
        }

        [Fact]
        public void AIProviderRegistry_resolves_self_describing_descriptor_metadata_without_hardcoded_extended_service()
        {
            ContractCaseRunner.Run(
                ("AIProviderRegistry source has no hardcoded extended_service or model_service literals", () =>
                {
                    string registrySource = ReadSource("Application/Common/Helpers/AIProviderRegistry.cs");
                    string apiDrawerSource = ReadSource("Presentation/UI/ApiTabDrawer.cs");
                    string settingsEntrySource = ReadSource("Infrastructure/UI/DebugCenter/Pages/SettingsEntryDebugCenterPageDrawer.cs");

                    Assert.DoesNotContain("extended_service", registrySource, StringComparison.OrdinalIgnoreCase);
                    Assert.DoesNotContain("model_service", registrySource, StringComparison.OrdinalIgnoreCase);
                    Assert.DoesNotContain("\"extended_service\"", apiDrawerSource, StringComparison.OrdinalIgnoreCase);
                    Assert.DoesNotContain("\"model_service\"", apiDrawerSource, StringComparison.OrdinalIgnoreCase);
                    Assert.DoesNotContain("extended_service", settingsEntrySource, StringComparison.OrdinalIgnoreCase);
                }),
                ("registered AIProviderDescriptor controls RequiresApiKey and GetSettingsTabId dynamically", () =>
                {
                    try
                    {
                        AIProviderRegistry.ResetToDefaults();

                        // Built-in defaults: openai requires key, player2 does not; unregistered provider defaults to requiring key
                        Assert.True(AIProviderRegistry.RequiresApiKey("openai", null));
                        Assert.False(AIProviderRegistry.RequiresApiKey("player2", null));
                        Assert.True(AIProviderRegistry.RequiresApiKey("extended_service", null));
                        Assert.Null(AIProviderRegistry.GetSettingsTabId("extended_service"));

                        // Registering descriptor updates RequiresApiKey and SettingsTabId without Core hardcoding
                        AIProviderRegistry.Register(new AIProviderDescriptor(
                            "extended_service",
                            RequiresApiKey: false,
                            SettingsTabId: "model_service",
                            DisplayLabelKey: "RimMind.ModelService.Provider.ExtendedService",
                            OwnerModId: "mcocdaa.RimMindModelService"));

                        Assert.False(AIProviderRegistry.RequiresApiKey("extended_service", null));
                        Assert.Equal("model_service", AIProviderRegistry.GetSettingsTabId("extended_service"));
                        Assert.Equal("RimMind.ModelService.Provider.ExtendedService", AIProviderRegistry.GetDisplayLabelKey("extended_service"));

                        int removed = AIProviderRegistry.UnregisterByOwner("mcocdaa.RimMindModelService");
                        Assert.Equal(1, removed);
                        Assert.True(AIProviderRegistry.RequiresApiKey("extended_service", null));
                        Assert.Null(AIProviderRegistry.GetSettingsTabId("extended_service"));
                    }
                    finally
                    {
                        AIProviderRegistry.ResetToDefaults();
                    }
                }),
                ("RimMindUITheme shares spacing and header metrics with RimMindUiMetrics", () =>
                {
                    Assert.Equal(RimMindUiMetrics.Padding, RimMindUITheme.Padding);
                    Assert.Equal(RimMindUiMetrics.ElementGap, RimMindUITheme.ElementGap);
                    Assert.Equal(RimMindUiMetrics.SectionGap, RimMindUITheme.SectionGap);
                    Assert.Equal(RimMindUiMetrics.HeaderHeight, RimMindUITheme.HeaderHeight);
                    Assert.Equal(RimMindUiMetrics.TabHeight, RimMindUITheme.TabHeight);
                    Assert.Equal(RimMindUiMetrics.RowHeight, RimMindUITheme.BtnHeight);
                }));
        }

        [Fact]
        public void TenDayPlaythroughRunner_does_not_fabricate_fallback_prose_or_hardcode_memory_package_id()
        {
            ContractCaseRunner.Run(
                ("TenDayPlaythroughRunner does not contain fabricated prompts, fallback generators, scripted base building, or survival cheats", () =>
                {
                    string runnerSource = ReadSource("Infrastructure/UI/DebugActions/TenDayPlaythroughRunner.cs");

                    Assert.DoesNotContain("GenerateDynamicMorningThoughtFallback", runnerSource, StringComparison.Ordinal);
                    Assert.DoesNotContain("GenerateDynamicSocialDialogueFallback", runnerSource, StringComparison.Ordinal);
                    Assert.DoesNotContain("PickNonRepeatingFallback", runnerSource, StringComparison.Ordinal);
                    Assert.DoesNotContain("seasonalMorningThoughts", runnerSource, StringComparison.Ordinal);
                    Assert.DoesNotContain("ExecuteMorningThought", runnerSource, StringComparison.Ordinal);
                    Assert.DoesNotContain("ExecuteSocialDialogue", runnerSource, StringComparison.Ordinal);
                    Assert.DoesNotContain("ExecuteAutonomousAgentAction", runnerSource, StringComparison.Ordinal);
                    Assert.DoesNotContain("ExecuteAdvisorProposal", runnerSource, StringComparison.Ordinal);
                    Assert.DoesNotContain("ExecuteDailyColonyOperation", runnerSource, StringComparison.Ordinal);
                    Assert.DoesNotContain("EnsureColonistSustenance", runnerSource, StringComparison.Ordinal);
                }),
                ("TenDayPlaythroughRunner does not hardcode mcocdaa.RimMindMemory package check", () =>
                {
                    string runnerSource = ReadSource("Infrastructure/UI/DebugActions/TenDayPlaythroughRunner.cs");

                    Assert.DoesNotContain("mcocdaa.RimMindMemory", runnerSource, StringComparison.OrdinalIgnoreCase);
                }));
        }

        private sealed class ProbeDebugCenterPageDrawer : IDebugCenterPageDrawer
        {
            public ProbeDebugCenterPageDrawer(string tag)
            {
                Tag = tag;
            }

            public string Tag { get; }

            public void Draw(Rect rect, DebugCenterPageContext context, RimMindLayoutScope scope)
            {
            }
        }

        private static string ReadSource(string relativePath) =>
            File.ReadAllText(Path.Combine(CoreRoot(), "Source", relativePath.Replace('/', Path.DirectorySeparatorChar)));

        private static string CoreRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "RimMind-Core", "Source")))
                directory = directory.Parent;
            return Path.Combine(directory?.FullName ?? throw new InvalidOperationException("Repository root not found."), "RimMind-Core");
        }
    }
}
