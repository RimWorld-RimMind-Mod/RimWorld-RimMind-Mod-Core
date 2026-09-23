using System.Collections.Generic;
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
                UserPrompt = "[L4] <dialogue_trigger>\n请根据你当前的状态和周围环境,自然地做出回应。\n</dialogue_trigger>"
            };

            var rows = AgentRequestTraceRowBuilder.BuildRecent(new[] { entry });
            Assert.Single(rows);

            var row = rows[0];
            Assert.Equal(AgentRequestTraceStatus.Waiting, row.Status);
            Assert.Contains("DailyReaction", row.Summary);
            Assert.DoesNotContain("[L4]", row.Summary);
            Assert.DoesNotContain("<dialogue_trigger>", row.Summary);
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
        public void BuildRow_HandlesExpressDialogueToolCallWithEscapedJsonArguments()
        {
            var entry = new AIRequestTraceEntry
            {
                RequestId = "req-005",
                State = AIRequestTraceState.Completed,
                Response = "[{\"id\":\"call_99\",\"type\":\"function\",\"function\":{\"name\":\"express_dialogue\",\"arguments\":\"{\\\"speech\\\":\\\"今天天气真不错。\\\",\\\"thought_tag\\\":\\\"VALUED\\\",\\\"thought_desc\\\":\\\"轻松愉快\\\"}\"}}]"
            };
            entry.ToolCalls.Add(new AIRequestToolCallTrace("call_99", "express_dialogue", true, null));

            var rows = AgentRequestTraceRowBuilder.BuildRecent(new[] { entry });
            Assert.Single(rows);

            var row = rows[0];
            Assert.Equal(AgentRequestTraceStatus.Success, row.Status);
            Assert.Contains("今天天气真不错。", row.Summary);
            Assert.Contains("轻松愉快", row.Summary);
            Assert.DoesNotContain("express_dialogue", row.Summary);
            Assert.DoesNotContain("call_99", row.Summary);
        }
    }
}
