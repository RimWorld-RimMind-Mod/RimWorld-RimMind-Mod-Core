using RimWorld;
using Verse;

namespace RimMind.Infrastructure.Psychology
{
    /// <summary>
    /// Custom Thought_Memory that returns a dynamic mood offset set by AI.
    /// Associated with RimMind_DynamicThought ThoughtDef.
    /// </summary>
    public class Thought_RimMindDynamic : Thought_Memory
    {
        public float MoodOffsetValue { get; set; } = 0f;

        public override float MoodOffset()
        {
            return MoodOffsetValue;
        }
    }
}
