using System;

namespace RimMind.Infrastructure.UI.DebugCenter
{
    public sealed class DebugCenterNavigation
    {
        public string? RequestedPageId { get; private set; }
        public string? RequestedSubTabId { get; private set; }

        public void GoTo(string pageId, string? subTabId = null)
        {
            ResolveRoute(pageId, out string resolvedPageId, out string? resolvedSubTabId);
            RequestedPageId = resolvedPageId;
            RequestedSubTabId = !string.IsNullOrWhiteSpace(subTabId)
                ? subTabId!.Trim()
                : resolvedSubTabId;
        }

        public void SetRequestedSubTab(string? subTabId)
        {
            RequestedSubTabId = string.IsNullOrWhiteSpace(subTabId) ? null : subTabId!.Trim();
        }

        public string? ConsumeRequestedPageId()
        {
            string? requestedPageId = RequestedPageId;
            RequestedPageId = null;
            return requestedPageId;
        }

        public string? ConsumeRequestedSubTabId()
        {
            string? requestedSubTabId = RequestedSubTabId;
            RequestedSubTabId = null;
            return requestedSubTabId;
        }

        public static void ResolveRoute(string? rawRoute, out string pageId, out string? subTabId)
        {
            if (string.IsNullOrWhiteSpace(rawRoute))
            {
                pageId = string.Empty;
                subTabId = null;
                return;
            }

            string trimmed = rawRoute!.Trim();
            int colonIndex = trimmed.IndexOf(':');
            if (colonIndex > 0)
            {
                pageId = trimmed.Substring(0, colonIndex).Trim();
                string rawSub = trimmed.Substring(colonIndex + 1).Trim();
                subTabId = rawSub.Length > 0 ? rawSub : null;
                return;
            }

            switch (trimmed.ToLowerInvariant())
            {
                case "tool_calls":
                    pageId = "capabilities";
                    subTabId = "tool_calls";
                    return;
                case "mechanisms":
                    pageId = "capabilities";
                    subTabId = "mechanisms";
                    return;
                case "context_keys":
                    pageId = "capabilities";
                    subTabId = "context_keys";
                    return;
                case "rollout":
                case "rollouts":
                    pageId = "ai_requests";
                    subTabId = "rollout";
                    return;
                case "ephemeral":
                case "ephemeral_requests":
                    pageId = "ai_requests";
                    subTabId = "ephemeral";
                    return;
                default:
                    pageId = trimmed;
                    subTabId = null;
                    return;
            }
        }
    }
}
