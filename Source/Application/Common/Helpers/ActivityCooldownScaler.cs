using System;

namespace RimMind.Application.Common.Helpers
{
    /// <summary>
    /// Shared activity-frequency cooldown scaling formula used across RimMind modules.
    /// Higher <c>activityFrequencyScale</c> produces a shorter cooldown (multiplier = clamp(1 / scale, 0.35, 3.5)).
    /// </summary>
    public static class ActivityCooldownScaler
    {
        public const float MinMultiplier = 0.35f;
        public const float MaxMultiplier = 3.5f;

        public static int Scale(int baseCooldownTicks, float activityFrequencyScale, int minFloorTicks = 0)
        {
            if (baseCooldownTicks <= 0)
                return minFloorTicks > 0 ? Math.Max(minFloorTicks, baseCooldownTicks) : baseCooldownTicks;

            int scaled;
            if (activityFrequencyScale <= 0.01f)
            {
                scaled = baseCooldownTicks;
            }
            else
            {
                float multiplier = 1.0f / activityFrequencyScale;
                if (multiplier < MinMultiplier) multiplier = MinMultiplier;
                else if (multiplier > MaxMultiplier) multiplier = MaxMultiplier;
                scaled = (int)Math.Round(baseCooldownTicks * multiplier);
            }

            return minFloorTicks > 0 ? Math.Max(minFloorTicks, scaled) : scaled;
        }
    }
}
