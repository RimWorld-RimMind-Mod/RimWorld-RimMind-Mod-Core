using RimMind.Application.Common.Interfaces.UI;
using Verse;

namespace RimMind.Infrastructure.UI;

public class WindowService : IWindowService
{
    public void OpenAgentDialogue(object pawn)
    {
        Find.WindowStack.Add(new Window_AgentDialogue(pawn as Pawn));
    }

    public void OpenRequestLog()
    {
        Find.WindowStack.Add(new Window_RequestLog());
    }

    public void OpenAIRequests()
    {
        Find.WindowStack.Add(Window_RimMindHub.OpenAIRequests());
    }

    public void OpenUpgradeWarning()
    {
        Find.WindowStack.Add(new Dialog_MessageBox(
            "RimMind.UpgradeWarning".Translate(),
            "OK".Translate(),
            null));
    }
}
