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
                ("retired [Obsolete] flat aliases have been hard-cut from RimMindAPI.cs", () =>
                {
                    int totalObsoleteCount = Regex.Matches(apiSource, @"\[Obsolete\(").Count;
                    Assert.Equal(0, totalObsoleteCount);

                    string[] retiredMembers =
                    {
                        "PauseQueue(",
                        "ResumeQueue(",
                        "ActiveRequestCount =>",
                        "TotalQueuedCount =>",
                        "ShouldSkipDialogue(",
                        "TriggerDialogue(",
                        "GetProviderData(",
                        "GetAgentBus(",
                        "GetPlayer2Client(",
                        "InvalidateClientCache("
                    };

                    foreach (string retired in retiredMembers)
                    {
                        Assert.DoesNotContain(retired, apiSource, StringComparison.Ordinal);
                    }
                }));
        }

        [Fact]
        public void DirectoryBuildProps_standardizes_compiler_defaults_without_overriding_projects()
        {
            string repoRoot = FindRepositoryRoot();
            string propsPath = Path.Combine(repoRoot, "Directory.Build.props");
            if (!File.Exists(propsPath))
            {
                propsPath = Path.Combine(repoRoot, "RimMind-Core", "Directory.Build.props");
            }

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
