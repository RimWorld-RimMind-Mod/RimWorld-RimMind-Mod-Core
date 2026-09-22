using System.Threading.Tasks;
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

        [Fact]
        public void HttpTransport_attaches_opencode_session_header_when_opencode_url_or_key_used()
        {
            using var req1 = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, "https://opencode.ai/zen/go/v1/chat/completions");
            RimMind.Infrastructure.Services.Clients.HttpTransport.EnsureOpenCodeSessionHeader(req1, "https://opencode.ai/zen/go/v1/chat/completions", null);
            Assert.True(req1.Headers.Contains("x-opencode-session"));
            Assert.StartsWith("rimmind-", System.Linq.Enumerable.First(req1.Headers.GetValues("x-opencode-session")));

            using var req2 = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, "https://api.custom.com/v1");
            RimMind.Infrastructure.Services.Clients.HttpTransport.EnsureOpenCodeSessionHeader(req2, "https://api.custom.com/v1", "Bearer oc_sk_test_key_123", "custom-sess");
            Assert.True(req2.Headers.Contains("x-opencode-session"));
            Assert.Equal("custom-sess", System.Linq.Enumerable.First(req2.Headers.GetValues("x-opencode-session")));

            using var req3 = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, "https://api.openai.com/v1");
            RimMind.Infrastructure.Services.Clients.HttpTransport.EnsureOpenCodeSessionHeader(req3, "https://api.openai.com/v1", "Bearer sk-standard-key");
            Assert.False(req3.Headers.Contains("x-opencode-session"));
        }
    }
}
