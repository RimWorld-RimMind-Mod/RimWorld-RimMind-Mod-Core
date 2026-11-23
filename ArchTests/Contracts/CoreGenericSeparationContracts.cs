using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
            string sourceRoot = ArchTestExtensions.RequireSourceDirectory();

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
                    foreach (string file in ArchTestExtensions.SourceFiles(sourceRoot))
                    {
                        string rel = Path.GetRelativePath(sourceRoot, file).Replace('\\', '/');
                        if (IsAllowedVendorAdapterPath(rel))
                            continue;

                        string code = ArchTestExtensions.StripComments(File.ReadAllText(file));
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
                    string code = ArchTestExtensions.StripComments(File.ReadAllText(transportPath));
                    Assert.DoesNotContain("opencode", code, StringComparison.OrdinalIgnoreCase);
                    Assert.DoesNotContain("oc_sk_", code, StringComparison.OrdinalIgnoreCase);
                    Assert.Contains("IHttpRequestDecorator", code, StringComparison.Ordinal);
                }),
                ("CoreContextProviders contains zero Storyteller prompt literals", () =>
                {
                    string providersPath = Path.Combine(sourceRoot, "Presentation", "Context", "CoreContextProviders.cs");
                    string code = ArchTestExtensions.StripComments(File.ReadAllText(providersPath));
                    Assert.DoesNotContain("You are the RimWorld storyteller AI", code, StringComparison.OrdinalIgnoreCase);
                    Assert.DoesNotContain("Storyteller rules:", code, StringComparison.OrdinalIgnoreCase);
                    Assert.DoesNotContain("IncidentOutput schema", code, StringComparison.OrdinalIgnoreCase);
                }),
                ("MechanismToolHandler uses declarative IMechanismAutonomyMetadata instead of hardcoding pawn.work", () =>
                {
                    string handlerPath = Path.Combine(sourceRoot, "Infrastructure", "Mechanisms", "MechanismToolHandler.cs");
                    string code = ArchTestExtensions.StripComments(File.ReadAllText(handlerPath));
                    Assert.DoesNotContain("\"pawn.work\"", code, StringComparison.OrdinalIgnoreCase);
                    Assert.Contains("IMechanismAutonomyMetadata", code, StringComparison.Ordinal);
                }),
                ("Core production code contains zero hardcoded Actions tool prefixes outside dev debug harnesses", () =>
                {
                    var violations = new List<string>();
                    foreach (string path in ArchTestExtensions.SourceFiles(sourceRoot))
                    {
                        string rel = Path.GetRelativePath(sourceRoot, path).Replace('\\', '/');
                        if (rel.StartsWith("Infrastructure/UI/DebugActions/", StringComparison.OrdinalIgnoreCase))
                            continue;

                        string code = ArchTestExtensions.StripComments(File.ReadAllText(path));
                        if (code.IndexOf("actions_", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            violations.Add(rel);
                        }
                    }

                    Assert.True(
                        violations.Count == 0,
                        $"Core must not hardcode RimMind-Actions tool IDs or prefixes:{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");
                }),
                ("RimMindRuntimeGameComponent.NotifyGameStopped uses pure IGameScopedService dispatch without Player2 hardcoding", () =>
                {
                    string compPath = Path.Combine(sourceRoot, "Presentation", "Runtime", "RimMindRuntimeGameComponent.cs");
                    string code = ArchTestExtensions.StripComments(File.ReadAllText(compPath));
                    int start = code.IndexOf("internal static void NotifyGameStopped", StringComparison.Ordinal);
                    Assert.True(start >= 0, "NotifyGameStopped method must exist");
                    string methodBody = ArchTestExtensions.ExtractBraceBalancedBlock(code, start);
                    Assert.DoesNotContain("Player2GameLifecycleService", methodBody, StringComparison.Ordinal);
                }),
                ("Application layer contains zero Infrastructure/Verse adapter string leaks", () =>
                {
                    string appDir = Path.Combine(sourceRoot, "Application");
                    var violations = new List<string>();
                    foreach (string file in ArchTestExtensions.SourceFiles(appDir))
                    {
                        string code = ArchTestExtensions.StripComments(File.ReadAllText(file));
                        if (code.IndexOf("VerseLogSink", StringComparison.Ordinal) >= 0
                            || code.IndexOf("LongEventHandler", StringComparison.Ordinal) >= 0)
                        {
                            violations.Add(Path.GetRelativePath(sourceRoot, file));
                        }
                    }

                    Assert.True(
                        violations.Count == 0,
                        $"Application must not reference Infrastructure/Verse adapter names:{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");
                }),
                ("Application generic seams stay free of hardcoded submodule/vendor/test leaks", () =>
                {
                    string schemaCode = ArchTestExtensions.StripComments(File.ReadAllText(
                        Path.Combine(sourceRoot, "Application", "Features", "Context", "SchemaRegistry.cs")));
                    Assert.DoesNotContain("PersonalityOutput", schemaCode, StringComparison.Ordinal);
                    Assert.DoesNotContain("IncidentOutput", schemaCode, StringComparison.Ordinal);
                    Assert.DoesNotContain("DarkMemoryOutput", schemaCode, StringComparison.Ordinal);

                    string credCode = ArchTestExtensions.StripComments(File.ReadAllText(
                        Path.Combine(sourceRoot, "Application", "Common", "Interfaces", "Internal", "IApiCredentialSettings.cs")));
                    Assert.DoesNotContain("Player2RemoteUrl", credCode, StringComparison.Ordinal);
                    Assert.DoesNotContain("IsOpenAIConfigured", credCode, StringComparison.Ordinal);

                    string clientMgrCode = ArchTestExtensions.StripComments(File.ReadAllText(
                        Path.Combine(sourceRoot, "Application", "Common", "Interfaces", "Internal", "IClientManager.cs")));
                    Assert.DoesNotContain("GetPlayer2Client", clientMgrCode, StringComparison.Ordinal);

                    string codecCode = ArchTestExtensions.StripComments(File.ReadAllText(
                        Path.Combine(sourceRoot, "Application", "Features", "Llm", "OpenAIWireCodec.cs")));
                    Assert.DoesNotContain("\"RimMind.Test\"", codecCode, StringComparison.Ordinal);

                    string registryCode = ArchTestExtensions.StripComments(File.ReadAllText(
                        Path.Combine(sourceRoot, "Application", "Features", "Tools", "ToolRegistry.cs")));
                    Assert.DoesNotContain("IToolRegistry? Current", registryCode, StringComparison.Ordinal);

                    string orchestratorCode = ArchTestExtensions.StripComments(File.ReadAllText(
                        Path.Combine(sourceRoot, "Application", "Features", "Agent", "ProactiveBehaviorOrchestrator.cs")));
                    Assert.DoesNotContain("TaskScheduler.Current", orchestratorCode, StringComparison.Ordinal);

                    string redactorCode = ArchTestExtensions.StripComments(File.ReadAllText(
                        Path.Combine(sourceRoot, "Application", "Features", "Llm", "LogSecretRedactor.cs")));
                    Assert.Contains("AIza[A-Za-z0-9", redactorCode, StringComparison.Ordinal);

                    string reactiveCode = ArchTestExtensions.StripComments(File.ReadAllText(
                        Path.Combine(sourceRoot, "Application", "Features", "Agent", "Modes", "ReactiveAgentMode.cs")));
                    Assert.DoesNotContain("ExcludedToolPrefixes", reactiveCode, StringComparison.Ordinal);
                    Assert.Contains("RequiresProactiveMode", reactiveCode, StringComparison.Ordinal);

                    string taskBuilderCode = ArchTestExtensions.StripComments(File.ReadAllText(
                        Path.Combine(sourceRoot, "Application", "Features", "Prompt", "TaskInstructionBuilder.cs")));
                    Assert.DoesNotContain("StripDiacritics", taskBuilderCode, StringComparison.Ordinal);
                    Assert.Contains("CanTranslate", taskBuilderCode, StringComparison.Ordinal);
                }));
        }

        private static bool IsAllowedVendorAdapterPath(string relativeSlashPath)
        {
            return relativeSlashPath.StartsWith("Infrastructure/Services/Clients/OpenAI/", StringComparison.OrdinalIgnoreCase)
                || relativeSlashPath.StartsWith("Infrastructure/Services/Clients/Player2/", StringComparison.OrdinalIgnoreCase)
                || relativeSlashPath.StartsWith("Infrastructure/UI/DebugActions/", StringComparison.OrdinalIgnoreCase);
        }
    }
}
