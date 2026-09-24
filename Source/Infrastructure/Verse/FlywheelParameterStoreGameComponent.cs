using System.Collections.Generic;
using RimMind.Application.Common.Interfaces;
using RimMind.Application.Common.Interfaces.Flywheel;
using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Presentation.Runtime.Services;
using Verse;

namespace RimMind.Infrastructure.Verse
{
    public class FlywheelParameterStoreGameComponent : GameComponent
    {
        private readonly RuntimeServiceRef<IFlywheelParameterStore> _store =
            RuntimeServiceRef<IFlywheelParameterStore>.Required();

        public FlywheelParameterStoreGameComponent() : base() { }
        public FlywheelParameterStoreGameComponent(global::Verse.Game game) : base() { }

        public IFlywheelParameterStore Store => _store.Value;

        public override void ExposeData()
        {
            base.ExposeData();
            var store = Store;
            if (global::Verse.Scribe.mode == global::Verse.LoadSaveMode.Saving)
            {
                var (keys, values) = store.GetSaveSnapshot();
                global::Verse.Scribe_Collections.Look(ref keys, "paramKeys");
                global::Verse.Scribe_Collections.Look(ref values, "paramValues");

                var (overrideKeys, overrideValues) = store.GetKeyPriorityOverridesSnapshot();
                global::Verse.Scribe_Collections.Look(ref overrideKeys, "overrideKeys");
                global::Verse.Scribe_Collections.Look(ref overrideValues, "overrideValues");

                var (feedbackKeys, feedbackValues) = store.GetKeyFeedbackScoresSnapshot();
                global::Verse.Scribe_Collections.Look(ref feedbackKeys, "feedbackKeys");
                global::Verse.Scribe_Collections.Look(ref feedbackValues, "feedbackValues");
            }
            else if (global::Verse.Scribe.mode == global::Verse.LoadSaveMode.LoadingVars)
            {
                var keys = new List<string>();
                var values = new List<float>();
                global::Verse.Scribe_Collections.Look(ref keys, "paramKeys");
                global::Verse.Scribe_Collections.Look(ref values, "paramValues");
                store.LoadFromSnapshot(keys, values);

                var overrideKeys = new List<string>();
                var overrideValues = new List<float>();
                global::Verse.Scribe_Collections.Look(ref overrideKeys, "overrideKeys");
                global::Verse.Scribe_Collections.Look(ref overrideValues, "overrideValues");
                store.LoadKeyPriorityOverridesSnapshot(overrideKeys, overrideValues);

                var feedbackKeys = new List<string>();
                var feedbackValues = new List<float>();
                global::Verse.Scribe_Collections.Look(ref feedbackKeys, "feedbackKeys");
                global::Verse.Scribe_Collections.Look(ref feedbackValues, "feedbackValues");
                store.LoadKeyFeedbackScoresSnapshot(feedbackKeys, feedbackValues);
            }
        }

        public override void FinalizeInit()
        {
            base.FinalizeInit();
        }
    }
}
