using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using RimMind.Application.Common.Constants;
using RimMind.Presentation.Api;
using RimMind.Testing;
using Xunit;

namespace RimMind.Core.ArchTests.Contracts
{
    [Trait("Phase", "N")]
    public sealed class PublicApiSurfaceContracts
    {
        private static readonly string[] CanonicalNestedGroups =
        {
            "Request",
            "Context",
            "ToolSet",
            "Ext",
            "Providers",
            "Settings",
            "Memory",
            "Agents",
            "Autonomy",
            "MainThread",
            "MainThreadSnapshot",
            "Prompt",
            "Log",
            "Bus",
            "Diagnostics",
            "Bridges",
            "Text",
            "GameEvents",
            "Thoughts"
        };

        private static readonly HashSet<string> AllowlistedInternalSurfaceMembers =
            new(StringComparer.Ordinal)
            {
                "RimMindAPI.Settings.ContextSettings",
                "RimMindAPI.Settings.get_ContextSettings",
                "RimMindAPI.Settings.ContextEngine",
                "RimMindAPI.Settings.get_ContextEngine",
                "RimMindAPI.Settings.GetContextEngine",
                "RimMindAPI.Settings.Telemetry",
                "RimMindAPI.Settings.get_Telemetry"
            };

        private static readonly string[] ExpectedObsoleteFlatAliases =
        {
            "PauseQueue",
            "ResumeQueue",
            "ActiveRequestCount",
            "GetActiveRequests",
            "GetAllQueuedRequests",
            "TotalQueuedCount",
            "ClearModCooldown",
            "GetModCooldownTicksLeft",
            "BuildMapContext",
            "ShouldSkipDialogue",
            "ShouldSkipFloatMenu",
            "ShouldSkipAction",
            "ShouldSkipStorytellerIncident",
            "TriggerDialogue",
            "NotifyIncidentExecuted",
            "CanTriggerDialogue",
            "RegisterAgentIdentityProvider",
            "GetAgentIdentity",
            "RegisterAgentActionBridge",
            "GetAgentActionBridge",
            "ParameterTuners",
            "GetProviderData",
            "GetStaticProviderData",
            "GetRegisteredCategories",
            "UnregisterModProviders",
            "IsConfigured",
            "GetContextEngine",
            "Telemetry",
            "AudioPlayer",
            "GetAgentBus",
            "PublishPerception",
            "InvalidateClientCache",
            "GetPlayer2Client"
        };

        [Fact]
        public void RimMindAPI_exposes_ApiVersion_2_and_all_canonical_nested_groups()
        {
            Type apiType = typeof(RimMindAPI);
            var nestedByName = apiType
                .GetNestedTypes(BindingFlags.Public)
                .ToDictionary(t => t.Name, t => t, StringComparer.Ordinal);

            ContractCaseRunner.Run(
                ("RimMindAPI.ApiVersion is a public const int >= 2", () =>
                {
                    Assert.True(RimMindAPI.ApiVersion >= 2);
                    FieldInfo? versionField = apiType.GetField(
                        nameof(RimMindAPI.ApiVersion),
                        BindingFlags.Public | BindingFlags.Static);
                    Assert.NotNull(versionField);
                    Assert.True(versionField!.IsLiteral && !versionField.IsInitOnly);
                    Assert.Equal(typeof(int), versionField.FieldType);
                    Assert.True((int)versionField.GetRawConstantValue()! >= 2);
                }),
                ("all canonical nested API groups exist as public static classes", () =>
                {
                    var missing = CanonicalNestedGroups
                        .Where(group => !nestedByName.TryGetValue(group, out Type? nested)
                            || !(nested.IsAbstract && nested.IsSealed))
                        .ToArray();
                    Assert.True(
                        missing.Length == 0,
                        $"Missing or non-static canonical RimMindAPI nested groups: {string.Join(", ", missing)}");
                }),
                ("deprecated flat aliases on RimMindAPI carry [Obsolete]", () =>
                {
                    var missingObsolete = new List<string>();
                    foreach (string memberName in ExpectedObsoleteFlatAliases)
                    {
                        MemberInfo[] members = apiType.GetMember(
                            memberName,
                            BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
                        if (members.Length == 0
                            || members.Any(m => m.GetCustomAttribute<ObsoleteAttribute>() == null))
                        {
                            missingObsolete.Add(memberName);
                        }
                    }

                    Assert.True(
                        missingObsolete.Count == 0,
                        $"Flat RimMindAPI aliases missing [Obsolete]: {string.Join(", ", missingObsolete)}");
                }));
        }

        [Fact]
        public void Nested_RimMindAPI_groups_do_not_expose_internal_interfaces_outside_allowlist()
        {
            string sourceRoot = RequireSourceDirectory();
            string internalInterfacesDir = Path.Combine(
                sourceRoot,
                "Application",
                "Common",
                "Interfaces",
                "Internal");

            var internalTypeNames = SourceFiles(internalInterfacesDir)
                .Select(Path.GetFileNameWithoutExtension)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Cast<string>()
                .ToHashSet(StringComparer.Ordinal);

            Assert.NotEmpty(internalTypeNames);
            Assert.Contains("IContextSettings", internalTypeNames);

            ContractCaseRunner.Run(
                ("reflection check over nested RimMindAPI public members finds no unallowlisted Internal types", () =>
                {
                    var violations = new List<string>();

                    foreach (Type nested in typeof(RimMindAPI).GetNestedTypes(BindingFlags.Public))
                    {
                        foreach (PropertyInfo prop in nested.GetProperties(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                        {
                            string memberId = $"RimMindAPI.{nested.Name}.{prop.Name}";
                            if (AllowlistedInternalSurfaceMembers.Contains(memberId))
                                continue;

                            try
                            {
                                foreach (Type referenced in ExpandTypes(prop.PropertyType))
                                {
                                    if (IsInternalInterfaceType(referenced, internalTypeNames))
                                    {
                                        violations.Add($"{memberId} -> {referenced.FullName}");
                                    }
                                }
                            }
                            catch (FileNotFoundException)
                            {
                                // External Verse/RimWorld reference assembly not loaded at runtime; covered by source signature scan below.
                            }
                        }

                        foreach (MethodInfo method in nested.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                        {
                            if (method.IsSpecialName)
                                continue;

                            string memberId = $"RimMindAPI.{nested.Name}.{method.Name}";
                            if (AllowlistedInternalSurfaceMembers.Contains(memberId))
                                continue;

                            try
                            {
                                foreach (Type referenced in ExpandTypes(method.ReturnType))
                                {
                                    if (IsInternalInterfaceType(referenced, internalTypeNames))
                                    {
                                        violations.Add($"{memberId} (return) -> {referenced.FullName}");
                                    }
                                }

                                foreach (ParameterInfo param in method.GetParameters())
                                {
                                    foreach (Type referenced in ExpandTypes(param.ParameterType))
                                    {
                                        if (IsInternalInterfaceType(referenced, internalTypeNames))
                                        {
                                            violations.Add($"{memberId} ({param.Name}) -> {referenced.FullName}");
                                        }
                                    }
                                }
                            }
                            catch (FileNotFoundException)
                            {
                                // External Verse/RimWorld reference assembly not loaded at runtime; covered by source signature scan below.
                            }
                        }
                    }

                    Assert.True(
                        violations.Count == 0,
                        $"Nested RimMindAPI public members must not expose RimMind.Application.Common.Interfaces.Internal types:{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");
                }),
                ("source signature scan over Presentation/Api/RimMindAPI.*.cs finds no unallowlisted Internal types on public members", () =>
                {
                    string apiDir = Path.Combine(sourceRoot, "Presentation", "Api");
                    var publicSignatureViolations = new List<string>();
                    var internalTypePattern = new Regex(
                        $@"\b(?:{string.Join("|", internalTypeNames.Select(Regex.Escape))})\b",
                        RegexOptions.CultureInvariant);

                    foreach (string file in Directory.EnumerateFiles(apiDir, "RimMindAPI.*.cs", SearchOption.TopDirectoryOnly))
                    {
                        string groupName = Path.GetFileNameWithoutExtension(file).Substring("RimMindAPI.".Length);
                        string[] lines = File.ReadAllLines(file);
                        for (int i = 0; i < lines.Length; i++)
                        {
                            string trimmed = lines[i].Trim();
                            if (!trimmed.StartsWith("public static ", StringComparison.Ordinal)
                                || trimmed.StartsWith("public static class ", StringComparison.Ordinal)
                                || trimmed.StartsWith("public static partial class ", StringComparison.Ordinal))
                            {
                                continue;
                            }

                            Match match = internalTypePattern.Match(trimmed);
                            if (!match.Success)
                                continue;

                            bool isAllowlisted = AllowlistedInternalSurfaceMembers.Any(allowed =>
                            {
                                string shortMember = allowed.Split('.').Last();
                                return trimmed.Contains($" {shortMember}", StringComparison.Ordinal);
                            });

                            if (!isAllowlisted)
                            {
                                publicSignatureViolations.Add($"{Path.GetFileName(file)}:{i + 1} ({groupName}): {trimmed}");
                            }
                        }
                    }

                    Assert.True(
                        publicSignatureViolations.Count == 0,
                        $"Public nested RimMindAPI declarations must not leak Internal interfaces:{Environment.NewLine}{string.Join(Environment.NewLine, publicSignatureViolations)}");
                }));
        }

        [Fact]
        public void Domain_and_Application_do_not_contain_hardcoded_provider_literals()
        {
            string sourceRoot = RequireSourceDirectory();
            string[] layers = { "Domain", "Application" };
            string[] forbiddenLiterals = { "\"opencode\"", "\"oc_sk_\"", "\"deepseek.com\"" };
            HashSet<string> allowedFiles = new(StringComparer.OrdinalIgnoreCase)
            {
                "RimMindDefaults.cs",
                "AIProviders.cs"
            };

            ContractCaseRunner.Run(
                forbiddenLiterals
                    .Select<string, (string Name, Action Verify)>(literal => (
                        $"Domain and Application do not contain provider literal {literal} outside RimMindDefaults/AIProviders",
                        () =>
                        {
                            var violations = layers
                                .SelectMany(layer => SourceFiles(Path.Combine(sourceRoot, layer)))
                                .Where(file => !allowedFiles.Contains(Path.GetFileName(file)))
                                .Where(file => StripComments(File.ReadAllText(file)).Contains(literal, StringComparison.OrdinalIgnoreCase))
                                .Select(file => Path.GetRelativePath(sourceRoot, file))
                                .OrderBy(path => path, StringComparer.Ordinal)
                                .ToArray();

                            Assert.True(
                                violations.Length == 0,
                                $"Forbidden hardcoded provider literal {literal} in Domain/Application:{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");
                        }))
                    .ToArray());
        }

        [Fact]
        public void Owner_ids_and_Harmony_patches_obey_building_block_architecture_rules()
        {
            string repoRoot = RepositoryRoot();
            string sourceRoot = RequireSourceDirectory();

            ContractCaseRunner.Run(
                ("RimMindOwnerConsts defines all 9 canonical module owner IDs as distinct non-empty constants", () =>
                {
                    string[] ids =
                    {
                        RimMindOwnerConsts.CoreModId,
                        RimMindOwnerConsts.ActionsModId,
                        RimMindOwnerConsts.AdvisorModId,
                        RimMindOwnerConsts.DialogueModId,
                        RimMindOwnerConsts.MemoryModId,
                        RimMindOwnerConsts.PersonalityModId,
                        RimMindOwnerConsts.StorytellerModId,
                        RimMindOwnerConsts.BridgeRimTalkModId,
                        RimMindOwnerConsts.BridgeRimChatModId
                    };

                    Assert.All(ids, id => Assert.False(string.IsNullOrWhiteSpace(id)));
                    Assert.Equal(ids.Length, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());
                }),
                ("no module assigns an empty string literal to OwnerModId", () =>
                {
                    var emptyOwnerPattern = new Regex(
                        @"OwnerModId\s*(?:=>|=)\s*""""",
                        RegexOptions.CultureInvariant);

                    var sourceDirs = Directory.EnumerateDirectories(repoRoot, "RimMind-*", SearchOption.TopDirectoryOnly)
                        .Select(dir => Path.Combine(dir, "Source"))
                        .Append(sourceRoot)
                        .Where(Directory.Exists)
                        .Select(Path.GetFullPath)
                        .Distinct(StringComparer.OrdinalIgnoreCase);

                    var violations = sourceDirs
                        .SelectMany(SourceFiles)
                        .Where(file => emptyOwnerPattern.IsMatch(StripComments(File.ReadAllText(file))))
                        .Select(file => Path.GetRelativePath(repoRoot, file))
                        .OrderBy(path => path, StringComparer.Ordinal)
                        .ToArray();

                    Assert.True(
                        violations.Length == 0,
                        $"OwnerModId must never be assigned an empty string literal:{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");
                }),
                ("Harmony patches obey building-block target rules and root Directory.Build.props is standardized", () =>
                {
                    string personalityPatchPath = Path.Combine(
                        repoRoot,
                        "RimMind-Personality",
                        "Source",
                        "Patches",
                        "Patch_PersonalityIncident.cs");
                    if (File.Exists(personalityPatchPath))
                    {
                        string personalityIncidentPatch = StripComments(File.ReadAllText(personalityPatchPath));
                        Assert.Contains("IncidentWorker.TryExecute", personalityIncidentPatch, StringComparison.Ordinal);
                        Assert.DoesNotContain("TryExecuteWorker", personalityIncidentPatch, StringComparison.Ordinal);
                    }

                    string[] nonPatchSubmods =
                    {
                        "RimMind-Actions",
                        "RimMind-Bridge-RimChat",
                        "RimMind-Bridge-RimTalk",
                        "RimMind-Extension-ModelService"
                    };

                    var forbiddenGameEventPatchPattern = new Regex(
                        @"\[HarmonyPatch\s*\(\s*typeof\s*\(\s*(?:Pawn_HealthTracker|SkillRecord|Pawn|HediffSet|MemoryThoughtHandler)\s*\)",
                        RegexOptions.CultureInvariant);

                    var patchViolations = nonPatchSubmods
                        .Select(mod => Path.Combine(repoRoot, mod, "Source"))
                        .Where(Directory.Exists)
                        .SelectMany(SourceFiles)
                        .Where(file => forbiddenGameEventPatchPattern.IsMatch(StripComments(File.ReadAllText(file))))
                        .Select(file => Path.GetRelativePath(repoRoot, file))
                        .OrderBy(path => path, StringComparer.Ordinal)
                        .ToArray();

                    Assert.True(
                        patchViolations.Length == 0,
                        $"Non-event submodules must not patch colonist health/skill/death directly:{Environment.NewLine}{string.Join(Environment.NewLine, patchViolations)}");

                    string propsPath = Path.Combine(repoRoot, "Directory.Build.props");
                    if (File.Exists(propsPath))
                    {
                        XDocument doc = XDocument.Load(propsPath);
                        Assert.Equal("10.0", doc.Descendants("LangVersion").FirstOrDefault()?.Value);
                        Assert.Equal("enable", doc.Descendants("Nullable").FirstOrDefault()?.Value);
                        Assert.Equal("true", doc.Descendants("Deterministic").FirstOrDefault()?.Value);
                    }
                }));
        }

        private static IEnumerable<Type> ExpandTypes(Type type)
        {
            if (type.IsByRef)
            {
                type = type.GetElementType()!;
            }

            yield return type;

            if (type.IsGenericType)
            {
                foreach (Type arg in type.GetGenericArguments())
                {
                    foreach (Type nested in ExpandTypes(arg))
                    {
                        yield return nested;
                    }
                }
            }
        }

        private static bool IsInternalInterfaceType(Type type, HashSet<string> internalTypeNames)
        {
            string ns = type.Namespace ?? string.Empty;
            if (ns.Equals("RimMind.Application.Common.Interfaces.Internal", StringComparison.Ordinal)
                || ns.StartsWith("RimMind.Application.Common.Interfaces.Internal.", StringComparison.Ordinal))
            {
                return true;
            }

            return internalTypeNames.Contains(type.Name);
        }

        private static string StripComments(string source)
        {
            return Regex.Replace(
                source,
                @"//[^\r\n]*|/\*.*?\*/",
                " ",
                RegexOptions.Singleline | RegexOptions.CultureInvariant);
        }

        private static IEnumerable<string> SourceFiles(string directory)
        {
            Assert.True(Directory.Exists(directory), $"Source directory must exist: {directory}");
            return Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
                .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(part =>
                    part.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
                    part.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
                    part.Equals("backup", StringComparison.OrdinalIgnoreCase)));
        }

        private static string RequireSourceDirectory()
        {
            string source = ArchTestExtensions.FindSourceDirectory();
            Assert.True(Directory.Exists(source), "RimMind-Core/Source must be discoverable.");
            return source;
        }

        private static string RepositoryRoot()
        {
            string source = RequireSourceDirectory();
            DirectoryInfo? core = Directory.GetParent(source);
            Assert.NotNull(core);
            DirectoryInfo? root = core!.Parent;
            Assert.NotNull(root);
            return root!.FullName;
        }
    }
}
