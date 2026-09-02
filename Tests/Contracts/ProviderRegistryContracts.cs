using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using RimMind.Application.Common.Interfaces.Abstractions;
using RimMind.Presentation.Runtime;
using Xunit;

namespace RimMind.Tests.Contracts
{
    public sealed class ProviderRegistryContracts
    {
        [Fact]
        public void Highest_priority_owner_wins_within_a_category()
        {
            var registry = new ProviderRegistry();

            registry.RegisterPawnProvider("profile", "low.mod", _ => "low", 10, overrideExisting: false);
            registry.RegisterPawnProvider("profile", "high.mod", _ => "high", 20, overrideExisting: false);

            var result = registry.GetProviderData("profile", new object());

            Assert.True(result.IsOk);
            Assert.Equal("high", result.Value);
        }

        [Fact]
        public void Unregistering_winning_owner_restores_lower_priority_candidate()
        {
            var registry = new ProviderRegistry();
            registry.RegisterPawnProvider("profile", "low.mod", _ => "low", 10, overrideExisting: false);
            registry.RegisterPawnProvider("profile", "high.mod", _ => "high", 20, overrideExisting: false);

            Assert.Equal(1, registry.UnregisterByOwner("high.mod"));
            Assert.Equal("low", registry.GetProviderData("profile", new object()).Value);
        }

        [Fact]
        public void Static_provider_resolution_uses_owner_priority()
        {
            var registry = new ProviderRegistry();
            registry.RegisterStaticProvider("world", "high.mod", () => "high", 20);
            registry.RegisterStaticProvider("world", "low.mod", () => "low", 10);

            Assert.Equal("high", registry.GetStaticProviderData("world").Value);
        }

        [Fact]
        public void Owner_unregistration_removes_pawn_and_static_registrations()
        {
            var registry = new ProviderRegistry();
            registry.RegisterPawnProvider("profile", "feature.mod", _ => "pawn", 10, overrideExisting: false);
            registry.RegisterStaticProvider("world", "feature.mod", () => "static", 10);

            var removed = registry.UnregisterByOwner("feature.mod");

            Assert.Equal(2, removed);
            Assert.True(registry.GetProviderData("profile", new object()).IsErr);
            Assert.True(registry.GetStaticProviderData("world").IsErr);
            Assert.DoesNotContain("profile", registry.GetRegisteredCategories());
            Assert.DoesNotContain("world", registry.GetRegisteredCategories());
        }

        [Fact]
        public void Replacing_a_typed_provider_emits_a_structured_warning()
        {
            var log = new CapturingLogSink();
            var registry = new ProviderRegistry(log);

            registry.RegisterProvider<object>("first");
            registry.RegisterProvider<object>(new object());

            var warning = Assert.Single(log.Warnings);
            Assert.Contains("event=typed_provider_replaced", warning);
            Assert.Contains("service_type=System.Object", warning);
            Assert.Contains("previous_type=System.String", warning);
            Assert.Contains("replacement_type=System.Object", warning);
        }

        [Fact]
        public void Public_facade_exposes_registration_reads_and_owner_unregistration()
        {
            var providersFacade = ReadSource("Presentation/Api/RimMindAPI.Providers.cs");
            var rootFacade = ReadSource("RimMindAPI.cs");
            var normalizedProvidersFacade = providersFacade.Replace("\r\n", "\n");
            Assert.Contains(
                "public static void RegisterPawnProvider(",
                providersFacade,
                StringComparison.Ordinal);
            Assert.Contains(
                "Func<Pawn, string?> provider",
                providersFacade,
                StringComparison.Ordinal);
            Assert.Contains(
                "Registries.Value.RegisterPawnProvider(\n" +
                "                    category,\n" +
                "                    ownerModId,\n" +
                "                    value => value is Pawn pawn ? provider(pawn) : null,\n" +
                "                    priority,\n" +
                "                    overrideExisting);",
                normalizedProvidersFacade,
                StringComparison.Ordinal);
            Assert.Contains(
                "public static void RegisterStaticProvider(",
                providersFacade,
                StringComparison.Ordinal);
            Assert.Contains(
                "Registries.Value.RegisterStaticProvider(\n" +
                "                    category,\n" +
                "                    ownerModId,\n" +
                "                    provider,\n" +
                "                    priority);",
                normalizedProvidersFacade,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "RegisterPawnProvider(",
                rootFacade,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "RegisterStaticProvider(",
                rootFacade,
                StringComparison.Ordinal);
            var composition = ReadSource("Presentation/Runtime/Composition/ContextComposition.cs");

            Assert.Contains("public static int UnregisterByOwner(string ownerModId)", providersFacade);
            Assert.Contains("Registries.Value.UnregisterByOwner(ownerModId)", providersFacade);
            Assert.Contains("public static int UnregisterModProviders(string modId)", rootFacade);
            Assert.Contains("Providers.UnregisterByOwner(modId)", rootFacade);
            Assert.Contains("new ProviderRegistry(logSink)", composition);
        }

        [Fact]
        public void Owner_identity_is_required_for_registration_and_unregistration()
        {
            var registry = new ProviderRegistry();

            Assert.Throws<System.ArgumentException>(() =>
                registry.RegisterPawnProvider("profile", " ", _ => "pawn", 1, overrideExisting: false));
            Assert.Throws<System.ArgumentException>(() =>
                registry.RegisterStaticProvider("world", " ", () => "static", 1));
            Assert.Throws<System.ArgumentException>(() => registry.UnregisterByOwner(" "));
        }

        [Fact]
        public void Provider_failures_preserve_the_original_exception()
        {
            var registry = new ProviderRegistry();
            var pawnFailure = new System.InvalidOperationException("pawn failed");
            var staticFailure = new System.ApplicationException("static failed");
            registry.RegisterPawnProvider("profile", "feature.mod", _ => throw pawnFailure, 1, overrideExisting: false);
            registry.RegisterStaticProvider("world", "feature.mod", () => throw staticFailure, 1);

            var pawnResult = registry.GetProviderData("profile", new object());
            var staticResult = registry.GetStaticProviderData("world");

            Assert.Same(pawnFailure, pawnResult.Error.InnerException);
            Assert.Same(staticFailure, staticResult.Error.InnerException);
        }

        [Fact]
        public void Equal_priority_and_override_rules_are_deterministic_and_owner_local()
        {
            var registry = new ProviderRegistry();
            registry.RegisterPawnProvider("profile", "z.mod", _ => "z", 20, overrideExisting: false);
            registry.RegisterPawnProvider("profile", "a.mod", _ => "a-v1", 20, overrideExisting: false);
            registry.RegisterPawnProvider("profile", "a.mod", _ => "ignored", 50, overrideExisting: false);

            Assert.Equal("a-v1", registry.GetProviderData("profile", new object()).Value);

            registry.RegisterPawnProvider("profile", "a.mod", _ => "a-v2", 5, overrideExisting: true);
            Assert.Equal("z", registry.GetProviderData("profile", new object()).Value);

            registry.UnregisterByOwner("z.mod");
            Assert.Equal("a-v2", registry.GetProviderData("profile", new object()).Value);
        }

        [Fact]
        public void Concurrent_registration_resolution_and_unregistration_are_safe()
        {
            var registry = new ProviderRegistry();

            Parallel.For(0, 128, i =>
            {
                registry.RegisterPawnProvider("profile", $"owner.{i:D3}", _ => i.ToString(), i, overrideExisting: false);
                Assert.True(registry.GetProviderData("profile", new object()).IsOk);
            });

            Assert.Equal("127", registry.GetProviderData("profile", new object()).Value);
            Parallel.For(0, 128, i =>
            {
                if (i % 2 == 1)
                    registry.UnregisterByOwner($"owner.{i:D3}");
            });
            Assert.Equal("126", registry.GetProviderData("profile", new object()).Value);
        }

        private static string ReadSource(string relativePath)
        {
            var directory = new DirectoryInfo(System.AppContext.BaseDirectory);
            while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "RimMind-Core", "Source")))
                directory = directory.Parent;
            var sourceRoot = Path.Combine(directory!.FullName, "RimMind-Core", "Source");
            return File.ReadAllText(Path.Combine(sourceRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        }

        private sealed class CapturingLogSink : ILogSink
        {
            public List<string> Warnings { get; } = new List<string>();
            public void Message(string msg) { }
            public void Warning(string msg) => Warnings.Add(msg);
            public void Error(string msg) { }
            public void LogFromBackground(string msg, bool isWarning = false) { }
        }
    }
}
