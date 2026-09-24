namespace RimMind.Application.Common.Interfaces.UI;

public interface IWindowService
{
    void OpenAgentDialogue(object pawn);
    void OpenRequestLog();
    void OpenAIRequests();
    void OpenUpgradeWarning();
}
