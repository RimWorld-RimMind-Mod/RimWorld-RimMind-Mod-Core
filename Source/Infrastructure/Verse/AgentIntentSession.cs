using System;
using Verse;
using Verse.AI;

namespace RimMind.Infrastructure.Verse
{
    /// <summary>
    /// Represents an active macro-intent lease held by an AI Agent on a colonist pawn.
    /// Unlike one-shot jobs, an Intent Session maintains continuous control across ticks
    /// and multiple sub-tasks (e.g., hauling materials then constructing a blueprint),
    /// competing effectively against vanilla ThinkTree wander/idle fallbacks.
    /// </summary>
    public class AgentIntentSession
    {
        public string IntentId { get; }
        public int StartTick { get; }
        public int ExpiryTick { get; }
        public float Priority { get; set; } = 7.8f;
        public object? Target { get; set; }

        public Func<Pawn, Job?>? JobProvider { get; set; }
        public Func<Pawn, bool>? CompletionPredicate { get; set; }

        public bool IsFinished { get; private set; }

        public AgentIntentSession(
            string intentId,
            int startTick,
            int durationTicks = 2500,
            float priority = 7.8f,
            object? target = null,
            Func<Pawn, Job?>? jobProvider = null,
            Func<Pawn, bool>? completionPredicate = null)
        {
            IntentId = string.IsNullOrWhiteSpace(intentId) ? "GeneralIntent" : intentId;
            StartTick = startTick;
            ExpiryTick = startTick + Math.Max(100, durationTicks);
            Priority = priority;
            Target = target;
            JobProvider = jobProvider;
            CompletionPredicate = completionPredicate;
        }

        public bool IsExpired(int currentTick) => currentTick >= ExpiryTick;

        public bool IsActive(Pawn pawn, int currentTick)
        {
            if (IsFinished) return false;
            if (IsExpired(currentTick)) return false;
            if (pawn == null || pawn.Dead || pawn.Downed) return false;

            if (CompletionPredicate != null)
            {
                try
                {
                    if (CompletionPredicate(pawn))
                    {
                        IsFinished = true;
                        return false;
                    }
                }
                catch
                {
                    // Fallback on predicate error: keep session active until expiry
                }
            }

            return true;
        }

        public Job? TryIssueNextJob(Pawn pawn)
        {
            if (IsFinished || JobProvider == null) return null;
            try
            {
                return JobProvider(pawn);
            }
            catch
            {
                return null;
            }
        }

        public void Complete()
        {
            IsFinished = true;
        }

        public void Cancel()
        {
            IsFinished = true;
        }
    }
}
