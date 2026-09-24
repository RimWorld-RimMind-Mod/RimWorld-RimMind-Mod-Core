using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using RimMind.Application.Common.Interfaces.Abstractions;
using RimMind.Application.Common.Interfaces.Context;
using RimMind.Application.Features.AgentBus;
using RimMind.Application.Features.Context;
using RimMind.Domain.ValueObjects;
using Xunit;

namespace RimMind.Tests.Contracts
{
    public sealed class ContextRegistryLifecycleContracts
    {
        [Fact]
        public async Task Concurrent_replacement_reports_the_owner_that_was_actually_replaced()
        {
            var log = new BlockingWarningLogSink();
            var registry = new ContextKeyRegistryImpl(log);
            registry.Register(Meta("initial"));

            Task firstReplacement = Task.Run(() => registry.Register(Meta("owner.a")));
            Assert.True(log.FirstWarningEntered.Wait(TimeSpan.FromSeconds(2)));

            registry.Register(Meta("owner.b"));
            log.ReleaseFirstWarning.Set();
            await firstReplacement.WaitAsync(TimeSpan.FromSeconds(2));

            KeyMeta current = Assert.IsType<KeyMeta>(registry.Get("shared"));
            string otherReplacement = current.OwnerMod == "owner.a" ? "owner.b" : "owner.a";
            Assert.Equal(otherReplacement, current.OverrideSource);
            Assert.Contains(
                log.Warnings,
                warning => warning.Contains($"registered by '{otherReplacement}' overwritten by '{current.OwnerMod}'", StringComparison.Ordinal));

            var bus = new AgentBusImpl();
            var cache = new ProviderCache(bus);
            var definitionLog = new BlockingWarningLogSink();
            var definitionRegistry = new ContextKeyRegistryImpl(definitionLog, cache);
            definitionRegistry.Register(Definition("provider", "initial", "Perception"));

            Task firstDefinitionReplacement = Task.Run(() =>
                definitionRegistry.Register(Definition("provider", "owner.a", "Action")));
            Assert.True(definitionLog.FirstWarningEntered.Wait(TimeSpan.FromSeconds(2)));

            definitionRegistry.Register(Definition("provider", "owner.b", "Decision"));
            definitionLog.ReleaseFirstWarning.Set();
            await firstDefinitionReplacement.WaitAsync(TimeSpan.FromSeconds(2));

            Assert.Equal("owner.b", definitionRegistry.Get("provider")?.OwnerMod);
            Assert.Equal(1, bus.GetHandlerCount());
            Assert.Equal(
                new[] { "Decision" },
                GetInvalidationTriggers(cache, "provider"));
        }

        [Fact]
        public void Unregister_releases_provider_invalidation_handlers()
        {
            var bus = new AgentBusImpl();
            var cache = new ProviderCache(bus);
            var registry = new ContextKeyRegistryImpl(providerCache: cache);
            registry.Register(Definition("provider", "owner", "Perception", "Decision"));
            Assert.Equal(2, bus.GetHandlerCount());

            Assert.True(registry.Unregister("provider"));

            Assert.Equal(0, bus.GetHandlerCount());

            registry.Register(Definition("replaced", "owner.a", "Perception"));
            registry.Register(Definition("replaced", "owner.b", "Decision"));
            Assert.Equal(1, bus.GetHandlerCount());

            Assert.Equal(1, registry.UnregisterByOwner("owner.b"));
            Assert.Equal(0, bus.GetHandlerCount());

            registry.Register(Definition("first", "owner.a", "Perception"));
            registry.Register(Definition("second", "owner.b", "Decision"));
            registry.Clear();
            Assert.Equal(0, bus.GetHandlerCount());
        }

        [Fact]
        public void Runtime_shutdown_clears_context_and_bus_subscriptions()
        {
            string source = ReadSource("Presentation/Runtime/RimMindLifecycleManager.cs");
            int shutdownStart = source.IndexOf("public void Shutdown()", StringComparison.Ordinal);
            Assert.True(shutdownStart >= 0);

            string shutdown = source.Substring(shutdownStart);
            Assert.Contains("_keyRegistry?.Clear();", shutdown, StringComparison.Ordinal);
            Assert.Contains("_agentBus.ClearAllSubscribers();", shutdown, StringComparison.Ordinal);
            Assert.DoesNotContain("ResetState(", source, StringComparison.Ordinal);
        }

        private static KeyMeta Meta(string owner)
            => new KeyMeta(
                "shared",
                ContextLayer.L2_Environment,
                1.0f,
                _ => new List<ContextEntry>(),
                owner);

        private static ContextProviderDef Definition(string key, string owner, params string[] triggers)
            => new ContextProviderDef(
                key,
                ContextLayer.L2_Environment,
                1.0f,
                (_, _) => Task.FromResult<string?>(null),
                ownerMod: owner,
                invalidationTriggers: triggers);

        private static IReadOnlyList<string> GetInvalidationTriggers(ProviderCache cache, string providerKey)
        {
            FieldInfo subscriptionsField = typeof(ProviderCache).GetField(
                "_invalidationSubscriptions",
                BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("ProviderCache subscription registry not found.");
            var triggers = new List<string>();
            foreach (object entry in (IEnumerable)subscriptionsField.GetValue(cache)!)
            {
                object key = entry.GetType().GetProperty("Key")!.GetValue(entry)!;
                string registeredProvider = (string)key.GetType().GetProperty("ProviderKey")!.GetValue(key)!;
                if (string.Equals(registeredProvider, providerKey, StringComparison.Ordinal))
                    triggers.Add((string)key.GetType().GetProperty("EventName")!.GetValue(key)!);
            }
            triggers.Sort(StringComparer.Ordinal);
            return triggers;
        }

        private static string ReadSource(string relativePath)
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "RimMind-Core", "Source")))
                directory = directory.Parent;
            return File.ReadAllText(Path.Combine(directory!.FullName, "RimMind-Core", "Source", relativePath.Replace('/', Path.DirectorySeparatorChar)));
        }

        private sealed class BlockingWarningLogSink : ILogSink
        {
            private readonly object _sync = new object();
            private int _warningCount;

            public ManualResetEventSlim FirstWarningEntered { get; } = new ManualResetEventSlim();
            public ManualResetEventSlim ReleaseFirstWarning { get; } = new ManualResetEventSlim();
            public List<string> Warnings { get; } = new List<string>();

            public void Message(string msg) { }

            public void Warning(string msg)
            {
                lock (_sync)
                {
                    Warnings.Add(msg);
                }

                if (Interlocked.Increment(ref _warningCount) == 1)
                {
                    FirstWarningEntered.Set();
                    ReleaseFirstWarning.Wait(TimeSpan.FromSeconds(2));
                }
            }

            public void Error(string msg) { }
            public void LogFromBackground(string msg, bool isWarning = false) { }
        }
    }
}
