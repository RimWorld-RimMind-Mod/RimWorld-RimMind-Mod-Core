using System;
using System.IO;
using System.Linq;
using RimMind.Testing;
using Xunit;

namespace RimMind.Tests.Contracts
{
    public sealed class RuntimeLifecycleContracts
    {
        [Fact]
        public void Runtime_publication_is_atomic_and_explicitly_composed()
        {
            ContractCaseRunner.Run(
                ("runtime host owns initialize recompose and shutdown", () =>
                {
                    var source = ReadSource("Presentation/Runtime/RimMindRuntimeHost.cs");
                    Assert.Contains("Initialize(", source, StringComparison.Ordinal);
                    Assert.Contains("TryRecompose(", source, StringComparison.Ordinal);
                    Assert.Contains("Shutdown(", source, StringComparison.Ordinal);
                    Assert.Contains("Publish(", source, StringComparison.Ordinal);
                    Assert.Contains("Retire", source, StringComparison.Ordinal);
                }),
                ("composition keeps registry identity outside candidates", () =>
                {
                    var source = ReadSource("Presentation/Runtime/Composition/ExtensionRegistryCatalog.cs");
                    Assert.Contains("ConcurrentDictionary", source, StringComparison.Ordinal);
                    Assert.Contains("GetExtensionRegistry", source, StringComparison.Ordinal);
                    Assert.Contains("Fork()", source, StringComparison.Ordinal);
                    Assert.Contains(".Register(extension)", source, StringComparison.Ordinal);
                }),
                ("candidate construction has no irreversible global publication", () =>
                {
                    var source = ReadSource("Presentation/Runtime/RimMindCompositionRoot.cs");
                    Assert.DoesNotContain("PawnDataExtractor.Initialize", source, StringComparison.Ordinal);
                    Assert.DoesNotContain("InitializeDebugActions", source, StringComparison.Ordinal);
                    Assert.DoesNotContain("npcManagers.Current", source, StringComparison.Ordinal);
                    Assert.Contains("extensions.Fork()", ReadSource("Presentation/Runtime/RimMindRuntimeHost.cs"), StringComparison.Ordinal);
                }),
                ("runtime host exposes lifecycle operations but not a current-runtime locator", () =>
                {
                    var source = ReadSource("Presentation/Runtime/RimMindRuntimeHost.cs");
                    Assert.DoesNotContain("RimMindRuntime Current", source, StringComparison.Ordinal);
                }),
                ("host retires replaced runtime state outside the lifecycle lock", () =>
                {
                    var source = ReadSource("Presentation/Runtime/RimMindRuntimeHost.cs");
                    Assert.Contains("retireReplacedLifetime: false", source, StringComparison.Ordinal);
                    AssertRetirementOutsideLock(source, "private static bool TryCompose(");
                    AssertRetirementOutsideLock(source, "public static void Shutdown()");
                }),
                ("AICoreMod is the sole production runtime initializer", () =>
                {
                    var mod = ReadSource("AICoreMod.cs");
                    var runtime = ReadSource("Presentation/Runtime/RimMindRuntime.cs");
                    var production = ReadTree(".");
                    Assert.Contains("RimMindRuntimeHost.Initialize(", mod, StringComparison.Ordinal);
                    Assert.DoesNotContain("public static void Initialize(", runtime, StringComparison.Ordinal);
                    Assert.DoesNotContain("static void Initialize(", runtime, StringComparison.Ordinal);
                    Assert.DoesNotContain("RimMindRuntime.Initialize(", production, StringComparison.Ordinal);
                    Assert.Equal(1, CountOccurrences(production, "RimMindRuntimeHost.Initialize("));
                }),
                ("AICoreMod startup identifiers have one exact contract", () =>
                {
                    var mod = ReadSource("AICoreMod.cs");
                    Assert.Contains("const string CurrentModVersion = \"2.0.0\"", mod, StringComparison.Ordinal);
                    Assert.Equal(1, CountOccurrences(mod, "\"2.0.0\""));
                    Assert.Contains("const string HarmonyId = \"mcocdaa.RimMindCore\"", mod, StringComparison.Ordinal);
                    Assert.Contains("const string DomainAssemblyName = \"0_RimMindDomain\"", mod, StringComparison.Ordinal);
                    Assert.Contains("const string ApplicationAssemblyName = \"1_RimMindApplication\"", mod, StringComparison.Ordinal);
                    Assert.Contains("const string CoreAssemblyName = \"2_RimMindCore\"", mod, StringComparison.Ordinal);
                    Assert.Contains("new Harmony(BootstrapConstants.HarmonyId)", mod, StringComparison.Ordinal);
                    Assert.Contains("a.GetName().Name == BootstrapConstants.DomainAssemblyName", mod, StringComparison.Ordinal);
                    Assert.Contains("a.GetName().Name == BootstrapConstants.ApplicationAssemblyName", mod, StringComparison.Ordinal);
                    Assert.Contains("a.GetName().Name == BootstrapConstants.CoreAssemblyName", mod, StringComparison.Ordinal);
                    Assert.Contains("throw new System.InvalidOperationException(msg)", mod, StringComparison.Ordinal);
                }),
                ("runtime backend has no legacy global lookup", () =>
                {
                    var runtime = ReadTree("Presentation/Runtime");
                    Assert.DoesNotContain("RimMindServiceLocator", runtime, StringComparison.Ordinal);
                    Assert.DoesNotContain("RimMindRuntime.Instance", runtime, StringComparison.Ordinal);
                    Assert.DoesNotContain("GetService<", runtime, StringComparison.Ordinal);
                }));
        }

        private static void AssertRetirementOutsideLock(string source, string methodSignature)
        {
            var methodStart = source.IndexOf(methodSignature, StringComparison.Ordinal);
            Assert.True(methodStart >= 0, $"Missing method: {methodSignature}");
            var lockStart = source.IndexOf("lock (Sync)", methodStart, StringComparison.Ordinal);
            Assert.True(lockStart >= 0, $"Missing lifecycle lock in {methodSignature}");
            var lockOpen = source.IndexOf('{', lockStart);
            var lockClose = FindMatchingBrace(source, lockOpen);
            var retire = source.IndexOf("Retire(", lockClose + 1, StringComparison.Ordinal);
            Assert.True(retire > lockClose, $"Retirement must occur after the lifecycle lock in {methodSignature}");
        }

        private static int FindMatchingBrace(string source, int openBrace)
        {
            Assert.True(openBrace >= 0);
            var depth = 0;
            for (var index = openBrace; index < source.Length; index++)
            {
                if (source[index] == '{') depth++;
                else if (source[index] == '}' && --depth == 0) return index;
            }

            throw new InvalidOperationException("Unbalanced braces.");
        }

        private static int CountOccurrences(string source, string value)
        {
            var count = 0;
            var offset = 0;
            while ((offset = source.IndexOf(value, offset, StringComparison.Ordinal)) >= 0)
            {
                count++;
                offset += value.Length;
            }

            return count;
        }

        private static string ReadSource(string relativePath) =>
            File.ReadAllText(Path.Combine(SourceRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar)));

        private static string ReadTree(string relativePath)
        {
            var directory = Path.Combine(SourceRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
            return string.Join("\n", Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories)
                .Select(File.ReadAllText));
        }

        private static string SourceRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "RimMind-Core", "Source")))
                directory = directory.Parent;
            return Path.Combine(directory?.FullName ?? throw new InvalidOperationException("Repository root not found."), "RimMind-Core", "Source");
        }
    }
}
