using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using RimMind.Testing;
using Xunit;

namespace RimMind.Tests.Contracts
{
    public sealed class P6ApiVersionContracts
    {
        [Fact]
        public void RimMindAPI_v2_version_constant_and_flat_alias_deprecations_are_locked()
        {
            string repoRoot = FindRepositoryRoot();
            string apiSource = File.ReadAllText(Path.Combine(repoRoot, "RimMind-Core", "Source", "RimMindAPI.cs"));

            ContractCaseRunner.Run(
                ("RimMindAPI declares public const int ApiVersion = 2", () =>
                {
                    Assert.Contains("public const int ApiVersion = 2;", apiSource, StringComparison.Ordinal);
                }),
                ("Modes and ModePolicies route directly through Ext.Get<T>()", () =>
                {
                    Assert.Contains("public static IExtensionRegistry<IAgentMode> Modes => Ext.Get<IAgentMode>();", apiSource, StringComparison.Ordinal);
                    Assert.Contains("public static IExtensionRegistry<IModeTransitionPolicy> ModePolicies => Ext.Get<IModeTransitionPolicy>();", apiSource, StringComparison.Ordinal);
                }),
                ("flat forwarding aliases guide callers to nested groups via [Obsolete]", () =>
                {
                    string[] expectedGuidance =
                    {
                        "[Obsolete(\"Use RimMindAPI.Request.PauseQueue instead.\")]",
                        "[Obsolete(\"Use RimMindAPI.Request.ResumeQueue instead.\")]",
                        "[Obsolete(\"Use RimMindAPI.Request.ActiveRequestCount instead.\")]",
                        "[Obsolete(\"Use RimMindAPI.Request.TotalQueuedCount instead.\")]",
                        "[Obsolete(\"Use RimMindAPI.Request.ClearModCooldown instead.\")]",
                        "[Obsolete(\"Use RimMindAPI.Request.GetModCooldownTicksLeft instead.\")]",
                        "[Obsolete(\"Use RimMindAPI.Ext.ShouldSkipDialogue instead.\")]",
                        "[Obsolete(\"Use RimMindAPI.Ext.ShouldSkipFloatMenu instead.\")]",
                        "[Obsolete(\"Use RimMindAPI.Ext.ShouldSkipAction instead.\")]",
                        "[Obsolete(\"Use RimMindAPI.Ext.ShouldSkipStorytellerIncident instead.\")]",
                        "[Obsolete(\"Use RimMindAPI.Ext.TriggerDialogue instead.\")]",
                        "[Obsolete(\"Use RimMindAPI.Ext.NotifyIncidentExecuted instead.\")]",
                        "[Obsolete(\"Use RimMindAPI.Ext.CanTriggerDialogue instead.\")]",
                        "[Obsolete(\"Use RimMindAPI.Ext.RegisterAgentIdentityProvider instead.\")]",
                        "[Obsolete(\"Use RimMindAPI.Ext.RegisterAgentActionBridge instead.\")]",
                        "[Obsolete(\"Use RimMindAPI.Providers.GetProviderData instead.\")]",
                        "[Obsolete(\"Use RimMindAPI.Providers.GetStaticProviderData instead.\")]",
                        "[Obsolete(\"Use RimMindAPI.Providers.GetRegisteredCategories instead.\")]",
                        "[Obsolete(\"Use RimMindAPI.Providers.UnregisterByOwner instead.\")]",
                        "[Obsolete(\"Use RimMindAPI.Settings.IsConfigured instead.\")]",
                        "[Obsolete(\"Use RimMindAPI.Settings.GetContextEngine instead.\")]",
                        "[Obsolete(\"Use RimMindAPI.Bus.PublishPerception instead.\")]",
                        "[Obsolete(\"Use RimMindAPI.Bus.InvalidateClientCache instead.\")]"
                    };

                    foreach (string attr in expectedGuidance)
                    {
                        Assert.Contains(attr, apiSource, StringComparison.Ordinal);
                    }

                    int totalObsoleteCount = Regex.Matches(apiSource, @"\[Obsolete\(").Count;
                    Assert.True(totalObsoleteCount >= 34, $"Expected at least 34 [Obsolete] attributes on RimMindAPI.cs, found {totalObsoleteCount}.");
                }));
        }

        [Fact]
        public void DirectoryBuildProps_standardizes_compiler_defaults_without_overriding_projects()
        {
            string repoRoot = FindRepositoryRoot();
            string propsPath = Path.Combine(repoRoot, "Directory.Build.props");

            ContractCaseRunner.Run(
                ("Directory.Build.props exists at repository root and sets conditional C# 10, Nullable, and Deterministic defaults", () =>
                {
                    Assert.True(File.Exists(propsPath), "Directory.Build.props must exist at the repository root.");
                    XDocument doc = XDocument.Load(propsPath);

                    XElement? langVersion = doc.Descendants("LangVersion").FirstOrDefault();
                    Assert.NotNull(langVersion);
                    Assert.Equal("10.0", langVersion!.Value);
                    Assert.Equal("'$(LangVersion)' == ''", langVersion.Attribute("Condition")?.Value);

                    XElement? nullable = doc.Descendants("Nullable").FirstOrDefault();
                    Assert.NotNull(nullable);
                    Assert.Equal("enable", nullable!.Value);
                    Assert.Equal("'$(Nullable)' == ''", nullable.Attribute("Condition")?.Value);

                    XElement? deterministic = doc.Descendants("Deterministic").FirstOrDefault();
                    Assert.NotNull(deterministic);
                    Assert.Equal("true", deterministic!.Value);
                }));
        }

        private static string FindRepositoryRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "RimMind-Core", "Source")))
            {
                directory = directory.Parent;
            }

            return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
        }
    }
}
