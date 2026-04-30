using System.Collections.Generic;
using Verse;

namespace RimMind.Core.Agent
{
    public class AgentIdentity : IExposable
    {
        public List<string> Motivations = new List<string>();
        public List<string> PersonalityTraits = new List<string>();
        public List<string> CoreValues = new List<string>();

        public void ExposeData()
        {
            Scribe_Collections.Look(ref Motivations, "motivations", LookMode.Value);
            Scribe_Collections.Look(ref PersonalityTraits, "personalityTraits", LookMode.Value);
            Scribe_Collections.Look(ref CoreValues, "coreValues", LookMode.Value);
            Motivations ??= new List<string>();
            PersonalityTraits ??= new List<string>();
            CoreValues ??= new List<string>();
        }
    }
}
