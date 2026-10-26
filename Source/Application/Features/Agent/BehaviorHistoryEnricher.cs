using System;
using System.Text;
using RimMind.Application.Common.Interfaces.Agent;
using RimMind.Domain.Llm;

namespace RimMind.Application.Features.Agent
{
    internal sealed class BehaviorHistoryEnricher : IEnvelopeEnricher
    {
        public int Order => 20;

        public void Enrich(LlmRequestEnvelope envelope, IAgentInfo agent)
        {
            if (envelope?.GameStateInfo == null || agent == null) return;
            var history = FormatBehaviorHistory(agent);
            if (!string.IsNullOrEmpty(history))
                envelope.GameStateInfo.AddSection("behavior_history", history);
        }

        private string FormatBehaviorHistory(IAgentInfo agent)
        {
            var recentHistory = agent.GetRecentHistory(10);
            var successRate = agent.GetRecentSuccessRate(10);

            if (recentHistory == null || recentHistory.Count == 0) return "";

            var sb = new StringBuilder();
            sb.AppendLine("<behavior_history>");
            string? lastRawLine = null;
            string? lastNormalizedKey = null;
            int repeatCount = 0;

            void FlushPendingLine()
            {
                if (lastRawLine == null) return;
                sb.AppendLine(repeatCount > 1
                    ? $"{lastRawLine.TrimEnd()} (x{repeatCount})"
                    : lastRawLine);
                lastRawLine = null;
                lastNormalizedKey = null;
                repeatCount = 0;
            }

            foreach (var record in recentHistory)
            {
                var status = record.Success
                    ? "Success"
                    : (!string.IsNullOrWhiteSpace(record.ResultReason) ? $"Fail ({record.ResultReason})" : "Fail");
                string line = $"- {record.Action} → {status}: {record.Reason}";
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
