using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using RimMind.Testing;
using Xunit;

namespace RimMind.Core.ArchTests.Contracts
{
    public sealed class RuntimeBoundaryContracts
    {
        [Fact]
        public void Production_code_has_no_global_service_lookup_backdoor()
        {
            ContractCaseRunner.Run(
                ("runtime singleton pulls are absent", () => AssertSourceAbsent(@"RimMindRuntime\s*\.\s*Instance")),
                ("legacy locator symbol is absent", () => AssertSourceAbsent(@"\bRimMindServiceLocator\b")),
                ("generic service lookup is absent", () => AssertSourceAbsent(@"\bGetService\s*<")),
                ("generic try lookup is absent", () => AssertSourceAbsent(@"\bTryGetService\s*<")),
                ("ambient service provider pulls are absent", () => AssertSourceAbsent(@"\bServiceProvider\s*\.\s*GetService\b")));
        }

        [Fact]
        public void Application_cannot_pull_runtime_or_game_state()
        {
            ContractCaseRunner.Run(
                ("application does not name runtime host", () => AssertLayerAbsent("Application", @"\bRimMindRuntimeHost\b")),
                ("application does not name composition root", () => AssertLayerAbsent("Application", @"\bRimMindCompositionRoot\b")),
                ("application does not pull Verse Find", () => AssertLayerAbsent("Application", @"\b(?:Verse\.)?Find\s*\.")),
                ("application does not open window stack", () => AssertLayerAbsent("Application", @"\bWindowStack\b")),
                ("application does not pull current game", () => AssertLayerAbsent("Application", @"\bCurrent\s*\.\s*Game\b")),
                ("application does not construct runtime service refs", () => AssertLayerAbsent("Application", @"\bRuntimeServiceRef\s*<")));
        }

        [Fact]
        public void Runtime_container_types_remain_in_outer_adapters()
        {
            ContractCaseRunner.Run(
                ("runtime host belongs to presentation runtime", () =>
                    AssertDeclarationUnder("RimMindRuntimeHost", Path.Combine("Presentation", "Runtime"))),
                ("composition root belongs to presentation runtime", () =>
                    AssertDeclarationUnder("RimMindCompositionRoot", Path.Combine("Presentation", "Runtime"))),
                ("service hub belongs to runtime services", () =>
                    AssertDeclarationUnder("RuntimeServiceHub", Path.Combine("Presentation", "Runtime", "Services"))),
                ("service snapshot belongs to runtime services", () =>
                    AssertDeclarationUnder("RuntimeServiceSnapshot", Path.Combine("Presentation", "Runtime", "Services"))),
                ("service scope belongs to runtime services", () =>
                    AssertDeclarationUnder("RuntimeServiceScope", Path.Combine("Presentation", "Runtime", "Services"))),
                ("completion fence remains an application port", () =>
                    AssertDeclarationUnder("ICompletionFence", Path.Combine("Application", "Common", "Interfaces"))));
        }

        private static void AssertSourceAbsent(string forbiddenPattern)
        {
            AssertAbsent(SourceFiles(RequireSourceDirectory()), RequireSourceDirectory(), forbiddenPattern);
        }

        private static void AssertLayerAbsent(string layer, string forbiddenPattern)
        {
            string source = RequireSourceDirectory();
            AssertAbsent(SourceFiles(Path.Combine(source, layer)), source, forbiddenPattern);
        }

        private static void AssertAbsent(
            IEnumerable<string> files,
            string source,
            string forbiddenPattern)
        {
            var pattern = new Regex(forbiddenPattern, RegexOptions.CultureInvariant);
            IReadOnlyList<string> violations = files
                .Where(file => pattern.IsMatch(StripCommentsAndStrings(File.ReadAllText(file))))
                .Select(file => Path.GetRelativePath(source, file))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            Assert.True(
                violations.Count == 0,
                $"Forbidden runtime dependency '{forbiddenPattern}':{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");
        }

        private static void AssertDeclarationUnder(string typeName, string expectedRelativeDirectory)
        {
            string source = RequireSourceDirectory();
            var declaration = new Regex(
                $@"\b(?:class|interface|struct|record)\s+{Regex.Escape(typeName)}\b",
                RegexOptions.CultureInvariant);
            string[] matches = SourceFiles(source)
                .Where(file => declaration.IsMatch(StripCommentsAndStrings(File.ReadAllText(file))))
                .ToArray();
            string expected = Path.GetFullPath(Path.Combine(source, expectedRelativeDirectory))
                .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

            Assert.NotEmpty(matches);
            Assert.All(matches, file => Assert.StartsWith(expected, Path.GetFullPath(file), StringComparison.OrdinalIgnoreCase));
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
