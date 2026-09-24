using System;

namespace RimMind.Application.Features.Context
{
    public static class ThreatClassifier
    {
        public static string ClassifyThreatTier(float wealth, float high, float medium, float low, float threatScale = 1f)
        {
            float adjusted = wealth * threatScale;
            if (adjusted >= high) return "Extreme";
            if (adjusted >= medium) return "High";
            if (adjusted >= low) return "Medium";
            return "Low";
        }
    }
}
