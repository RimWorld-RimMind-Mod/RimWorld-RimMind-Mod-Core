using System;
using System.Threading;

namespace RimMind.Application.Common.Models.UI
{
    public enum RequestCompletionReason
    {
        Selected,
        Expired,
        Evicted,
        Dismissed
    }

    public class RequestEntry
    {
        private int _completionStarted;

        public string title = "";
        public string description = "";
        public string[] options = Array.Empty<string>();
        public string[]? optionTooltips;
        public Action<string>? callback;
        public Action<RequestCompletionReason>? completionCallback;
        public object? pawn;
        public string source = "";
        public bool systemBlocked;
        public int expireTicks;
        public int tick;

        public int ExpireAtTicks { get; set; }

        public bool TryComplete(string? choice, RequestCompletionReason reason)
        {
            if (Interlocked.CompareExchange(ref _completionStarted, 1, 0) != 0)
                return false;

            try
            {
                if (choice != null)
                    callback?.Invoke(choice);
            }
            finally
            {
                completionCallback?.Invoke(reason);
            }

            return true;
        }
    }
}
