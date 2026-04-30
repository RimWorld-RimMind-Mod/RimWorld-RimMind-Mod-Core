using System.Collections.Generic;
using RimMind.Core.Client;

namespace RimMind.Core.Context
{
    public class ContextSnapshot
    {
        public string NpcId = null!;
        public string Scenario = "";
        public List<ChatMessage> Messages = new List<ChatMessage>();
        public List<StructuredTool>? Tools;
        public int EstimatedTokens;
        public ContextLayerMeta Meta = new ContextLayerMeta();
        public int MaxTokens = 400;
        public float Temperature = 0.7f;
        public string? CurrentQuery;
        public string[] IncludedKeys = new string[0];
        public string[] TrimmedKeys = new string[0];
        public float BudgetValue;
        public Dictionary<string, bool> CacheHitEvents = new Dictionary<string, bool>();
        public Dictionary<string, int> KeyChangeCounts = new Dictionary<string, int>();
        public Dictionary<string, float> KeyScores = new Dictionary<string, float>();
        public int DiffCount;
        public long BuildStartTicks;
        public Dictionary<string, long> LatencyByLayerMs = new Dictionary<string, long>();
    }

    public class ContextLayerMeta
    {
        public int L0Tokens;
        public int L1Tokens;
        public int L2Tokens;
        public int L3Tokens;
        public int L4Tokens;
        public int L5Tokens;
        public int TotalTokens;
    }
}
