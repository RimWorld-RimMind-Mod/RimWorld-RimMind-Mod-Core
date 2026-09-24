using System.Collections.Generic;
using System.Text;
using RimMind.Application.Common.Interfaces.Agent;
using RimMind.Application.Common.Interfaces.Agent.Psychology;
using RimMind.Application.Common.Models.Agent;
using RimMind.Application.Common.Models.Pipeline;
using RimMind.Application.Common.Models.Tools;
using RimMind.Application.Features.Agent.InnerVoice;
using RimMind.Domain.Llm;

namespace RimMind.Application.Features.Agent
{
    public sealed class ThinkContextEnricher : IEnvelopeEnricher
    {
        private readonly InnerVoiceHandler? _innerVoiceHandler;
        private readonly IPsychologyWatcher? _psychologyWatcher;

        public int Order => 10;

        public ThinkContextEnricher(
            InnerVoiceHandler? voiceHandler = null,
            IPsychologyWatcher? psychologyWatcher = null)
        {
            _innerVoiceHandler = voiceHandler;
            _psychologyWatcher = psychologyWatcher;
        }

        public void Enrich(LlmRequestEnvelope envelope, IAgentInfo agent)
        {
            if (envelope == null || agent == null) return;

            var voiceText = ConsumeInnerVoice(agent.NpcId);
            EnrichEnvelope(envelope, agent.NpcId, voiceText);
        }

        public string? ConsumeInnerVoice(string npcId)
        {
            var text = _innerVoiceHandler?.GetPendingVoiceText(npcId);
            if (!string.IsNullOrEmpty(text))
                _innerVoiceHandler?.ClearVoice(npcId);
            return text;
        }

        public void CheckPsychology(IAgentInfo agent, int pawnId)
        {
            _psychologyWatcher?.CheckAndPublish(agent, pawnId);
        }

        public void EnrichEnvelope(LlmRequestEnvelope envelope, string npcId, string? voiceText)
        {
            if (envelope == null) return;

            envelope.GameStateInfo ??= new GameStateInfo();

            var voice = voiceText ?? "";
            if (voice.Length > 0)
                envelope.GameStateInfo.AddSection("inner_voice", voice);

            if (_psychologyWatcher?.HasUrgentEvent(npcId) == true)
                envelope.GameStateInfo.AddSection("psychology_alert", "Urgent psychological event pending");
        }

        public string FormatToolCallResults(IReadOnlyList<ToolResult> results, int round)
        {
            var inner = ToolCallResultFormatter.Format(results, round);
            if (string.IsNullOrEmpty(inner)) return "";
            var sb = new StringBuilder();
            sb.AppendLine($"<tool_call_results round=\"{round}\">");
            sb.AppendLine(inner);
            sb.AppendLine("</tool_call_results>");
            return sb.ToString();
        }

        public void EnrichWithToolCallResults(LlmRequestEnvelope envelope, IReadOnlyList<ToolResult> results, int round)
        {
            if (envelope == null || results == null || results.Count == 0) return;

            var toolCallSection = FormatToolCallResults(results, round);
            if (string.IsNullOrEmpty(toolCallSection)) return;

            envelope.GameStateInfo ??= new GameStateInfo();
            envelope.GameStateInfo.AddSection($"tool_call_results", toolCallSection);
        }

        public string FormatBehaviorHistory(IReadOnlyList<BehaviorRecordDto> history, float successRate)
        {
            if (history == null || history.Count == 0) return "";

            var sb = new StringBuilder();
            sb.AppendLine("<behavior_history>");
            foreach (var record in history)
            {
                var status = record.Success ? "Success" : "Fail";
                sb.AppendLine($"- {record.Action} → {status}: {record.Reason}");
            }

            if (successRate < 0.4f)
            {
                sb.AppendLine("[Warning: Recent behavior success rate is low. Consider more cautious decisions.]");
            }

            sb.AppendLine("</behavior_history>");
            return sb.ToString();
        }
    }
}
