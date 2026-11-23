using System;
using System.Collections.Generic;
using RimMind.Application.Common.Interfaces.Diagnostics;
using RimMind.Application.Common.Models.Debug;
using RimMind.Infrastructure.UI.AgentsPage;
using Xunit;

namespace RimMind.Tests.Contracts
{
    public class AgentTraceFormattingContracts
    {
        [Fact]
        public void BuildRow_ExtractsNarrationAndThought_WhenResponseIsJson()
        {
            var entry = new AIRequestTraceEntry
            {
                RequestId = "req-001",
                State = AIRequestTraceState.Completed,
                Model = "claude-3-5-sonnet",
                Response = "{\"narration\":\"你感到全身旧伤，只想找个地方好好睡一觉。\",\"thought\":{\"tag\":\"STRESSED\",\"description\":\"疲惫\"}}",
                ElapsedMs = 350,
                TokensUsed = 420
            };

            var rows = AgentRequestTraceRowBuilder.BuildRecent(new[] { entry });
            Assert.Single(rows);

            var row = rows[0];
            Assert.Equal(AgentRequestTraceStatus.Success, row.Status);
            Assert.Contains("你感到全身旧伤", row.Summary);
            Assert.Contains("疲惫", row.Summary);
            Assert.DoesNotContain("narration", row.Summary);
            Assert.DoesNotContain("{", row.Summary);
            Assert.DoesNotContain("\n", row.Summary);

            // Rich Tooltip verification
            Assert.Contains("350", row.TooltipDetail);
            Assert.Contains("420", row.TooltipDetail);
            Assert.Contains("claude-3-5-sonnet", row.TooltipDetail);
        }

        [Fact]
        public void BuildRow_CleansXmlTagsAndNewlines_WhenWaiting()
        {
            var entry = new AIRequestTraceEntry
            {
                RequestId = "req-002",
                State = AIRequestTraceState.Running,
                UserPrompt = "[L4] <event_trigger>\nObserve surrounding state and respond naturally.\n</event_trigger>"
            };

            var rows = AgentRequestTraceRowBuilder.BuildRecent(new[] { entry });
            Assert.Single(rows);

            var row = rows[0];
            Assert.Equal(AgentRequestTraceStatus.Waiting, row.Status);
            Assert.Contains("Observe surrounding state", row.Summary);
            Assert.DoesNotContain("[L4]", row.Summary);
            Assert.DoesNotContain("<event_trigger>", row.Summary);
            Assert.DoesNotContain("\n", row.Summary);
            Assert.DoesNotContain("\r", row.Summary);
        }

        [Fact]
        public void BuildRow_FormatsToolCalls_WhenPresent()
        {
            var entry = new AIRequestTraceEntry
            {
                RequestId = "req-003",
                State = AIRequestTraceState.Completed,
                Response = "Thinking completed."
            };
            entry.ToolCalls.Add(new AIRequestToolCallTrace("call-1", "haul_item", true, null));

            var rows = AgentRequestTraceRowBuilder.BuildRecent(new[] { entry });
            Assert.Single(rows);

            var row = rows[0];
            Assert.Equal(AgentRequestTraceStatus.Success, row.Status);
            Assert.Contains("haul_item", row.Summary);
            Assert.DoesNotContain("\n", row.Summary);
        }

        [Fact]
        public void BuildRow_HandlesMarkdownCodeBlockJson()
        {
            var entry = new AIRequestTraceEntry
            {
                RequestId = "req-004",
                State = AIRequestTraceState.Completed,
                Response = "```json\n{\"reply\":\"你好，同伴。\",\"thought\":{\"tag\":\"CONNECTED\",\"description\":\"亲近\"}}\n```"
            };

            var rows = AgentRequestTraceRowBuilder.BuildRecent(new[] { entry });
            Assert.Single(rows);

            var row = rows[0];
            Assert.Equal(AgentRequestTraceStatus.Success, row.Status);
            Assert.Contains("你好，同伴。", row.Summary);
            Assert.Contains("亲近", row.Summary);
            Assert.DoesNotContain("```", row.Summary);
        }

        [Fact]
        public void BuildRow_DelegatesSummarizationAndToolSuppression_ToRegisteredTraceSummarizer()
        {
            const string owner = "test.trace.summarizer";
            var summarizer = new StubTraceSummarizer(owner);
            RequestTraceSummarizerRegistry.Register(summarizer);
            try
            {
                var waitingEntry = new AIRequestTraceEntry
                {
                    RequestId = "req-005-wait",
                    State = AIRequestTraceState.Running,
                    UserPrompt = "[L4] <custom_trigger>auto</custom_trigger>"
                };
                var waitRow = Assert.Single(AgentRequestTraceRowBuilder.BuildRecent(new[] { waitingEntry }));
                Assert.Equal(AgentRequestTraceStatus.Waiting, waitRow.Status);
                Assert.Contains("CustomTriggerSummary", waitRow.Summary);

                var completedEntry = new AIRequestTraceEntry
                {
                    RequestId = "req-005-done",
                    State = AIRequestTraceState.Completed,
                    Response = "[{\"id\":\"call_99\",\"type\":\"function\",\"function\":{\"name\":\"custom_speech_tool\",\"arguments\":\"{}\"}}]"
                };
                completedEntry.ToolCalls.Add(new AIRequestToolCallTrace("call_99", "custom_speech_tool", true, null));

                var doneRow = Assert.Single(AgentRequestTraceRowBuilder.BuildRecent(new[] { completedEntry }));
                Assert.Equal(AgentRequestTraceStatus.Success, doneRow.Status);
                Assert.Contains("Clear skies today.", doneRow.Summary);
                Assert.DoesNotContain("custom_speech_tool", doneRow.Summary);
                Assert.DoesNotContain("call_99", doneRow.Summary);
            }
            finally
            {
                RequestTraceSummarizerRegistry.UnregisterByOwner(owner);
            }
        }

        private sealed class StubTraceSummarizer : IRequestTraceSummarizer
        {
            public StubTraceSummarizer(string ownerModId)
            {
                OwnerModId = ownerModId;
            }

            public string Id => "test.summarizer.custom";
            public string OwnerModId { get; }
            public int Order => 10;

            public bool TrySummarizeResponse(AIRequestTraceEntry entry, out RequestTraceSummary? summary)
            {
                if (entry.Response != null && entry.Response.Contains("custom_speech_tool", StringComparison.Ordinal))
                {
                    summary = new RequestTraceSummary("Clear skies today.", new[] { "custom_speech_tool" });
                    return true;
                }
                summary = null;
                return false;
            }

            public bool TrySummarizePrompt(string rawPrompt, out string? summary)
            {
                if (rawPrompt.Contains("<custom_trigger>", StringComparison.Ordinal))
                {
                    summary = "CustomTriggerSummary";
                    return true;
                }
                summary = null;
                return false;
            }

            public bool ShouldSuppressToolCall(string toolName)
                => string.Equals(toolName, "custom_speech_tool", StringComparison.OrdinalIgnoreCase);
        }
    }
}
