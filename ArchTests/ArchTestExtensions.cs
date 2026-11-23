using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace RimMind.Core.ArchTests
{
    public static class ArchTestExtensions
    {
        public static string FindSourceDirectory()
        {
            var dir = Path.GetDirectoryName(typeof(ArchTestExtensions).Assembly.Location);
            while (dir != null)
            {
                var candidate = Path.Combine(dir, "RimMind-Core", "Source");
                if (Directory.Exists(candidate)) return candidate;

                candidate = Path.Combine(dir, "Source");
                if (Directory.Exists(candidate)) return candidate;

                dir = Directory.GetParent(dir)?.FullName;
            }
            return "";
        }

        public static string RequireSourceDirectory()
        {
            string source = FindSourceDirectory();
            Assert.True(Directory.Exists(source), "RimMind-Core/Source must be discoverable.");
            return source;
        }

        public static string RepositoryRoot()
        {
            string source = RequireSourceDirectory();
            DirectoryInfo? core = Directory.GetParent(source);
            Assert.NotNull(core);
            DirectoryInfo? root = core!.Parent;
            Assert.NotNull(root);
            return root!.FullName;
        }

        public static IEnumerable<string> SourceFiles(string directory)
        {
            Assert.True(Directory.Exists(directory), $"Source directory must exist: {directory}");
            return Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
                .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(part =>
                    part.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
                    part.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
                    part.Equals("backup", StringComparison.OrdinalIgnoreCase)));
        }

        public static string StripComments(string source)
        {
            return Regex.Replace(
                source,
                @"//[^\r\n]*|/\*.*?\*/",
                " ",
                RegexOptions.Singleline | RegexOptions.CultureInvariant);
        }

        public static string StripCommentsAndStrings(string source)
        {
            return Regex.Replace(
                source,
                @"@""(?:""""|[^""])*""|""(?:\\.|[^""\\])*""|'(?:\\.|[^'\\])'|//[^\r\n]*|/\*.*?\*/",
                " ",
                RegexOptions.Singleline | RegexOptions.CultureInvariant);
        }

        public static string ExtractBraceBalancedBlock(string code, int startIndex)
        {
            int openBrace = code.IndexOf('{', startIndex);
            Assert.True(openBrace >= startIndex, "Method opening brace must be discoverable.");
            int depth = 0;
            for (int i = openBrace; i < code.Length; i++)
            {
                if (code[i] == '{')
                {
                    depth++;
                }
                else if (code[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        return code.Substring(startIndex, i - startIndex + 1);
                    }
                }
            }

            Assert.Fail("Unbalanced braces when extracting method block.");
            return string.Empty;
        }

        public static CsprojAnalysis AnalyzeCsproj(string csprojPath)
        {
            if (!File.Exists(csprojPath))
                throw new FileNotFoundException($"csproj not found: {csprojPath}");

            var doc = XDocument.Load(csprojPath);
            var ns = doc.Root?.Name.Namespace ?? XNamespace.None;

            var packageRefs = doc.Descendants(ns + "PackageReference")
                .Select(el => new PackageRef
                {
                    Include = el.Attribute("Include")?.Value ?? "",
                    Version = el.Attribute("Version")?.Value ?? el.Element(ns + "Version")?.Value ?? ""
                })
                .ToList();

            var projectRefs = doc.Descendants(ns + "ProjectReference")
                .Select(el => el.Attribute("Include")?.Value ?? "")
                .ToList();

            var assemblyName = doc.Descendants(ns + "AssemblyName")
                .Select(el => el.Value)
                .FirstOrDefault() ?? Path.GetFileNameWithoutExtension(csprojPath);

            var targetFramework = doc.Descendants(ns + "TargetFramework")
                .Concat(doc.Descendants(ns + "TargetFrameworks"))
                .Select(el => el.Value)
                .FirstOrDefault() ?? "";

            return new CsprojAnalysis
            {
                Path = csprojPath,
                AssemblyName = assemblyName,
                TargetFramework = targetFramework,
                PackageReferences = packageRefs,
                ProjectReferences = projectRefs
            };
        }
    }

    public class CsprojAnalysis
    {
        public string Path { get; set; } = "";
        public string AssemblyName { get; set; } = "";
        public string TargetFramework { get; set; } = "";
        public List<PackageRef> PackageReferences { get; set; } = new();
        public List<string> ProjectReferences { get; set; } = new();

        public bool HasPackageRef(string name) =>
            PackageReferences.Any(pr => string.Equals(pr.Include, name, StringComparison.OrdinalIgnoreCase));

        public bool HasProjectRef(string name) =>
            ProjectReferences.Any(pr => pr.Contains(name, StringComparison.OrdinalIgnoreCase));
    }

    public class PackageRef
    {
        public string Include { get; set; } = "";
        public string Version { get; set; } = "";
    }
}
