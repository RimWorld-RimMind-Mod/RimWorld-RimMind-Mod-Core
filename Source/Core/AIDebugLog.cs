using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using RimMind.Core.Client;
using Verse;

namespace RimMind.Core.Internal
{
    public class AIDebugLog : GameComponent
    {
        private const int MaxEntries = 200;

        private readonly List<AIDebugEntry> _entries = new List<AIDebugEntry>(MaxEntries);
        private readonly ConcurrentQueue<AIDebugEntry> _pendingEntries = new ConcurrentQueue<AIDebugEntry>();

        private static AIDebugLog? _instance;
        public static AIDebugLog? Instance => _instance;

        public AIDebugLog(Game game)
        {
            _instance = this;
        }

        public IReadOnlyList<AIDebugEntry> Entries => _entries;

        public override void GameComponentTick()
        {
            while (_pendingEntries.TryDequeue(out var entry))
            {
                if (_entries.Count >= MaxEntries)
                    _entries.RemoveAt(0);
                _entries.Add(entry);
            }
        }

        public void Clear() => _entries.Clear();

        public static void Record(AIRequest request, AIResponse response, int elapsedMs)
        {
            _instance?._pendingEntries.Enqueue(new AIDebugEntry
            {
                GameTick          = Find.TickManager.TicksGame,
                Source            = request.RequestId,
                ModelName         = RimMindCoreMod.Settings.modelName,
                FullSystemPrompt  = request.SystemPrompt,
                FullUserPrompt    = request.Messages != null
                    ? Newtonsoft.Json.JsonConvert.SerializeObject(request.Messages, Newtonsoft.Json.Formatting.Indented)
                    : request.UserPrompt,
                FullResponse      = response.Content,
                ElapsedMs         = elapsedMs,
                TokensUsed        = response.TokensUsed,
                IsError           = !response.Success,
                ErrorMsg          = response.Error,
            });
        }
    }

    public class AIDebugEntry
    {
        public int    GameTick         { get; set; }
        public string Source           { get; set; } = string.Empty;
        public string ModelName        { get; set; } = string.Empty;
        public string FullSystemPrompt { get; set; } = string.Empty;
        public string FullUserPrompt   { get; set; } = string.Empty;
        public string FullResponse     { get; set; } = string.Empty;
        public int    ElapsedMs        { get; set; }
        public int    TokensUsed       { get; set; }
        public bool   IsError          { get; set; }
        public string ErrorMsg         { get; set; } = string.Empty;

        /// <summary>格式化为游戏时间形式。</summary>
        public string FormattedTime
        {
            get
            {
                int day  = GameTick / 60000 + 1;
                int hour = (GameTick % 60000) / 2500;
                int min  = ((GameTick % 60000) % 2500) * 60 / 2500;
                return "RimMind.Core.Prompt.Time.Format".Translate(day, $"{hour:D2}", $"{min:D2}");
            }
        }
    }
}
