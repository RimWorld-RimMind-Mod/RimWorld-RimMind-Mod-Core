using System;
using RimMind.Domain.ValueObjects;

namespace RimMind.Application.Features.Llm
{
    /// <summary>
    /// Unified HTTP status &amp; response-body classifier shared by Core clients and extension model services.
    /// Separates content moderation (non-retryable, no failover), node-level quota/auth failures
    /// (isolate node &amp; fail over), transient rate-limit/5xx errors (short cooldown &amp; fail over),
    /// and permanent client errors.
    /// </summary>
    public static class HttpErrorClassifier
    {
        public static bool IsContentModerationResponse(int statusCode, string? responseBody)
        {
            return statusCode == 400 &&
                   !string.IsNullOrEmpty(responseBody) &&
                   (responseBody!.IndexOf("Content Exists Risk", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    responseBody.IndexOf("content_filter", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        public static bool IsQuotaOrAuthError(int statusCode, string? responseBody)
        {
            if (statusCode == 401 || statusCode == 402 || statusCode == 403 || statusCode == 410)
                return true;

            if (string.IsNullOrEmpty(responseBody))
                return false;

            string msg = responseBody!;
            return msg.IndexOf("Insufficient Balance", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   msg.IndexOf("Insufficient account funds", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   msg.IndexOf("insufficient_quota", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   msg.IndexOf("account_deactivated", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   msg.IndexOf("invalid_api_key", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   msg.IndexOf("billing_not_active", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   msg.IndexOf("model_not_found", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   msg.IndexOf("ModelDeprecated", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   msg.IndexOf("has been deprecated", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   msg.IndexOf("Model is unavailable", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   msg.IndexOf("is not supported by any configured account", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static bool IsTransientRateLimitOrQueueError(int statusCode, string? responseBody)
        {
            if (IsQuotaOrAuthError(statusCode, responseBody))
                return false;

            if (statusCode == 429)
                return true;

            if (string.IsNullOrEmpty(responseBody))
                return false;

            string msg = responseBody!;
            return msg.IndexOf("rate_limit", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   msg.IndexOf("gateway_concurrency_limit", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   msg.IndexOf("gateway_queue_full", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   msg.IndexOf("concurrency_limit", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   msg.IndexOf("Concurrency limit exceeded", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   msg.IndexOf("queue_full", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   msg.IndexOf("Too many pending requests", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   msg.IndexOf("server_overloaded", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static bool IsTransientHttpFailure(int statusCode, string? responseBody)
        {
            if (IsContentModerationResponse(statusCode, responseBody) || IsQuotaOrAuthError(statusCode, responseBody))
                return false;

            return statusCode == 408 ||
                   statusCode == 429 ||
                   statusCode >= 500 ||
                   IsTransientRateLimitOrQueueError(statusCode, responseBody);
        }

        public static RimMindError Classify(int statusCode, string? sanitizedBody, string? sourceLabel = null)
        {
            string safeBody = LogSecretRedactor.ForMessage(sanitizedBody);
            string errMsg = string.IsNullOrWhiteSpace(sourceLabel)
                ? $"HTTP {statusCode}: {safeBody}".TrimEnd(' ', ':')
                : $"Node '{sourceLabel}' HTTP {statusCode}: {safeBody}".TrimEnd(' ', ':');

            if (IsContentModerationResponse(statusCode, sanitizedBody))
                return RimMindErrors.ContentFiltered(errMsg);

            if (IsQuotaOrAuthError(statusCode, sanitizedBody))
                return RimMindErrors.ClientQuotaOrAuth(errMsg);

            if (IsTransientHttpFailure(statusCode, sanitizedBody))
                return RimMindErrors.ClientTransient(errMsg);

            return RimMindErrors.ClientPermanent(errMsg);
        }
    }
}
