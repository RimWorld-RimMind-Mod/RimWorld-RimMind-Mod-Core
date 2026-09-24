using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using RimMind.Application.Common.Interfaces.Context;
using RimMind.Domain.ValueObjects;
using RimMind.Presentation.Runtime.Services;
using Verse;

namespace RimMind.Infrastructure.Verse
{
    public sealed class HistoryManagerGameComponent : GameComponent
    {
        private const string EmptyHistory = "{}";
        private readonly RuntimeServiceRef<IHistoryManager> _historyManager;
        private string _serializedHistory = EmptyHistory;
        private bool _restorePending;

        public HistoryManagerGameComponent(Game game)
            : this(game, RuntimeServiceRef<IHistoryManager>.Optional())
        {
        }

        internal HistoryManagerGameComponent(
            Game game,
            RuntimeServiceRef<IHistoryManager> historyManager)
            : base()
        {
            _historyManager = historyManager
                ?? throw new ArgumentNullException(nameof(historyManager));
        }

        public override void ExposeData()
        {
            base.ExposeData();
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                IHistoryManager? manager = _historyManager.ValueOrDefault;
                if (manager != null)
                    _serializedHistory = manager.GetAllForSave();
                Scribe_Values.Look(ref _serializedHistory, "histories", EmptyHistory);
            }
            else if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                _serializedHistory = EmptyHistory;
                Scribe_Values.Look(ref _serializedHistory, "histories", EmptyHistory);
                _restorePending = true;
                TryRestore();
            }
            else if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                TryRestore();
            }
        }

        public override void LoadedGame()
        {
            base.LoadedGame();
            TryRestore();
        }

        public override void GameComponentTick()
        {
            base.GameComponentTick();
            if (_restorePending)
                TryRestore();
        }

        private void TryRestore()
        {
            if (!_restorePending)
                return;

            IHistoryManager? manager = _historyManager.ValueOrDefault;
            if (manager == null)
                return;

            try
            {
                var data = JsonConvert.DeserializeObject<Dictionary<string, List<HistoryEntry>>>(
                    _serializedHistory ?? EmptyHistory)
                    ?? new Dictionary<string, List<HistoryEntry>>();
                manager.LoadFromSave(data);
            }
            catch (JsonException exception)
            {
                Log.Error($"[RimMind-Core] Failed to restore conversation history: {exception.GetType().Name}");
            }
            finally
            {
                _restorePending = false;
                _serializedHistory = EmptyHistory;
            }
        }
    }
}
