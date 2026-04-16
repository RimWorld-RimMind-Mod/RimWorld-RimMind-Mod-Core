using System;
using Verse;

namespace RimMind.Core.UI
{
    public class RequestEntry
    {
        public string source = "";
        public Pawn? pawn;
        public string title = "";
        public string? description;
        public string[] options = new string[0];
        public Action<string>? callback;
        public bool systemBlocked;
        public int tick;
        public int expireTicks;
    }
}
