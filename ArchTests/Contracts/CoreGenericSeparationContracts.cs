using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using RimMind.Testing;
using Xunit;

namespace RimMind.Core.ArchTests.Contracts
{
    /// <summary>
    /// Architecture guardrails locking in generic-vs-specific separation inside RimMind-Core:
    /// no vendor hardcoding outside concrete client adapters, no submodule prompt/tool literals
    /// in generic Core infrastructure, and declarative mechanism/provider extension seams.
    /// </summary>
    public sealed class CoreGenericSeparationContracts
    {
        [Fact]
        public void Core_source_maintains_generic_vs_specific_separation_invariants()
        {
            string sourceRoot = ArchTestExtensions.FindSourceDirectory();
            Assert.True(Directory.Exists(sourceRoot), "RimMind-Core/Source must be discoverable.");

            ContractCaseRunner.Run(
                ("no vendor literals outside concrete client adapters and dev harnesses", () =>
                {
                    string[] forbiddenVendors =
                    {
                        "\"opencode\"",
                        "\"oc_sk_\"",
                        "opencode.ai",
                        "deepseek.com",
                        "deepseek-flash",
                        "deepseek-v4",
                        "siliconflow.cn",
                        "moonshot.cn",
                        "grok-"
                    };

                    var violations = new List<string>();
                    foreach (string file in EnumerateCoreSourceFiles(sourceRoot))
                    {
                        string rel = Path.GetRelativePath(sourceRoot, file).Replace('\\', '/');
                        if (IsAllowedVendorAdapterPath(rel))
                            continue;

                        string code = StripComments(File.ReadAllText(file));
                        foreach (string literal in forbiddenVendors)
                        {
                            if (code.IndexOf(literal, StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                violations.Add($"{rel} contains forbidden vendor literal '{literal}'");
                            }
                        }
                    }

                    Assert.True(
                        violations.Count == 0,
                        $"Generic Core code must not hardcode vendor literals:{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");
                }),
                ("HttpTransport has zero vendor sniffing and delegates to IHttpRequestDecorator", () =>
                {
                    string transportPath = Path.Combine(sourceRoot, "Infrastructure", "Services", "Clients", "HttpTransport.cs");
                    string code = StripComments(File.ReadAllText(transportPath));
                    Assert.DoesNotContain("opencode", code, StringComparison.OrdinalIgnoreCase);
                    Assert.DoesNotContain("oc_sk_", code, StringComparison.OrdinalIgnoreCase);
                    Assert.Contains("IHttpRequestDecorator", code, StringComparison.Ordinal);
                }),
                ("CoreContextProviders contains zero Storyteller prompt literals", () =>
                {
                    string providersPath = Path.Combine(sourceRoot, "Presentation", "Context", "CoreContextProviders.cs");
                    string code = StripComments(File.ReadAllText(providersPath));
                    Assert.DoesNotContain("You are the RimWorld storyteller AI", code, StringComparison.OrdinalIgnoreCase);
                    Assert.DoesNotContain("Storyteller rules:", code, StringComparison.OrdinalIgnoreCase);
                    Assert.DoesNotContain("IncidentOutput schema", code, StringComparison.OrdinalIgnoreCase);
                }),
                ("MechanismToolHandler uses declarative IMechanismAutonomyMetadata instead of hardcoding pawn.work", () =>
                {
                    string handlerPath = Path.Combine(sourceRoot, "Infrastructure", "Mechanisms", "MechanismToolHandler.cs");
                    string code = StripComments(File.ReadAllText(handlerPath));
                    Assert.DoesNotContain("\"pawn.work\"", code, StringComparison.OrdinalIgnoreCase);
                    Assert.Contains("IMechanismAutonomyMetadata", code, StringComparison.Ordinal);
                }),
                ("Core lookup and map context builders contain zero hardcoded Actions tool prefixes", () =>
                {
                    string[] filesToCheck =
                    {
                        Path.Combine(sourceRoot, "Presentation", "Common", "RimMindPawnLookup.cs"),
                        Path.Combine(sourceRoot, "Presentation", "Agent", "MapContextBuilder.cs"),
                        Path.Combine(sourceRoot, "Presentation", "Context", "CoreContextProviders.cs")
                    };

                    var violations = new List<string>();
                    foreach (string path in filesToCheck)
                    {
                        string code = StripComments(File.ReadAllText(path));
                        if (code.IndexOf("actions_", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            violations.Add(Path.GetRelativePath(sourceRoot, path));
                        }
                    }

                    Assert.True(
                        violations.Count == 0,
                        $"Core must not hardcode RimMind-Actions tool IDs or prefixes:{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");
                }),
                ("RimMindRuntimeGameComponent.NotifyGameStopped uses pure IGameScopedService dispatch without Player2 hardcoding", () =>
                {
                    string compPath = Path.Combine(sourceRoot, "Presentation", "Runtime", "RimMindRuntimeGameComponent.cs");
                    string code = StripComments(File.ReadAllText(compPath));
                    int start = code.IndexOf("internal static void NotifyGameStopped", StringComparison.Ordinal);
                    Assert.True(start >= 0, "NotifyGameStopped method must exist");
                    int nextMethod = code.IndexOf("private static IExtensionRegistry<IGameScopedService>", start, StringComparison.Ordinal);
                    Assert.True(nextMethod > start, "NotifyGameStopped method boundary must be discoverable");
                    string methodBody = code.Substring(start, nextMethod - start);
                    Assert.DoesNotContain("Player2GameLifecycleService", methodBody, StringComparison.Ordinal);
                }));
        }

        private static bool IsAllowedVendorAdapterPath(string relativeSlashPath)
        {
            return relativeSlashPath.StartsWith("Infrastructure/Services/Clients/OpenAI/", StringComparison.OrdinalIgnoreCase)
                || relativeSlashPath.StartsWith("Infrastructure/Services/Clients/Player2/", StringComparison.OrdinalIgnoreCase)
                || relativeSlashPath.StartsWith("Infrastructure/UI/DebugActions/", StringComparison.OrdinalIgnoreCase);
        }

        private static IEnumerable<string> EnumerateCoreSourceFiles(string sourceRoot)
        {
            return Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
                .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(part =>
                    part.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
                    part.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
                    part.Equals("backup", StringComparison.OrdinalIgnoreCase)));
        }

        private static string StripComments(string source)
        {
            return Regex.Replace(
                source,
                @"//[^\r\n]*|/\*.*?\*/",
                " ",
                RegexOptions.Singleline | RegexOptions.CultureInvariant);
        }
    }
}
