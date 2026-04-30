using System.Collections.Generic;
using RimMind.Core.Npc;
using Verse;

namespace RimMind.Core.Context
{
    public class HistoryGameComponent : GameComponent
    {
        private Dictionary<string, List<HistoryEntry>> _histories = new Dictionary<string, List<HistoryEntry>>();
        private Dictionary<string, string> _kvStore = new Dictionary<string, string>();

        public HistoryGameComponent() : base() { }
        public HistoryGameComponent(Game game) : base() { }

        public override void ExposeData()
        {
            base.ExposeData();

            if (Scribe.mode == LoadSaveMode.Saving)
                _histories = HistoryManager.Instance.GetAllForSave();

            Scribe_Collections.Look(ref _histories, "contextHistories",
                LookMode.Value, LookMode.Deep);
            _histories ??= new Dictionary<string, List<HistoryEntry>>();

            if (Scribe.mode == LoadSaveMode.LoadingVars)
                HistoryManager.Instance.LoadFromSave(_histories);

            if (Scribe.mode == LoadSaveMode.Saving)
                _kvStore = new Dictionary<string, string>(LocalStorageDriver.KvStore);

            Scribe_Collections.Look(ref _kvStore, "kvStore", LookMode.Value, LookMode.Value);
            _kvStore ??= new Dictionary<string, string>();

            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                LocalStorageDriver.KvStore.Clear();
                foreach (var kvp in _kvStore)
                    LocalStorageDriver.KvStore[kvp.Key] = kvp.Value;
            }
        }
    }
}
