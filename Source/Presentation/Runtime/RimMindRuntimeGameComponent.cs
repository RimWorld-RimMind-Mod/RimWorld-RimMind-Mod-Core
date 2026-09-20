using System;
using System.Collections;
using System.Reflection;
using RimMind.Application.Common.Helpers;
using RimMind.Application.Common.Interfaces.Agent;
using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Application.Common.Interfaces.Npc;
using RimMind.Presentation.Runtime.Services;
using RimWorld;
using Verse;

namespace RimMind.Presentation.Runtime
{
    public class RimMindRuntimeGameComponent : GameComponent
    {
        private IAgentLoopScheduler? _scheduler;
        private IScopedAgentManager? _scopedAgentManager;
        private IOverlayService? _overlayService;
        private readonly Game _game;
        private int _lastTick = -1;
        private bool _initialized;

        public RimMindRuntimeGameComponent(Game game) : base()
        {
            _game = game ?? throw new ArgumentNullException(nameof(game));
        }

        private void EnsureInitialized()
        {
            if (!_initialized)
            {
                _initialized = true;
            }

            var scope = RuntimeServiceHub.Shared.Capture();
            _scheduler = scope.GetOptional<IAgentLoopScheduler>();
            _scopedAgentManager = scope.GetOptional<IScopedAgentManager>();
            _overlayService = scope.GetOptional<IOverlayService>();
        }

        public override void GameComponentTick()
        {
            base.GameComponentTick();
            EnsureInitialized();
            int now = Find.TickManager.TicksGame;
            if (now == _lastTick) return;
            _lastTick = now;
            _scheduler?.Tick(now);
            _overlayService?.Tick();
        }

        private static bool _apiKeyMissingNotified;

        public override void StartedNewGame()
        {
            base.StartedNewGame();
            EnsureInitialized();
            PublishGameServices();
            ResetRuntimeAgents();
            CheckMissingApiKeyNotification();
        }

        public override void LoadedGame()
        {
            base.LoadedGame();
            EnsureInitialized();
            PublishGameServices();
            ResetRuntimeAgents();
            CheckMissingApiKeyNotification();
        }

        private void CheckMissingApiKeyNotification()
        {
            if (_apiKeyMissingNotified) return;

            var scope = RuntimeServiceHub.Shared.Capture();
            var settings = scope.GetOptional<ISettingsProvider>();
            if (settings == null) return;

            if (AIProviderRegistry.RequiresApiKey(settings.Provider) && string.IsNullOrWhiteSpace(settings.ApiKey))
            {
                _apiKeyMissingNotified = true;
                LongEventHandler.ExecuteWhenFinished(() =>
                {
                    if (Find.LetterStack != null)
                    {
                        Find.LetterStack.ReceiveLetter(
                            "RimMind.Notification.ApiKeyMissing.Title".Translate(),
                            "RimMind.Notification.ApiKeyMissing.Desc".Translate(),
                            LetterDefOf.NeutralEvent);
                    }
                });
            }
        }

        private void PublishGameServices()
        {
            var npcManager = ResolveGameComponent<INpcManager>(_game);
            var aiDebugLog = ResolveGameComponent<IAIDebugLog>(_game);
            var builder = new GameServiceBuilder()
                .Bind<INpcManager>(npcManager)
                .Bind<IAIDebugLog>(aiDebugLog)
                .Require<INpcManager>()
                .Require<IAIDebugLog>();
            GameServiceHub.Shared.Publish(builder.Build());
        }

        private static T ResolveGameComponent<T>(Game game)
            where T : class
        {
            foreach (var field in typeof(Game).GetFields(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (!(field.GetValue(game) is IEnumerable values)) continue;
                foreach (var value in values)
                {
                    if (value is T service) return service;
                }
            }

            throw new InvalidOperationException(
                $"{typeof(T).Name} has not been created for the current game.");
        }

        internal static void StopGameServices()
        {
            GameServiceHub.Shared.Stop();
        }

        private void ResetRuntimeAgents()
        {
            try
            {
                _scopedAgentManager?.Clear();
            }
            catch (Exception ex)
            {
                Log.Error($"[RimMind-Core] Failed to clear runtime scoped agents: {ex}");
            }

            try
            {
                _overlayService?.Clear();
            }
            catch (Exception ex)
            {
                Log.Error($"[RimMind-Core] Failed to clear pending requests: {ex}");
            }
            finally
            {
                try
                {
                    _scheduler?.Clear();
                }
                finally
                {
                    _lastTick = -1;
                }
            }
        }
    }
}
