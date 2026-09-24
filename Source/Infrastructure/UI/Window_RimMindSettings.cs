using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Presentation.Runtime.Services;
using RimMind.Presentation.UI.Layout;
using RimMind.Presentation.UI;
using UnityEngine;
using Verse;

namespace RimMind.Infrastructure.UI
{
    public class Window_RimMindSettings : RimMindWindowBase
    {
        private readonly bool _queueReference;
        private readonly bool _bottom;
        private readonly RuntimeServiceRef<ISettingsProvider> _settingsProvider =
            RuntimeServiceRef<ISettingsProvider>.Required();

        public override Vector2 InitialSize => new Vector2(800f, 600f);

        public Window_RimMindSettings()
        {
            forcePause = false;
            closeOnClickedOutside = true;
            absorbInputAroundWindow = false;
            doCloseX = true;
        }

        internal Window_RimMindSettings(bool bottom) : this()
        {
            _queueReference = true;
            _bottom = bottom;
        }

        protected override void DrawContents(Rect inRect, RimMindLayoutScope scope)
        {
            scope.Record(inRect, "Settings:Body");
            if (_queueReference)
                RimMindCoreSettingsUI.DrawQueueReference(inRect, scope, _bottom);
            else
                RimMindCoreSettingsUI.Draw(inRect, scope);
        }

        public override void PreClose()
        {
            if (!_queueReference)
                _settingsProvider.Value.Persist();
            base.PreClose();
        }
    }
}
