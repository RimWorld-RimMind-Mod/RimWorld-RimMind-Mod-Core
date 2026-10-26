using RimWorld;
using Verse;

namespace RimMind.Infrastructure.Psychology
{
    /// <summary>
    /// Custom Thought_Memory that returns a dynamic mood offset set by AI.
    /// Associated with RimMind_DynamicThought ThoughtDef.
    /// </summary>
    /// <remarks>
    /// Mood offset, AI text and duration override are persisted. Saves made before these fields
    /// existed load with defaults (offset 0, def label/description, def duration).
    /// </remarks>
    public class Thought_RimMindDynamic : Thought_Memory
    {
        /// <summary>Longest AI text shown as the thought label; the full text stays in the description.</summary>
        internal const int MaxLabelLength = 48;

        private float moodOffsetValue;
        private string? thoughtText;
        private int customDurationTicks = -1;

        public float MoodOffsetValue
        {
            get => moodOffsetValue;
            set => moodOffsetValue = value;
        }

        /// <summary>AI-generated thought text; null/empty falls back to the ThoughtDef label and description.</summary>
        public string? ThoughtText
        {
            get => thoughtText;
            set => thoughtText = value;
        }

        /// <summary>Duration override in ticks; values &lt;= 0 use the ThoughtDef duration.</summary>
        public int CustomDurationTicks
        {
            get => customDurationTicks;
            set => customDurationTicks = value > 0 ? value : -1;
        }

        public override float MoodOffset()
        {
            return moodOffsetValue;
        }

        public override int DurationTicks
            => customDurationTicks > 0 ? customDurationTicks : base.DurationTicks;

        public override string LabelCap
        {
            get
            {
                string? label = BuildLabel(thoughtText);
                return label.NullOrEmpty() ? base.LabelCap : label!.CapitalizeFirst();
            }
        }

        public override string Description
            => thoughtText.NullOrEmpty() ? base.Description : thoughtText!;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref moodOffsetValue, "rimmindMoodOffset", 0f);
            Scribe_Values.Look(ref thoughtText, "rimmindThoughtText", null);
            Scribe_Values.Look(ref customDurationTicks, "rimmindDurationTicks", -1);
        }

        /// <summary>First line of the AI text, trimmed and shortened to <see cref="MaxLabelLength"/>.</summary>
        internal static string? BuildLabel(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            string line = text!.Trim();
            int newline = line.IndexOfAny(new[] { '\r', '\n' });
            if (newline > 0) line = line.Substring(0, newline).TrimEnd();
            if (line.Length > MaxLabelLength) line = line.Substring(0, MaxLabelLength - 1).TrimEnd() + "…";
            return line;
        }
    }
}
