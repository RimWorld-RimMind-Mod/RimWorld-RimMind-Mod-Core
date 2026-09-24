namespace RimMind.Application.Common.Interfaces
{
    public interface IResponseDispatcher
    {
        void DispatchChatResponse(string npcId, string requestId);
    }
}
