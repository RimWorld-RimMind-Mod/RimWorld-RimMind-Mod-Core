using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using RimMind.Application.Common.Models.Context;
using RimMind.Application.Common.Models.Pipeline;
using RimMind.Application.Common.Models.Tools;
using RimMind.Application.Features.Agent;
using RimMind.Application.Features.Llm;
using RimMind.Application.Features.Pipeline.Unified;
using RimMind.Domain.Llm;
using RimMind.Testing;
using Xunit;

namespace RimMind.Tests.Contracts
{
    public sealed class MultimodalVisionAttachmentContracts
    {
        private const string SampleBase64Png = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=";
        private const string SampleBase64Jpeg = "/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAAgGBgcGBQgHBwcJCQgKDBQNDAsLDBkSEw8UHRofHh0aHBwgJC4nICIsIxwcKDcpLDAxNDQ0Hyc5PTgyPC4zNDL/wAALCAABAAEBAREA/8QAFAABAAAAAAAAAAAAAAAAAAAACf/EABQQAQAAAAAAAAAAAAAAAAAAAAD/2gAIAQEAAD8AVN//2Q==";

        private const string GoldenPlainToolLoopRequest =
            "{\"model\":\"test-model\",\"messages\":[" +
            "{\"role\":\"system\",\"content\":\"S\"}," +
            "{\"role\":\"user\",\"content\":\"EU\"}," +
            "{\"role\":\"assistant\",\"content\":\"EA\"}," +
            "{\"role\":\"user\",\"content\":\"U1\"}," +
            "{\"role\":\"assistant\",\"content\":\"\",\"reasoning_content\":\"R\",\"tool_calls\":[{\"id\":\"call_1\",\"type\":\"function\",\"function\":{\"name\":\"actions_move_pawn\",\"arguments\":\"{\\\"x\\\":1}\"}}]}," +
            "{\"role\":\"tool\",\"content\":\"ok\",\"tool_call_id\":\"call_1\"}]," +
            "\"max_tokens\":256,\"temperature\":0.5,\"stream\":false," +
            "\"response_format\":{\"type\":\"json_schema\",\"json_schema\":{\"name\":\"response\",\"schema\":{\"type\":\"object\",\"properties\":{\"a\":{\"type\":\"string\"}}}}}," +
            "\"tools\":[" +
            "{\"type\":\"function\",\"function\":{\"name\":\"actions_move_pawn\",\"description\":\"Move\",\"parameters\":{\"type\":\"object\",\"properties\":{}}}}," +
            "{\"type\":\"function\",\"function\":{\"name\":\"core_query\",\"description\":\"Q\",\"parameters\":{\"type\":\"object\"}}}]," +
            "\"tool_choice\":\"required\"}";

        [Fact]
        public void OpenAIWireCodec_multimodal_attachments_parity_degradation_and_log_redaction()
        {
            ContractCaseRunner.Run(
                ("plain-text wire JSON stays byte-for-byte identical when Attachments is null or empty", () =>
                {
                    LlmRequestEnvelope BuildToolLoop(List<LlmImageAttachment>? userAttachments) => new LlmRequestEnvelope
                    {
                        RequestId = "req-parity",
                        MaxTokens = 256,
                        Temperature = 0.5f,
                        JsonSchema = "{\"type\":\"object\",\"properties\":{\"a\":{\"type\":\"string\"}}}",
                        Messages = new List<ChatMessage>
                        {
                            new ChatMessage { Role = "system", Content = "S" },
                            new ChatMessage { Role = "user", Content = "U1", Attachments = userAttachments },
                            new ChatMessage
                            {
                                Role = "assistant",
                                Content = "",
                                ReasoningContent = "R",
                                ToolCalls = new List<ChatToolCall>
                                {
                                    new ChatToolCall { Id = "call_1", Name = "actions_move_pawn", Arguments = "{\"x\":1}" }
                                }
                            },
                            new ChatMessage { Role = "tool", Content = "ok", ToolCallId = "call_1" },
                        },
                        Examples = new List<ChatMessage>
                        {
                            new ChatMessage { Role = "user", Content = "EU" },
                            new ChatMessage { Role = "assistant", Content = "EA" },
                        },
                        Tools = new List<StructuredTool>
                        {
                            new StructuredTool { Name = "core.query", Description = "Q", Parameters = "{\"type\":\"object\"}" },
                            new StructuredTool { Name = "actions.move_pawn", Description = "Move", Parameters = null, ToolChoice = "required" },
                        },
                    };

                    string nullJson = OpenAIWireCodec.BuildRequestJson(BuildToolLoop(null), "test-model", defaultMaxTokens: 0);
                    string emptyJson = OpenAIWireCodec.BuildRequestJson(BuildToolLoop(new List<LlmImageAttachment>()), "test-model", defaultMaxTokens: 0);

                    Assert.Equal(GoldenPlainToolLoopRequest, nullJson);
                    Assert.Equal(GoldenPlainToolLoopRequest, emptyJson);
                }),
                ("user attachments serialize as OpenAI image_url content parts while non-user roles stay plain strings", () =>
                {
                    var envelope = new LlmRequestEnvelope
                    {
                        MaxTokens = 128,
                        Messages = new List<ChatMessage>
                        {
                            new ChatMessage
                            {
                                Role = "system",
                                LayerTag = "L0",
                                Content = "Static rules",
                                Attachments = new List<LlmImageAttachment>
                                {
                                    new LlmImageAttachment { MediaType = "image/png", Base64Data = SampleBase64Png }
                                }
                            },
                            new ChatMessage
                            {
                                Role = "user",
                                LayerTag = "L4",
                                Content = "Pick a safe spot for a bed.",
                                Attachments = new List<LlmImageAttachment>
                                {
                                    new LlmImageAttachment
                                    {
                                        MediaType = "image/png",
                                        Base64Data = SampleBase64Png,
                                        Label = "Colony overview r=16",
                                        Detail = "high"
                                    },
                                    new LlmImageAttachment
                                    {
                                        MediaType = "image/jpeg",
                                        Base64Data = SampleBase64Jpeg
                                    }
                                }
                            }
                        }
                    };

                    var parsed = JObject.Parse(OpenAIWireCodec.BuildRequestJson(envelope, "vision-model", 128));
                    var messages = (JArray)parsed["messages"]!;

                    // L0 system message must remain a plain string even if attachments were erroneously set on it
                    Assert.Equal(JTokenType.String, messages[0]["content"]!.Type);
                    Assert.Equal("Static rules", messages[0]["content"]!.Value<string>());

                    // L4 user message becomes a multimodal content-parts array
                    var parts = Assert.IsType<JArray>(messages[1]["content"]);
                    Assert.Equal(3, parts.Count);

                    Assert.Equal("text", parts[0]["type"]?.ToString());
                    Assert.Contains("Pick a safe spot for a bed.", parts[0]["text"]?.ToString());
                    Assert.Contains("Colony overview r=16", parts[0]["text"]?.ToString());

                    Assert.Equal("image_url", parts[1]["type"]?.ToString());
                    Assert.Equal($"data:image/png;base64,{SampleBase64Png}", parts[1]["image_url"]?["url"]?.ToString());
                    Assert.Equal("high", parts[1]["image_url"]?["detail"]?.ToString());

                    Assert.Equal("image_url", parts[2]["type"]?.ToString());
                    Assert.Equal($"data:image/jpeg;base64,{SampleBase64Jpeg}", parts[2]["image_url"]?["url"]?.ToString());
                    Assert.Null(parts[2]["image_url"]?["detail"]);
                }),
                ("allowVisionAttachments=false gracefully degrades multimodal user messages to plain text", () =>
                {
                    var envelope = new LlmRequestEnvelope
                    {
                        MaxTokens = 128,
                        Messages = new List<ChatMessage>
                        {
                            new ChatMessage { Role = "system", Content = "Rules" },
                            new ChatMessage
                            {
                                Role = "user",
                                Content = "Inspect room layout",
                                Attachments = new List<LlmImageAttachment>
                                {
                                    new LlmImageAttachment { MediaType = "image/png", Base64Data = SampleBase64Png, Detail = "low" }
                                }
                            }
                        }
                    };

                    string degradedJson = OpenAIWireCodec.BuildRequestJson(
                        envelope,
                        "text-only-model",
                        128,
                        allowVisionAttachments: false);
                    var parsed = JObject.Parse(degradedJson);
                    var userContent = parsed["messages"]?[1]?["content"];
                    Assert.NotNull(userContent);
                    Assert.Equal(JTokenType.String, userContent!.Type);
                    Assert.Equal("Inspect room layout", userContent.Value<string>());
                    Assert.DoesNotContain("image_url", degradedJson);
                }),
                ("EnsureJsonKeyword handles multimodal content arrays safely in both WireMessageDto and JArray paths", () =>
                {
                    // Base64 intentionally contains "jSoN" to prove image_url blobs do NOT trigger a false-positive match.
                    string fakeBase64ContainingJson = "AAAAjSoNBBBB" + SampleBase64Png;
                    var envelope = new LlmRequestEnvelope
                    {
                        MaxTokens = 128,
                        Messages = new List<ChatMessage>
                        {
                            new ChatMessage { Role = "system", Content = "You are a colonist." },
                            new ChatMessage
                            {
                                Role = "user",
                                Content = "Choose a bedroom tile.",
                                Attachments = new List<LlmImageAttachment>
                                {
                                    new LlmImageAttachment { MediaType = "image/png", Base64Data = fakeBase64ContainingJson }
                                }
                            }
                        }
                    };

                    string forcedJson = OpenAIWireCodec.BuildRequestJson(
                        envelope,
                        "vision-model",
                        128,
                        forceJsonMode: true);
                    var forcedObj = JObject.Parse(forcedJson);
                    Assert.Equal("json_object", forcedObj["response_format"]?["type"]?.ToString());
                    Assert.Contains("Please respond in JSON format.", forcedObj["messages"]?[0]?["content"]?.ToString());

                    // Also test public EnsureJsonKeyword(JArray) directly on both absent and present text-part cases
                    var jArrayAbsent = JArray.Parse(JObject.Parse(
                        OpenAIWireCodec.BuildRequestJson(envelope, "vision-model", 128, forceJsonMode: false))["messages"]!.ToString());
                    OpenAIWireCodec.EnsureJsonKeyword(jArrayAbsent);
                    Assert.Contains("Please respond in JSON format.", jArrayAbsent[0]["content"]?.ToString());

                    var envelopeWithJsonInUserText = new LlmRequestEnvelope
                    {
                        MaxTokens = 128,
                        Messages = new List<ChatMessage>
                        {
                            new ChatMessage { Role = "system", Content = "You are a colonist." },
                            new ChatMessage
                            {
                                Role = "user",
                                Content = "Reply as JSON with candidate_id.",
                                Attachments = new List<LlmImageAttachment>
                                {
                                    new LlmImageAttachment { MediaType = "image/png", Base64Data = SampleBase64Png }
                                }
                            }
                        }
                    };
                    var jArrayPresent = JArray.Parse(JObject.Parse(
                        OpenAIWireCodec.BuildRequestJson(envelopeWithJsonInUserText, "vision-model", 128, forceJsonMode: false))["messages"]!.ToString());
                    OpenAIWireCodec.EnsureJsonKeyword(jArrayPresent);
                    Assert.Equal("You are a colonist.", jArrayPresent[0]["content"]?.ToString());
                }),
                ("LlmRequestEnvelope.WithTraceAndHints and Clone preserve Attachments as independent copies", () =>
                {
                    var original = new LlmRequestEnvelope
                    {
                        RequestId = "req-attach",
                        Attachments = new List<LlmImageAttachment>
                        {
                            new LlmImageAttachment { MediaType = "image/png", Base64Data = SampleBase64Png, Label = "v1" }
                        }
                    };

                    var traced = original.WithTraceAndHints("trace-1", new Dictionary<string, string> { ["k"] = "v" });
                    var cloned = original.Clone();

                    Assert.NotNull(traced.Attachments);
                    Assert.NotNull(cloned.Attachments);
                    Assert.Single(traced.Attachments!);
                    Assert.Single(cloned.Attachments!);
                    Assert.Equal("v1", traced.Attachments![0].Label);
                    Assert.Equal("v1", cloned.Attachments![0].Label);

                    // Mutating copy must not clear original
                    traced.Attachments!.Clear();
                    Assert.Single(original.Attachments!);
                    Assert.Single(cloned.Attachments!);
                }),
                ("LogSecretRedactor collapses large data:image base64 URIs while preserving subtype and credential redaction", () =>
                {
                    string bigPngBase64 = "iVBORw0KGgo" + "AIzaFakePrefixInsideB6412345" + new string('A', 200);
                    string bigJpegBase64 = new string('B', 120);
                    string rawPayload = $"{{\"auth\":\"Bearer sk-secret1234567890\",\"img1\":\"data:image/png;base64,{bigPngBase64}\",\"img2\":\"data:image/jpeg;base64,{bigJpegBase64}\"}}";

                    string redacted = LogSecretRedactor.Redact(rawPayload);
                    Assert.Contains("data:image/png;base64,<omitted>", redacted);
                    Assert.Contains("data:image/jpeg;base64,<omitted>", redacted);
                    Assert.DoesNotContain(new string('A', 32), redacted);
                    Assert.DoesNotContain(new string('B', 32), redacted);
                    Assert.DoesNotContain("sk-secret1234567890", redacted);
                    Assert.DoesNotContain("AIzaFakePrefixInsideB6412345", redacted);
                }));
        }

        [Fact]
        public async Task ThinkContextEnricher_and_ContextBuildMiddleware_route_attachments_to_L4_user_turn_only_with_caps()
        {
            await ContractCaseRunner.RunAsync(
                ("ThinkContextEnricher enforces max 2 attachments & 524,288-char cap and keeps base64 out of GameStateInfo XML", () =>
                {
                    var enricher = new ThinkContextEnricher();
                    var envelope = new LlmRequestEnvelope
                    {
                        RequestId = "req-enrich",
                        GameStateInfo = new GameStateInfo()
                            .AddSection("perceptions", "Saw open bedroom space near north wall.")
                    };

                    string oversizedBase64 = new string('Z', (512 * 1024) + 1);
                    var results = new List<ToolResult>
                    {
                        ToolResult.Ok("Map overview rendered; candidates [1..4].", "tc-1", "inspect_map_view") with
                        {
                            Attachments = new[]
                            {
                                new LlmImageAttachment { MediaType = "image/png", Base64Data = "   ", Label = "blank" },
                                new LlmImageAttachment { MediaType = "image/png", Base64Data = oversizedBase64, Label = "too-big" },
                                new LlmImageAttachment { MediaType = "image/png", Base64Data = SampleBase64Png, Label = "overview" },
                                new LlmImageAttachment { MediaType = "image/jpeg", Base64Data = SampleBase64Jpeg, Label = "zoom" },
                                new LlmImageAttachment { MediaType = "image/png", Base64Data = SampleBase64Png, Label = "third-dropped" }
                            }
                        }
                    };

                    enricher.EnrichWithToolCallResults(envelope, results, round: 2);

                    Assert.NotNull(envelope.Attachments);
                    Assert.Equal(2, envelope.Attachments!.Count);
                    Assert.Equal("overview", envelope.Attachments[0].Label);
                    Assert.Equal("zoom", envelope.Attachments[1].Label);

                    string xml = envelope.GameStateInfo!.ToXml();
                    Assert.DoesNotContain(SampleBase64Png, xml);
                    Assert.DoesNotContain(SampleBase64Jpeg, xml);
                    Assert.DoesNotContain("data:image", xml);
                    Assert.True(new BudgetOverflowGuardrail().Check(envelope).Passed);
                    return Task.CompletedTask;
                }),
                ("ContextBuildMiddleware pre-populated branch attaches strictly to final user turn (or appends L4 user turn) and leaves system prefixes pure", async () =>
                {
                    var sharedUserMsg = new ChatMessage { Role = "user", LayerTag = "L4", Content = "Final user prompt" };
                    var envelopeWithUser = new LlmRequestEnvelope
                    {
                        Messages = new List<ChatMessage>
                        {
                            new ChatMessage { Role = "system", LayerTag = "L0", Content = "L0 static" },
                            new ChatMessage { Role = "system", LayerTag = "L1", Content = "L1 baseline" },
                            new ChatMessage { Role = "system", LayerTag = "L2", Content = "L2 environment" },
                            new ChatMessage { Role = "user", LayerTag = "L4", Content = "Earlier user turn" },
                            sharedUserMsg
                        },
                        Attachments = new List<LlmImageAttachment>
                        {
                            new LlmImageAttachment { MediaType = "image/png", Base64Data = SampleBase64Png, Label = "view1" }
                        }
                    };

                    var mw = new ContextBuildMiddleware();
                    await mw.InvokeAsync(new LlmRequestContext(envelopeWithUser), _ => Task.CompletedTask);

                    Assert.Null(envelopeWithUser.Attachments);
                    Assert.All(envelopeWithUser.Messages.Where(m => m.Role == "system"), m => Assert.Null(m.Attachments));
                    Assert.Null(envelopeWithUser.Messages[3].Attachments);
                    Assert.NotNull(envelopeWithUser.Messages[4].Attachments);
                    Assert.Single(envelopeWithUser.Messages[4].Attachments!);
                    // Shared original ChatMessage instance must not be mutated in place
                    Assert.Null(sharedUserMsg.Attachments);

                    // Pre-populated messages with NO user turn must append a new L4 user message
                    var systemOnlyEnvelope = new LlmRequestEnvelope
                    {
                        Messages = new List<ChatMessage>
                        {
                            new ChatMessage { Role = "system", LayerTag = "L0", Content = "L0 static" },
                            new ChatMessage { Role = "system", LayerTag = "L1", Content = "L1 baseline" }
                        },
                        Attachments = new List<LlmImageAttachment>
                        {
                            new LlmImageAttachment { MediaType = "image/png", Base64Data = SampleBase64Png, Label = "view2" }
                        }
                    };

                    await mw.InvokeAsync(new LlmRequestContext(systemOnlyEnvelope), _ => Task.CompletedTask);

                    Assert.Null(systemOnlyEnvelope.Attachments);
                    Assert.Equal(3, systemOnlyEnvelope.Messages.Count);
                    Assert.Null(systemOnlyEnvelope.Messages[0].Attachments);
                    Assert.Null(systemOnlyEnvelope.Messages[1].Attachments);
                    Assert.Equal("user", systemOnlyEnvelope.Messages[2].Role);
                    Assert.Equal("[Attached visual inspection view]", systemOnlyEnvelope.Messages[2].Content);
                    Assert.Equal("L4", systemOnlyEnvelope.Messages[2].LayerTag);
                    Assert.Single(systemOnlyEnvelope.Messages[2].Attachments!);
                }),
                ("ContextBuildMiddleware IContextEngine branch routes enriched attachments strictly onto L4 user turn and preserves L0/L1/L2 strings", async () =>
                {
                    var runtime = new AsyncContextBuildContracts.ContextRuntime();
                    runtime.Keys.Register(new ContextProviderDef(
                        "l0_rules",
                        ContextLayer.L0_Static,
                        1.0f,
                        (_, _) => Task.FromResult<string?>("Static colonist directives"),
                        stalenessTicks: 0));
                    runtime.Keys.Register(new ContextProviderDef(
                        "l1_map",
                        ContextLayer.L1_Baseline,
                        1.0f,
                        (_, _) => Task.FromResult<string?>("Map baseline topology"),
                        stalenessTicks: 0));
                    runtime.Keys.Register(new ContextProviderDef(
                        "l2_weather",
                        ContextLayer.L2_Environment,
                        1.0f,
                        (_, _) => Task.FromResult<string?>("Clear spring morning"),
                        stalenessTicks: 0));

                    var envelope = LlmRequestEnvelopeBuilder
                        .ForNpc("pawn-vision", new GameStateInfo().AddSection("perceptions", "Need to place a bed."))
                        .ForScenarioId("agent_decision")
                        .Build();

                    var enricher = new ThinkContextEnricher();
                    enricher.EnrichWithToolCallResults(
                        envelope,
                        new[]
                        {
                            ToolResult.Ok("Candidates [1..3] rendered.", "tc-vis", "inspect_map_view") with
                            {
                                Attachments = new[]
                                {
                                    new LlmImageAttachment
                                    {
                                        MediaType = "image/png",
                                        Base64Data = SampleBase64Png,
                                        Label = "zoom_r8",
                                        Detail = "high"
                                    }
                                }
                            }
                        },
                        round: 2);

                    var mw = new ContextBuildMiddleware(runtime.Engine);
                    var reqCtx = new LlmRequestContext(envelope);
                    await mw.InvokeAsync(reqCtx, _ => Task.CompletedTask);

                    Assert.NotNull(reqCtx.Snapshot);
                    Assert.Null(envelope.Attachments);

                    var systemMessages = envelope.Messages.Where(m => m.Role == "system").ToList();
                    Assert.NotEmpty(systemMessages);
                    Assert.All(systemMessages, m => Assert.Null(m.Attachments));

                    var lastUser = envelope.Messages.LastOrDefault(m => m.Role == "user");
                    Assert.NotNull(lastUser);
                    Assert.NotNull(lastUser!.Attachments);
                    Assert.Single(lastUser.Attachments!);
                    Assert.Equal("zoom_r8", lastUser.Attachments![0].Label);

                    // Verify wire serialization emits plain strings for L0/L1/L2 and image_url only on final L4 user turn
                    var wire = JObject.Parse(OpenAIWireCodec.BuildRequestJson(envelope, "vision-model", 256));
                    var wireMessages = (JArray)wire["messages"]!;
                    foreach (var wm in wireMessages)
                    {
                        if (string.Equals(wm["role"]?.ToString(), "system", StringComparison.OrdinalIgnoreCase))
                        {
                            Assert.Equal(JTokenType.String, wm["content"]!.Type);
                        }
                    }

                    var wireLastUser = wireMessages.Last(m => string.Equals(m["role"]?.ToString(), "user", StringComparison.OrdinalIgnoreCase));
                    var contentParts = Assert.IsType<JArray>(wireLastUser["content"]);
                    Assert.Contains(contentParts, p => p["type"]?.ToString() == "image_url" && p["image_url"]?["detail"]?.ToString() == "high");
                }));
        }
    }
}
