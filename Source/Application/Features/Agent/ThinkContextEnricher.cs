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
using RimMind.Application.Features.Pipeline.Unified;
using RimMind.Application.Features.Tools;
using RimMind.Domain.Llm;

namespace RimMind.Application.Features.Agent
{
    public sealed class ThinkContextEnricher
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

        private const string ToolResultsTruncationNotice = "\n...[truncated to fit GameStateInfo budget]";
        private const int ToolResultsHeadroomChars = 400;
        private const int ToolResultsMinPayloadFloorChars = 512;

        public string FormatToolCallResults(IReadOnlyList<ToolResult> results, int round)
        {
            var inner = ToolCallResultFormatter.Format(results, round);
            return WrapToolCallResults(inner, round);
        }

        private static string WrapToolCallResults(string? inner, int round)
        {
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

            var inner = ToolCallResultFormatter.Format(results, round);
            if (string.IsNullOrEmpty(inner)) return;

            int existingLength = envelope.GameStateInfo?.ToXml().Length ?? 0;
            int wrapperOverhead = WrapToolCallResults("x", round).Length - 1
                + "<tool_call_results>".Length + "</tool_call_results>".Length
                + Environment.NewLine.Length * 3;

            int budgetWithHeadroom = BudgetOverflowGuardrail.MaxGameStateInfoLength - existingLength - ToolResultsHeadroomChars;
            int hardMaxInner = Math.Max(0, BudgetOverflowGuardrail.MaxGameStateInfoLength - existingLength - wrapperOverhead);
            int maxInner = Math.Min(hardMaxInner, Math.Max(ToolResultsMinPayloadFloorChars, budgetWithHeadroom));

            if (inner.Length > maxInner)
            {
                int keepChars = Math.Max(0, maxInner - ToolResultsTruncationNotice.Length);
                if (keepChars > 0)
                {
                    inner = inner.Substring(0, keepChars) + ToolResultsTruncationNotice;
                }
                else
                {
                    inner = hardMaxInner >= ToolResultsTruncationNotice.Length
                        ? ToolResultsTruncationNotice
                        : ToolResultsTruncationNotice.Substring(0, hardMaxInner);
                }
            }

            var toolCallSection = WrapToolCallResults(inner, round);
            if (string.IsNullOrEmpty(toolCallSection)) return;

            envelope.GameStateInfo ??= new GameStateInfo();
            envelope.GameStateInfo.AddSection("tool_call_results", toolCallSection);
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
