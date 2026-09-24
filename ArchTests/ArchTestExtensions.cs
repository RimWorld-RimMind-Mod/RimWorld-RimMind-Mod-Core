using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Xml.Linq;
using NetArchTest.Rules;

namespace RimMind.Core.ArchTests
{
    public static class ArchTestExtensions
    {
        public static string FormatFailingTypes(this TestResult result)
        {
            if (result.IsSuccessful)
                return string.Empty;

            var names = result.FailingTypes?
                .Select(t => t.FullName ?? t.Name)
                .OrderBy(n => n)
                ?? Enumerable.Empty<string>();

            return string.Join("\n  ", names);
        }

        public static string FormatFailingTypes(this IEnumerable<Type> types)
        {
            var names = types
                .Select(t => t.FullName ?? t.Name)
                .OrderBy(n => n);

            return string.Join("\n  ", names);
        }

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

        public static string LocateAssembly(string fileName)
        {
            var sourceDir = FindSourceDirectory();
            var gameVersion = "1.6";

            var candidates = new List<string>();

            if (!string.IsNullOrEmpty(sourceDir))
            {
                candidates.Add(Path.Combine(sourceDir, "..", gameVersion, "Assemblies", fileName));
                candidates.Add(Path.Combine(sourceDir, "..", "Assemblies", fileName));
            }

            candidates.Add(Path.Combine(AppContext.BaseDirectory, fileName));

            foreach (var candidate in candidates)
            {
                var full = Path.GetFullPath(candidate);
                if (File.Exists(full))
                    return full;
            }

            throw new FileNotFoundException(
                $"R-D: 找不到 {fileName}。搜索路径:\n  {string.Join("\n  ", candidates.Select(Path.GetFullPath))}\n" +
                "请先构建三个源码项目: dotnet build RimMind-Core/Source/Domain/RimMindCore.Domain.csproj 等");
        }

        public static bool TryLocateAssembly(string fileName, out string? path)
        {
            path = null;
            try
            {
                path = LocateAssembly(fileName);
                return true;
            }
            catch (FileNotFoundException)
            {
                return false;
            }
        }

        public static HashSet<string> GetAssemblyReferences(string assemblyPath)
        {
            var refs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            using var stream = File.OpenRead(assemblyPath);
            using var peReader = new PEReader(stream);
            var metadataReader = peReader.GetMetadataReader();

            foreach (var handle in metadataReader.AssemblyReferences)
            {
                var asmRef = metadataReader.GetAssemblyReference(handle);
                var name = metadataReader.GetString(asmRef.Name);
                refs.Add(name);
            }

            return refs;
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
