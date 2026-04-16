using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using RimMind.Core.Client;
using RimMind.Core.Settings;
using Verse;

namespace RimMind.Core.Internal
{
    public class AIRequestQueue : GameComponent
    {
        private readonly ConcurrentQueue<(AIResponse response, Action<AIResponse> callback)> _results
            = new ConcurrentQueue<(AIResponse, Action<AIResponse>)>();

        private readonly ConcurrentQueue<(string msg, bool isWarning)> _pendingLogs
            = new ConcurrentQueue<(string, bool)>();

        private readonly Dictionary<string, int> _modCooldowns = new Dictionary<string, int>();

        private readonly Dictionary<string, Queue<PendingRequest>> _modQueues = new Dictionary<string, Queue<PendingRequest>>();

        private int _lastQueueProcessTick;

        private const int QueueProcessInterval = 60;

        private static AIRequestQueue? _instance;
        public static AIRequestQueue Instance => _instance!;

        public static void LogFromBackground(string msg, bool isWarning = false)
            => _instance?._pendingLogs.Enqueue((msg, isWarning));

        public AIRequestQueue(Game game)
        {
            _instance = this;
        }

        public override void StartedNewGame() { _modCooldowns.Clear(); ClearAllQueues(); }
        public override void LoadedGame() { _modCooldowns.Clear(); ClearAllQueues(); }

        public override void GameComponentTick()
        {
            while (_pendingLogs.TryDequeue(out var log))
            {
                if (log.isWarning) Log.Warning(log.msg);
                else               Log.Message(log.msg);
            }

            while (_results.TryDequeue(out var item))
            {
                try { item.callback?.Invoke(item.response); }
                catch (Exception ex)
                {
                    Log.Error($"[RimMind] Callback exception for {item.response.RequestId}: {ex}");
                }
            }

            int now = Find.TickManager.TicksGame;
            if (now - _lastQueueProcessTick >= QueueProcessInterval)
            {
                _lastQueueProcessTick = now;
                ProcessAllQueues(now);
            }
        }

        public void Enqueue(AIRequest request, Action<AIResponse> callback, IAIClient client)
        {
            string modId = !string.IsNullOrEmpty(request.ModId) ? request.ModId : "Unknown";
            var settings = RimMindCoreMod.Settings;

            if (!_modQueues.TryGetValue(modId, out var queue))
            {
                queue = new Queue<PendingRequest>();
                _modQueues[modId] = queue;
            }

            queue.Enqueue(new PendingRequest(request, callback, client));

            if (settings.debugLogging)
                Log.Message($"[RimMind][Core] Enqueued request {request.RequestId} for mod {modId}, queue depth={queue.Count}");

            int now = Find.TickManager.TicksGame;
            TryProcessModQueue(modId, now);
        }

        private void ProcessAllQueues(int now)
        {
            foreach (var kvp in _modQueues)
            {
                if (kvp.Value.Count > 0)
                    TryProcessModQueue(kvp.Key, now);
            }
        }

        private void TryProcessModQueue(string modId, int now)
        {
            if (!_modQueues.TryGetValue(modId, out var queue) || queue.Count == 0)
                return;

            if (_modCooldowns.TryGetValue(modId, out int nextAllowed) && now < nextAllowed)
                return;

            var settings = RimMindCoreMod.Settings;

            while (queue.Count > 0)
            {
                var pending = queue.Peek();

                if (pending.Request.ExpireAtTicks > 0 && now > pending.Request.ExpireAtTicks)
                {
                    queue.Dequeue();
                    if (settings.debugLogging)
                        Log.Message($"[RimMind][Core] Expired request {pending.Request.RequestId} skipped (enqueued at tick, expired at {pending.Request.ExpireAtTicks}, now={now})");
                    continue;
                }

                int cooldownTicks = GetModCooldownTicks(modId);
                _modCooldowns[modId] = now + cooldownTicks;

                var req = queue.Dequeue();

                if (settings.debugLogging)
                    Log.Message($"[RimMind][Core] Processing request {req.Request.RequestId} for mod {modId}, cooldown={cooldownTicks} ticks");

                Task.Run(async () =>
                {
                    var response = await req.Client.SendAsync(req.Request);
                    _results.Enqueue((response, req.Callback));
                });

                break;
            }
        }

        public void EnqueueImmediate(AIRequest request, Action<AIResponse> callback, IAIClient client)
        {
            var settings = RimMindCoreMod.Settings;
            if (settings.debugLogging)
                Log.Message($"[RimMind][Core] Immediate request {request.RequestId} for mod {request.ModId}, bypassing queue");

            Task.Run(async () =>
            {
                var response = await client.SendAsync(request);
                _results.Enqueue((response, callback));
            });
        }

        private int GetModCooldownTicks(string modId)
        {
            var getter = RimMindAPI.GetModCooldownGetter(modId);
            if (getter != null)
            {
                try { return getter(); }
                catch { }
            }
            return 3600;
        }

        public int GetCooldownTicksLeft(string modId)
        {
            if (!_modCooldowns.TryGetValue(modId, out int nextAllowed)) return 0;
            int left = nextAllowed - Find.TickManager.TicksGame;
            return left > 0 ? left : 0;
        }

        public int GetQueueDepth(string modId)
        {
            if (!_modQueues.TryGetValue(modId, out var queue)) return 0;
            return queue.Count;
        }

        public void ClearCooldown(string modId) => _modCooldowns.Remove(modId);

        public void ClearAllCooldowns() => _modCooldowns.Clear();

        public void ClearAllQueues()
        {
            foreach (var kvp in _modQueues)
                kvp.Value.Clear();
            _modQueues.Clear();
        }

        public IReadOnlyDictionary<string, int> GetAllCooldowns() => _modCooldowns;

        public IReadOnlyDictionary<string, int> GetAllQueueDepths()
        {
            var result = new Dictionary<string, int>();
            foreach (var kvp in _modQueues)
                result[kvp.Key] = kvp.Value.Count;
            return result;
        }

        private struct PendingRequest
        {
            public AIRequest Request;
            public Action<AIResponse> Callback;
            public IAIClient Client;

            public PendingRequest(AIRequest request, Action<AIResponse> callback, IAIClient client)
            {
                Request = request;
                Callback = callback;
                Client = client;
            }
        }
    }
}
