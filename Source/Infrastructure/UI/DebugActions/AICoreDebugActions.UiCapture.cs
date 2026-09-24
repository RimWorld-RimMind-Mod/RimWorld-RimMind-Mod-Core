using LudeonTK;
using RimMind.Infrastructure.UI.Layout;

namespace RimMind.Infrastructure.UI
{
    public static partial class RimMindCoreDebugActions
    {
        [DebugAction("RimMind", "Capture Core UI pages (read-only)", actionType = DebugActionType.Action)]
        public static void CaptureCoreUiPages() => UiCaptureRunner.StartCapture();
    }
}
