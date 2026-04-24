using System.Collections.Generic;
using Verse;

namespace RimMind.Core.Npc
{
    public enum NpcType
    {
        Pawn = 0,
        Storyteller = 1,
        Map = 2
    }

    public class NpcProfile : IExposable
    {
        public string NpcId = "";
        public NpcType Type;
        public string Name = "";
        public string ShortName = "";
        public string CharacterDescription = "";
        public string SystemPrompt = "";
        public List<NpcCommand> Commands = new List<NpcCommand>();
        public NpcTtsConfig? TtsConfig;

        public void ExposeData()
        {
            Scribe_Values.Look(ref NpcId, "npcId");
            Scribe_Values.Look(ref Type, "type");
            Scribe_Values.Look(ref Name, "name");
            Scribe_Values.Look(ref ShortName, "shortName");
            Scribe_Values.Look(ref CharacterDescription, "charDesc");
            Scribe_Values.Look(ref SystemPrompt, "sysPrompt");
            Scribe_Collections.Look(ref Commands, "commands", LookMode.Deep);
            Commands ??= new List<NpcCommand>();
            Scribe_Deep.Look(ref TtsConfig, "ttsConfig");
        }
    }

    public class NpcCommand : IExposable
    {
        public string Name = "";
        public string Description = "";
        public string? Parameters;
        public bool NeverRespondWithMessage;

        public void ExposeData()
        {
            Scribe_Values.Look(ref Name, "name");
            Scribe_Values.Look(ref Description, "desc");
            Scribe_Values.Look(ref Parameters, "params");
            Scribe_Values.Look(ref NeverRespondWithMessage, "neverRespond", false);
        }
    }

    public class NpcTtsConfig : IExposable
    {
        public List<string> VoiceIds = new List<string>();
        public float Speed = 1.0f;
        public string AudioFormat = "mp3";

        public void ExposeData()
        {
            Scribe_Collections.Look(ref VoiceIds, "voiceIds");
            Scribe_Values.Look(ref Speed, "speed", 1.0f);
            Scribe_Values.Look(ref AudioFormat, "audioFormat", "mp3");
            VoiceIds ??= new List<string>();
        }
    }

    public class NpcChatResult
    {
        public string Message = "";
        public List<NpcCommandResult> Commands = new List<NpcCommandResult>();
        public byte[]? Audio;
        public string? AudioUrl;
        public string? Error;
    }

    public class NpcCommandResult
    {
        public string Name = "";
        public string? Arguments;
    }
}
