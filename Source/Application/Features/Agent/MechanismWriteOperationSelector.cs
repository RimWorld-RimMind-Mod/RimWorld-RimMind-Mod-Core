using System.Collections.Generic;
using RimMind.Domain.Enums;

namespace RimMind.Application.Features.Agent
{
    /// <summary>
    /// Single source of truth for which write operation a mechanism-style agent intent
    /// (e.g. <c>pawn.job.force_rest</c>) invokes. Used both to execute the intent and to
    /// assess its risk, so the approval gate always judges the operation that will really run.
    /// </summary>
    public static class MechanismWriteOperationSelector
    {
        private static readonly MechanismOperationType[] FallbackOrder =
        {
            MechanismOperationType.Trigger,
            MechanismOperationType.Set,
            MechanismOperationType.Add,
            MechanismOperationType.Toggle,
            MechanismOperationType.Remove,
        };

        /// <summary>
        /// Returns <paramref name="preferred"/> when supported, otherwise the first supported operation in
        /// Trigger → Set → Add → Toggle → Remove order, or null when the mechanism has no write operation.
        /// </summary>
        public static MechanismOperationType? Select(
            IReadOnlyList<MechanismOperationType>? supported,
            MechanismOperationType preferred)
        {
            if (supported == null || supported.Count == 0) return null;
            if (Contains(supported, preferred)) return preferred;

            for (int i = 0; i < FallbackOrder.Length; i++)
            {
                if (Contains(supported, FallbackOrder[i])) return FallbackOrder[i];
            }

            return null;
        }

        private static bool Contains(IReadOnlyList<MechanismOperationType> supported, MechanismOperationType op)
        {
            for (int i = 0; i < supported.Count; i++)
            {
                if (supported[i] == op) return true;
            }
            return false;
        }
    }
}
