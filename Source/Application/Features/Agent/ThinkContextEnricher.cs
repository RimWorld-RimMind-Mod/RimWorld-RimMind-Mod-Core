using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimMind.Application.Common.Interfaces.Agent;
using RimMind.Application.Common.Interfaces.Agent.Modes;
using RimMind.Application.Common.Interfaces.Agent.Psychology;
using RimMind.Application.Common.Interfaces.Extension;
using RimMind.Application.Common.Models.Agent;
using RimMind.Application.Common.Models.Pipeline;
using RimMind.Application.Common.Models.Tools;
using RimMind.Application.Features.Agent.InnerVoice;
using RimMind.Application.Features.Tools;
using RimMind.Domain.Llm;

namespace RimMind.Application.Features.Agent
{
    public sealed class ThinkContextEnricher : IEnvelopeEnricher
    {
        private readonly InnerVoiceHandler? _innerVoiceHandler;
        private readonly IPsychologyWatcher? _psychologyWatcher;
        private readonly IExtensionRegistry<IThinkContributor>? _contributorRegistry;
        private readonly Func<IExtensionRegistry<IThinkContributor>?>? _contributorRegistryProvider;

        public int Order => 10;

        public ThinkContextEnricher(
            InnerVoiceHandler? voiceHandler = null,
            IPsychologyWatcher? psychologyWatcher = null,
            IExtensionRegistry<IThinkContributor>? contributorRegistry = null,
            Func<IExtensionRegistry<IThinkContributor>?>? contributorRegistryProvider = null)
        {
            _innerVoiceHandler = voiceHandler;
            _psychologyWatcher = psychologyWatcher;
            _contributorRegistry = contributorRegistry;
            _contributorRegistryProvider = contributorRegistryProvider;
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

        public void EnrichEnvelope(LlmRequestEnvelope envelope, string npcId, string? voiceText, int pawnId = 0)
        {
            if (envelope == null) return;

            envelope.GameStateInfo ??= new GameStateInfo();

            var voice = voiceText ?? "";
            if (voice.Length > 0)
                envelope.GameStateInfo.AddSection("inner_voice", voice);

            if (_psychologyWatcher?.HasUrgentEvent(npcId) == true)
                envelope.GameStateInfo.AddSection("psychology_alert", "Urgent psychological event pending");

            var registry = _contributorRegistry ?? _contributorRegistryProvider?.Invoke();
            var contributors = registry?.All;
            if (contributors != null && contributors.Count > 0)
            {
                int resolvedPawnId = pawnId;
                if (resolvedPawnId <= 0 && !string.IsNullOrEmpty(npcId))
                {
                    PawnIdParser.TryParse(npcId, out resolvedPawnId);
                }

                var sections = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var contributor in contributors.OrderBy(c => c.Order))
                {
                    if (contributor == null) continue;
                    contributor.Contribute(npcId, resolvedPawnId, sections);
                }

                foreach (var kv in sections)
                {
                    if (!string.IsNullOrEmpty(kv.Key) && !string.IsNullOrEmpty(kv.Value))
                    {
                        envelope.GameStateInfo.AddSection(kv.Key, kv.Value);
                    }
                }
            }
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
            string? lastRawLine = null;
            string? lastNormalizedKey = null;
            int repeatCount = 0;

            void FlushPendingLine()
            {
                if (lastRawLine == null) return;
                string finalLine = repeatCount > 1
                    ? $"{lastRawLine.TrimEnd()} (x{repeatCount})"
                    : lastRawLine;
                if (sb.Length + finalLine.Length + 96 <= 1200)
                {
                    sb.AppendLine(finalLine);
                }
                lastRawLine = null;
                lastNormalizedKey = null;
                repeatCount = 0;
            }

            foreach (var record in history)
            {
                var status = record.Success
                    ? "Success"
                    : (!string.IsNullOrWhiteSpace(record.ResultReason) ? $"Fail ({record.ResultReason})" : "Fail");
                string reason = (record.Reason ?? "").Replace("\r\n", " ").Replace('\n', ' ').Trim();
                if (reason.Length > 160)
                    reason = reason.Substring(0, 160) + "...";
                string line = $"- {record.Action} → {status}: {reason}";
                string normalizedKey = line.TrimEnd();

                if (string.Equals(normalizedKey, lastNormalizedKey, StringComparison.OrdinalIgnoreCase))
                {
                    repeatCount++;
                }
                else
                {
                    FlushPendingLine();
                    lastRawLine = line;
                    lastNormalizedKey = normalizedKey;
                    repeatCount = 1;
                }
            }
            FlushPendingLine();

            if (successRate < 0.4f)
            {
                sb.AppendLine("[Warning: Recent behavior success rate is low. Consider more cautious decisions.]");
            }

            sb.AppendLine("</behavior_history>");
            return sb.ToString();
        }
    }
}
