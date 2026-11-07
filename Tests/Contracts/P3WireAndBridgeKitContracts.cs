using System;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json.Linq;
using RimMind.Application.Common.Interfaces.Extension;
using RimMind.Application.Features.Bridges;
using RimMind.Domain.Llm;
using RimMind.Infrastructure.Services.Clients.OpenAI;
using RimMind.Infrastructure.Services.Clients.Shared;
using RimMind.Testing;
using Xunit;

namespace RimMind.Tests.Contracts
{
    public sealed class P3WireAndBridgeKitContracts
    {
        [Fact]
        public void OpenAIRequestSerializer_parses_responses_and_redacts_secrets_with_LogSanitizer()
        {
            ContractCaseRunner.Run(
                ("ParseResponse extracts tool calls, cached tokens, and reasoning content", () =>
                {
                    const string raw =
                        "{\"id\":\"chatcmpl-p3\",\"choices\":[{\"finish_reason\":\"tool_calls\",\"message\":{\"role\":\"assistant\",\"content\":null," +
                        "\"reasoning_content\":\"plan\",\"tool_calls\":[{\"id\":\"call_42\",\"type\":\"function\",\"function\":{\"name\":\"actions_move_pawn\",\"arguments\":\"{\\\"x\\\":7}\"}}]}}]," +
                        "\"usage\":{\"prompt_tokens\":40,\"completion_tokens\":10,\"total_tokens\":50,\"prompt_tokens_details\":{\"cached_tokens\":32}}}";

                    var envelope = new LlmRequestEnvelope
                    {
                        Tools = new List<StructuredTool> { new StructuredTool { Name = "actions.move_pawn" } }
                    };

                    var result = OpenAIRequestSerializer.ParseResponse(raw, envelope);
                    Assert.True(result.IsOk);
                    Assert.Equal("chatcmpl-p3", result.Value.RequestId);
                    Assert.Equal(string.Empty, result.Value.Content);
                    Assert.Equal("plan", result.Value.ReasoningContent);
                    Assert.Equal(40, result.Value.PromptTokens);
                    Assert.Equal(10, result.Value.CompletionTokens);
                    Assert.Equal(32, result.Value.CachedTokens);
                    Assert.Equal(50, result.Value.TokensUsed);

                    var calls = JArray.Parse(result.Value.ToolCallsJson!);
                    Assert.Single(calls);
                    Assert.Equal("call_42", calls[0]["id"]?.ToString());
                    Assert.Equal("actions_move_pawn", calls[0]["function"]?["name"]?.ToString());
                    Assert.Equal("{\"x\":7}", calls[0]["function"]?["arguments"]?.ToString());
                }),
                ("ParseResponse falls back to reasoning content only when tools were not offered and reply was not truncated", () =>
                {
                    const string stopRaw =
                        "{\"choices\":[{\"finish_reason\":\"stop\",\"message\":{\"role\":\"assistant\",\"content\":\"\",\"reasoning_content\":\"{\\\"ok\\\":true}\"}}]}";
                    var plainResult = OpenAIRequestSerializer.ParseResponse(stopRaw, new LlmRequestEnvelope());
                    Assert.True(plainResult.IsOk);
                    Assert.Equal("{\"ok\":true}", plainResult.Value.Content);
                    Assert.Null(plainResult.Value.ToolCallsJson);

                    const string lengthRaw =
                        "{\"choices\":[{\"finish_reason\":\"length\",\"message\":{\"role\":\"assistant\",\"content\":\"\",\"reasoning_content\":\"partial\"}}]}";
                    var truncatedResult = OpenAIRequestSerializer.ParseResponse(lengthRaw, new LlmRequestEnvelope());
                    Assert.True(truncatedResult.IsOk);
                    Assert.Equal(string.Empty, truncatedResult.Value.Content);
                    Assert.Equal("partial", truncatedResult.Value.ReasoningContent);
                }),
                ("ParseResponse redacts provider error payloads and LogSanitizer.ForMessageWithSecret masks custom node secrets", () =>
                {
                    const string errRaw =
                        "{\"error\":{\"message\":\"Auth failed for sk-proj-1234567890abcdef and Bearer secretToken99\"}}";
                    var errResult = OpenAIRequestSerializer.ParseResponse(errRaw);
                    Assert.True(errResult.IsErr);
                    Assert.DoesNotContain("sk-proj-1234567890abcdef", errResult.Error.Message);
                    Assert.DoesNotContain("secretToken99", errResult.Error.Message);

                    string customMasked = LogSanitizer.ForMessageWithSecret(
                        "Gateway rejected token gw_custom_key_77 and Bearer abcdefgh with payload " + new string('z', 400),
                        "gw_custom_key_77",
                        maxLength: 80);
                    Assert.DoesNotContain("gw_custom_key_77", customMasked);
                    Assert.DoesNotContain("abcdefgh", customMasked);
                    Assert.Contains("[truncated", customMasked);
                }));
        }

        [Fact]
        public void Bridge_kit_reflection_detector_and_coordinator_preserve_contracts()
        {
            ContractCaseRunner.Run(
                ("ReflectionAccessor caches members, deduplicates warnings, and trips/resets structural circuit breaker", () =>
                {
                    var warnings = new List<string>();
                    int resolveCalls = 0;
                    var accessor = new ReflectionAccessor(
                        typeName =>
                        {
                            resolveCalls++;
                            return typeName == "Probe" ? typeof(ReflectionProbe) : null;
                        },
                        msg => warnings.Add(msg),
                        "[TestBridge]");

                    Assert.Same(typeof(ReflectionProbe), accessor.GetCachedType("Probe"));
                    Assert.Same(typeof(ReflectionProbe), accessor.GetCachedType("Probe"));
                    Assert.Equal(1, resolveCalls);

                    var field1 = accessor.GetCachedField(typeof(ReflectionProbe), nameof(ReflectionProbe.Count));
                    var field2 = accessor.GetCachedField(typeof(ReflectionProbe), nameof(ReflectionProbe.Count));
                    Assert.NotNull(field1);
                    Assert.Same(field1, field2);

                    var prop = accessor.GetCachedProperty(typeof(ReflectionProbe), nameof(ReflectionProbe.Label));
                    var method = accessor.GetCachedMethod(typeof(ReflectionProbe), nameof(ReflectionProbe.Echo), BindingFlags.Public | BindingFlags.Static, new[] { typeof(string) });
                    Assert.NotNull(prop);
                    Assert.NotNull(method);
                    Assert.Equal("hello", accessor.GetStaticPropertyValue(typeof(ReflectionProbe), nameof(ReflectionProbe.Label)));

                    Assert.Null(accessor.GetCachedField(typeof(ReflectionProbe), "MissingField"));
                    Assert.Null(accessor.GetCachedField(typeof(ReflectionProbe), "MissingField"));
                    Assert.Single(warnings);

                    Assert.False(accessor.IsStructurallyDisabled);
                    accessor.MarkStructuralFailure("Missing tuple field Item1.");
                    accessor.MarkStructuralFailure("Missing tuple field Item1.");
                    Assert.True(accessor.IsStructurallyDisabled);
                    Assert.Equal("Missing tuple field Item1.", accessor.StructuralFailureReason);
                    Assert.Equal(2, warnings.Count);

                    accessor.ResetForTesting();
                    Assert.False(accessor.IsStructurallyDisabled);
                    Assert.Null(accessor.StructuralFailureReason);
                }),
                ("SoftDependencyDetector caches mod-active checks by tick interval, handles tick rollback, and resolves candidate API types", () =>
                {
                    int currentTick = 100;
                    int activeChecks = 0;
                    int typeChecks = 0;
                    bool modActive = true;

                    var detector = new SoftDependencyDetector(
                        "example.externalmod",
                        new[] { "Missing.ApiType", "Valid.ApiType" },
                        _ =>
                        {
                            activeChecks++;
                            return modActive;
                        },
                        typeName =>
                        {
                            typeChecks++;
                            return typeName == "Valid.ApiType" ? typeof(ReflectionProbe) : null;
                        },
                        () => currentTick,
                        cacheIntervalTicks: 300);

                    Assert.True(detector.IsModActive());
                    currentTick = 250;
                    Assert.True(detector.IsModActive());
                    Assert.Equal(1, activeChecks);

                    // Advance past 300-tick TTL
                    currentTick = 401;
                    Assert.True(detector.IsModActive());
                    Assert.Equal(2, activeChecks);

                    // Tick rollback (e.g. save reload) invalidates cached result
                    modActive = false;
                    currentTick = 50;
                    Assert.False(detector.IsModActive());
                    Assert.Equal(3, activeChecks);
                    Assert.False(detector.IsAvailable());

                    detector.ResetCache();
                    modActive = true;
                    currentTick = 500;
                    Assert.True(detector.IsAvailable());
                    Assert.Same(typeof(ReflectionProbe), detector.ResolveApiType());
                    Assert.Same(typeof(ReflectionProbe), detector.ResolveApiType());
                    Assert.Equal(2, typeChecks);
                }),
                ("ExternalModBridgeCoordinator coordinates modules and gates with exception isolation and refresh", () =>
                {
                    bool available = false;
                    var logs = new List<string>();
                    var registeredGates = new List<string>();
                    var m1 = new ProbeBridgeModule("m1");
                    var m2 = new ProbeBridgeModule("m2") { ThrowOnRegister = true };
                    var m3 = new ProbeBridgeModule("m3");
                    var gate = new ProbeSkipCheck("g1");

                    var coordinator = new ExternalModBridgeCoordinator(
                        () => available,
                        new IBridgeModule[] { m1, m2, m3 },
                        new ISkipCheck[] { gate },
                        logInfo: msg => logs.Add(msg),
                        registerGate: g => registeredGates.Add(g.Id),
                        unregisterGate: id => registeredGates.Remove(id));

                    coordinator.RegisterAll();
                    Assert.False(coordinator.AreAllRegistered);
                    Assert.Empty(registeredGates);

                    available = true;
                    coordinator.RegisterAll();
                    Assert.True(m1.IsRegistered);
                    Assert.False(m2.IsRegistered);
                    Assert.True(m3.IsRegistered);
                    Assert.False(coordinator.AreAllRegistered);
                    Assert.Single(registeredGates, "g1");

                    m2.ThrowOnRegister = false;
                    coordinator.Refresh();
                    Assert.True(coordinator.AreAllRegistered);
                    Assert.Single(registeredGates, "g1");

                    coordinator.UnregisterAll();
                    Assert.False(m1.IsRegistered);
                    Assert.False(m2.IsRegistered);
                    Assert.False(m3.IsRegistered);
                    Assert.Empty(registeredGates);
                }));
        }

        private sealed class ReflectionProbe
        {
            public int Count = 5;
            public static string Label => "hello";
            public static string Echo(string input) => input;
        }

        private sealed class ProbeBridgeModule : IBridgeModule
        {
            public ProbeBridgeModule(string id) => Id = id;
            public string Id { get; }
            public string OwnerModId => "TestBridge";
            public bool IsRegistered { get; private set; }
            public bool ThrowOnRegister { get; set; }

            public void Register()
            {
                if (IsRegistered) return;
                if (ThrowOnRegister) throw new InvalidOperationException("simulated failure");
                IsRegistered = true;
            }

            public void Unregister()
            {
                IsRegistered = false;
            }
        }

        private sealed class ProbeSkipCheck : ISkipCheck
        {
            public ProbeSkipCheck(string id) => Id = id;
            public string Id { get; }
            public string OwnerModId => "TestBridge";
            public SkipCheckKind Kind => SkipCheckKind.Dialogue;
            public bool ShouldSkip(in SkipCheckArgs args) => false;
        }
    }
}
