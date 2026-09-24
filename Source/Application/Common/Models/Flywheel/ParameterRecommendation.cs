namespace RimMind.Application.Common.Models.Flywheel
{
    public class ParameterRecommendation
    {
        public string Target = null!;
        public float CurrentValue;
        public float RecommendedValue;
        public float Confidence;
        public string Reason = null!;
    }
}
