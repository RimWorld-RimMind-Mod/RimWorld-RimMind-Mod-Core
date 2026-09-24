using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using RimMind.Testing;
using Xunit;

namespace RimMind.Core.ArchTests.Contracts
{
    public sealed class LayeringContracts
    {
        [Fact]
        public void Domain_remains_independent_of_outer_layers_and_game_frameworks()
        {
            ContractCaseRunner.Run(
                ("domain does not depend on application", () => AssertNoUsing("Domain", "RimMind.Application")),
                ("domain does not depend on presentation", () => AssertNoUsing("Domain", "RimMind.Presentation")),
                ("domain does not depend on infrastructure", () => AssertNoUsing("Domain", "RimMind.Infrastructure")),
                ("domain does not depend on Verse", () => AssertNoUsing("Domain", "Verse")),
                ("domain does not depend on Harmony", () => AssertNoUsing("Domain", "HarmonyLib")),
                ("domain does not depend on Unity", () => AssertNoUsing("Domain", "UnityEngine")));
        }

        [Fact]
        public void Application_depends_on_ports_not_outer_adapters()
        {
            ContractCaseRunner.Run(
                ("application does not depend on presentation", () => AssertNoUsing("Application", "RimMind.Presentation")),
                ("application does not depend on infrastructure", () => AssertNoUsing("Application", "RimMind.Infrastructure")),
                ("application does not depend on Verse", () => AssertNoUsing("Application", "Verse")),
                ("application does not depend on Harmony", () => AssertNoUsing("Application", "HarmonyLib")),
                ("application does not depend on Unity", () => AssertNoUsing("Application", "UnityEngine")),
                ("application does not depend on RimWorld adapters", () => AssertNoUsing("Application", "RimWorld")));
        }

        [Fact]
        public void Project_references_follow_the_clean_architecture_direction()
        {
            ContractCaseRunner.Run(
                ("domain has no project dependency", () =>
                    Assert.Empty(Project("Domain", "RimMindCore.Domain.csproj").ProjectReferences)),
                ("application depends inward on domain", () =>
                    Assert.True(Project("Application", "RimMindCore.Application.csproj").HasProjectRef("Domain"))),
                ("application does not depend on outer core", () =>
                    Assert.False(Project("Application", "RimMindCore.Application.csproj").HasProjectRef("RimMindCore.csproj"))),
                ("outer core depends on application", () =>
                    Assert.True(Project(".", "RimMindCore.csproj").HasProjectRef("Application"))),
                ("game framework packages stay out of domain", () =>
                {
                    CsprojAnalysis domain = Project("Domain", "RimMindCore.Domain.csproj");
                    Assert.False(domain.HasPackageRef("Krafs.Rimworld.Ref"));
                    Assert.False(domain.HasPackageRef("Lib.Harmony.Ref"));
                }),
                ("game framework packages stay out of application", () =>
                {
                    CsprojAnalysis application = Project("Application", "RimMindCore.Application.csproj");
                    Assert.False(application.HasPackageRef("Krafs.Rimworld.Ref"));
                    Assert.False(application.HasPackageRef("Lib.Harmony.Ref"));
                }));
        }

        private static CsprojAnalysis Project(string directory, string projectName)
        {
            string source = RequireSourceDirectory();
            return ArchTestExtensions.AnalyzeCsproj(Path.GetFullPath(Path.Combine(source, directory, projectName)));
        }

        private static void AssertNoUsing(string relativeLayer, string forbiddenNamespace)
        {
            string source = RequireSourceDirectory();
            string layer = Path.Combine(source, relativeLayer);
            var pattern = new Regex(
                $@"^\s*using\s+(?:global::)?{Regex.Escape(forbiddenNamespace)}(?:\.|;)",
                RegexOptions.Multiline | RegexOptions.CultureInvariant);
            IReadOnlyList<string> violations = SourceFiles(layer)
                .Where(file => pattern.IsMatch(StripCommentsAndStrings(File.ReadAllText(file))))
                .Select(file => Path.GetRelativePath(source, file))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();

            Assert.True(
                violations.Count == 0,
                $"{relativeLayer} must not reference {forbiddenNamespace}:{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");
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
    }
}
