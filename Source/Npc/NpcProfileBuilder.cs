using System.Collections.Generic;
using RimMind.Core.Npc;
using Verse;

namespace RimMind.Core.Npc
{
    public static class NpcProfileBuilder
    {
        public static NpcProfile BuildPawnNpc(Pawn pawn)
        {
            string npcId = $"NPC-{pawn.thingIDNumber}";

            var profile = new NpcProfile
            {
                NpcId = npcId,
                Type = NpcType.Pawn,
                Name = pawn.Name?.ToStringFull ?? pawn.LabelShort,
                ShortName = pawn.Name?.ToStringShort ?? pawn.LabelShort,
                CharacterDescription = BuildPawnDescription(pawn),
                SystemPrompt = "RimMind.Core.Prompt.SystemInstruction.Pawn".Translate(pawn.Name?.ToStringShort ?? "a colonist"),
                Commands = new List<NpcCommand>
                {
                    new NpcCommand { Name = "express_emotion", Description = "RimMind.Core.Prompt.Command.ExpressEmotion".Translate(), Parameters = "{\"emotion\":\"string\",\"intensity\":\"float\"}" },
                    new NpcCommand { Name = "change_relationship", Description = "RimMind.Core.Prompt.Command.ChangeRelationship".Translate(), Parameters = "{\"target_name\":\"string\",\"delta\":\"float\"}", NeverRespondWithMessage = true },
                },
            };

            return profile;
        }

        public static NpcProfile BuildStorytellerNpc()
        {
            var storyteller = Find.Storyteller;
            string name = storyteller?.def?.label ?? "Storyteller";

            var profile = new NpcProfile
            {
                NpcId = "NPC-storyteller",
                Type = NpcType.Storyteller,
                Name = name,
                ShortName = name,
                CharacterDescription = $"You are the {name}, the narrator and director of events in this RimWorld colony. You decide what challenges and opportunities the colonists face.",
                SystemPrompt = "You are the storyteller, orchestrating events and narrative arcs for the colony.",
                Commands = new List<NpcCommand>
                {
                    new NpcCommand { Name = "trigger_incident", Description = "Trigger an incident", Parameters = "{\"defName\":\"string\",\"reason\":\"string\"}", NeverRespondWithMessage = true },
                    new NpcCommand { Name = "adjust_threat", Description = "Adjust threat level", Parameters = "{\"direction\":\"up|down\"}", NeverRespondWithMessage = true },
                    new NpcCommand { Name = "set_weather", Description = "Set weather", Parameters = "{\"defName\":\"string\"}", NeverRespondWithMessage = true },
                },
                TtsConfig = new NpcTtsConfig
                {
                    VoiceIds = new List<string> { "default" },
                    Speed = 1.0f,
                    AudioFormat = "mp3",
                },
            };

            return profile;
        }

        public static NpcProfile BuildMapNpc(Map map)
        {
            string npcId = $"map-{map.uniqueID}";
            string mapName = map.info?.parent?.Label ?? "Colony Map";

            var profile = new NpcProfile
            {
                NpcId = npcId,
                Type = NpcType.Map,
                Name = mapName,
                ShortName = mapName,
                CharacterDescription = $"You are the world spirit of {mapName}, orchestrating local events and narrative tension on this map.",
                SystemPrompt = "You are the world spirit of this map, orchestrating local events and narrative tension.",
                Commands = new List<NpcCommand>
                {
                    new NpcCommand { Name = "trigger_incident", Description = "Trigger an incident", Parameters = "{\"defName\":\"string\",\"reason\":\"string\"}", NeverRespondWithMessage = true },
                    new NpcCommand { Name = "adjust_threat", Description = "Adjust threat level", Parameters = "{\"direction\":\"up|down\"}", NeverRespondWithMessage = true },
                    new NpcCommand { Name = "set_weather", Description = "Set weather", Parameters = "{\"defName\":\"string\"}", NeverRespondWithMessage = true },
                },
            };

            return profile;
        }

        private static string BuildPawnDescription(Pawn pawn)
        {
            if (pawn == null) return "RimMind.Core.Prompt.Unknown".Translate();
            var sb = new System.Text.StringBuilder();
            if (pawn.story?.Adulthood != null)
                sb.AppendLine("RimMind.Core.Prompt.PawnDesc.Backstory".Translate(pawn.story.Adulthood.title));
            if (pawn.story?.traits?.allTraits != null)
            {
                var traitNames = new List<string>();
                foreach (var t in pawn.story.traits.allTraits)
                    traitNames.Add(t.Label);
                if (traitNames.Count > 0)
                    sb.AppendLine("RimMind.Core.Prompt.PawnDesc.Traits".Translate(string.Join(", ", traitNames)));
            }
            if (pawn.skills?.skills != null)
            {
                var topSkills = new List<string>();
                foreach (var s in pawn.skills.skills)
                    if (s.Level >= 10) topSkills.Add($"{s.def.label}:{s.Level}");
                if (topSkills.Count > 0)
                    sb.AppendLine("RimMind.Core.Prompt.PawnDesc.Skills".Translate(string.Join(", ", topSkills)));
            }
            return sb.ToString().TrimEnd();
        }
    }
}
