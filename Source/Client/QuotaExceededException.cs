using System;

namespace RimMind.Core.Client
{
    public class QuotaExceededException : Exception
    {
        public QuotaExceededException() : base("Player2 quota exceeded. Please top up your Joules balance or switch to another provider.") { }
        public QuotaExceededException(string message) : base(message) { }
        public QuotaExceededException(string message, Exception innerException) : base(message, innerException) { }

        public static bool IsQuotaError(string error)
        {
            if (string.IsNullOrEmpty(error)) return false;
            return error.Contains("[QuotaExceeded]", StringComparison.OrdinalIgnoreCase)
                || error.Contains("quota", StringComparison.OrdinalIgnoreCase)
                || error.Contains("insufficient_balance", StringComparison.OrdinalIgnoreCase)
                || error.Contains("payment_required", StringComparison.OrdinalIgnoreCase);
        }
    }
}
