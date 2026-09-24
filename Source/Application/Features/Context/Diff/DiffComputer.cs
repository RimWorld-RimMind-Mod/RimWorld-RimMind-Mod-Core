using System.Collections.Generic;
using RimMind.Domain.ValueObjects;

namespace RimMind.Application.Features.Context.Diff
{
    /// <summary>
    /// Pure function: compares old and new L1 baseline values, computes diff entries.
    /// </summary>
    internal sealed class DiffComputer
    {
        /// <summary>
        /// Compute diffs between old and new key values.
        /// </summary>
        /// <param name="oldValues">Previous key-value snapshot</param>
        /// <param name="newValues">Current key-value snapshot</param>
        /// <param name="layer">The context layer for the diffs</param>
        /// <returns>List of detected changes</returns>
        public List<ContextDiff> Compute(
            Dictionary<string, string> oldValues,
            Dictionary<string, string> newValues,
            ContextLayer layer)
        {
            var diffs = new List<ContextDiff>();
            if (oldValues == null || newValues == null) return diffs;

            foreach (var kvp in newValues)
            {
                if (!oldValues.TryGetValue(kvp.Key, out var oldValue) || oldValue != kvp.Value)
                {
                    diffs.Add(new ContextDiff
                    {
                        Key = kvp.Key,
                        OldValue = oldValue ?? string.Empty,
                        NewValue = kvp.Value,
                        Layer = layer
                    });
                }
            }

            // Detect removed keys
            foreach (var kvp in oldValues)
            {
                if (!newValues.ContainsKey(kvp.Key))
                {
                    diffs.Add(new ContextDiff
                    {
                        Key = kvp.Key,
                        OldValue = kvp.Value,
                        NewValue = string.Empty,
                        Layer = layer
                    });
                }
            }

            return diffs;
        }
    }
}
