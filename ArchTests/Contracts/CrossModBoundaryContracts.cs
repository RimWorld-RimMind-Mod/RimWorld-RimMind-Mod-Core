using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using RimMind.Testing;
using Xunit;

namespace RimMind.Core.ArchTests.Contracts
{
    public sealed class CrossModBoundaryContracts
    {
        [Fact]
        public void Dependent_mods_use_capabilities_instead_of_runtime_hub_access()
        {
            ContractCaseRunner.Run(
                ("runtime singleton is not consumed", () => AssertDependentSourceAbsent(@"RimMindRuntime\s*\.\s*Instance")),
                ("legacy locator is not consumed", () => AssertDependentSourceAbsent(@"\bRimMindServiceLocator\b")),
                ("generic service lookup is not consumed", () => AssertDependentSourceAbsent(@"\bGetService\s*<")),
                ("generic try lookup is not consumed", () => AssertDependentSourceAbsent(@"\bTryGetService\s*<")),
                ("runtime service hub is not consumed", () => AssertDependentSourceAbsent(@"\bRuntimeServiceHub\b")),
                ("runtime snapshots are not consumed", () => AssertDependentSourceAbsent(@"\bRuntimeServiceSnapshot\b")));
        }

        [Fact]
        public void Feature_mod_dependencies_remain_directional()
        {
            ContractCaseRunner.Run(
                ("actions does not depend on feature mods", () =>
                    AssertNoUsing("RimMind-Actions", "Advisor", "Memory", "Personality", "Dialogue", "Storyteller", "Bridge")),
                ("advisor does not depend on state-producing feature mods", () =>
                    AssertNoUsing("RimMind-Advisor", "Memory", "Personality", "Dialogue", "Storyteller", "Bridge")),
                ("memory does not depend on peer feature mods", () =>
                    AssertNoUsing("RimMind-Memory", "Actions", "Advisor", "Personality", "Dialogue", "Storyteller", "Bridge")),
                ("personality does not depend on peer feature mods", () =>
                    AssertNoUsing("RimMind-Personality", "Actions", "Advisor", "Memory", "Dialogue", "Storyteller", "Bridge")),
                ("dialogue does not depend on peer feature mods", () =>
                    AssertNoUsing("RimMind-Dialogue", "Actions", "Advisor", "Memory", "Personality", "Storyteller", "Bridge")),
                ("storyteller does not depend on peer feature mods", () =>
                    AssertNoUsing("RimMind-Storyteller", "Actions", "Advisor", "Memory", "Personality", "Dialogue", "Bridge")));
        }

        [Fact]
        public void Bridge_mods_stay_on_declared_public_seams()
        {
            ContractCaseRunner.Run(
                ("RimTalk consumes only public data seams from feature mods", () =>
                    AssertNoPattern(
                        "RimMind-Bridge-RimTalk",
                        @"^\s*using\s+RimMind\.(?:Advisor|Memory|Personality)\.(?!Data(?:\.|;))")),
                ("RimTalk does not consume dialogue or storyteller implementations", () =>
                    AssertNoUsing("RimMind-Bridge-RimTalk", "Dialogue", "Storyteller")),
                ("RimChat does not consume peer feature implementations", () =>
                    AssertNoUsing("RimMind-Bridge-RimChat", "Actions", "Advisor", "Memory", "Personality", "Dialogue", "Storyteller")),
                ("bridges do not consume Core infrastructure", () =>
                {
                    AssertNoPattern("RimMind-Bridge-RimTalk", @"^\s*using\s+RimMind\.Infrastructure(?:\.|;)");
                    AssertNoPattern("RimMind-Bridge-RimChat", @"^\s*using\s+RimMind\.Infrastructure(?:\.|;)");
                }),
                ("bridges do not consume Core runtime composition", () =>
                {
                    AssertNoPattern("RimMind-Bridge-RimTalk", @"^\s*using\s+RimMind\.Presentation\.Runtime(?:\.|;)");
                    AssertNoPattern("RimMind-Bridge-RimChat", @"^\s*using\s+RimMind\.Presentation\.Runtime(?:\.|;)");
                }));
        }

        private static void AssertDependentSourceAbsent(string forbiddenPattern)
        {
            var pattern = new Regex(forbiddenPattern, RegexOptions.CultureInvariant);
            string root = RepositoryRoot();
            IReadOnlyList<string> violations = DiscoverDependentMods()
                .SelectMany(mod => SourceFiles(Path.Combine(root, mod, "Source")))
                .Where(file => pattern.IsMatch(StripCommentsAndStrings(File.ReadAllText(file))))
                .Select(file => Path.GetRelativePath(root, file))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            Assert.True(
                violations.Count == 0,
                $"Dependent Mods must not consume '{forbiddenPattern}':{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");
        }

        private static void AssertNoUsing(string mod, params string[] forbiddenModules)
        {
            string alternatives = string.Join("|", forbiddenModules.Select(Regex.Escape));
            AssertNoPattern(mod, $@"^\s*using\s+RimMind\.(?:{alternatives})(?:\.|;)");
        }

        private static void AssertNoPattern(string mod, string forbiddenPattern)
        {
            string root = RepositoryRoot();
            var pattern = new Regex(
                forbiddenPattern,
                RegexOptions.Multiline | RegexOptions.CultureInvariant);
            IReadOnlyList<string> violations = SourceFiles(Path.Combine(root, mod, "Source"))
                .Where(file => pattern.IsMatch(StripCommentsAndStrings(File.ReadAllText(file))))
                .Select(file => Path.GetRelativePath(root, file))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            Assert.True(
                violations.Count == 0,
                $"{mod} crosses a forbidden Mod boundary:{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");
        }

        private static IReadOnlyList<string> DiscoverDependentMods()
        {
            string root = RepositoryRoot();
            return Directory.EnumerateDirectories(root, "RimMind-*", SearchOption.TopDirectoryOnly)
                .Where(directory => !Path.GetFileName(directory).Equals("RimMind-Core", StringComparison.OrdinalIgnoreCase))
                .Where(directory => Directory.Exists(Path.Combine(directory, "Source")))
                .Select(Path.GetFileName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Cast<string>()
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
        }

        private static string StripCommentsAndStrings(string source)
        {
            return Regex.Replace(
                source,
                @"@""(?:""""|[^""])*""|""(?:\\.|[^""\\])*""|'(?:\\.|[^'\\])'|//[^\r\n]*|/\*.*?\*/",
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

        private static string RepositoryRoot()
        {
            string source = ArchTestExtensions.FindSourceDirectory();
            Assert.True(Directory.Exists(source), "RimMind-Core/Source must be discoverable.");
            DirectoryInfo? core = Directory.GetParent(source);
            Assert.NotNull(core);
            DirectoryInfo? root = core!.Parent;
            Assert.NotNull(root);
            return root!.FullName;
        }
    }
}
