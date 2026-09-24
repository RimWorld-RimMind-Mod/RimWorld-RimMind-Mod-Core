using System;
using System.Collections.Generic;

namespace RimMind.Application.Common.Models.Npc
{
    public class NpcCommand
    {
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string[] Parameters { get; set; } = Array.Empty<string>();
        public bool NeverRespondWithMessage { get; set; }

        public NpcCommand() { }

        public NpcCommand(string name, string description)
        {
            Name = name;
            Description = description;
        }
    }

    public class TtsConfig
    {
        public string[] VoiceIds { get; set; } = Array.Empty<string>();
        public float Speed { get; set; } = 1.0f;
        public string AudioFormat { get; set; } = "mp3";
    }

    public class NpcProfile
    {
        public string NpcId { get; set; } = "";
        public int PawnId { get; set; }
        public string Name { get; set; } = "";
        public string ShortName { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string Backstory { get; set; } = "";
        public string CharacterDescription { get; set; } = "";
        public string SystemPrompt { get; set; } = "";
        public string Personality { get; set; } = "";
        public string SpeakingStyle { get; set; } = "";
        public string? AvatarUrl { get; set; }
        public List<NpcCommand> Commands { get; set; } = new List<NpcCommand>();
        public TtsConfig? TtsConfig { get; set; }
        public Dictionary<string, string> Extra { get; set; } = new Dictionary<string, string>();

        public NpcProfile() { }

        public NpcProfile(string npcId, int pawnId, string displayName, string backstory = "")
        {
            NpcId = npcId;
            PawnId = pawnId;
            DisplayName = displayName;
            Backstory = backstory;
        }
    }

}
