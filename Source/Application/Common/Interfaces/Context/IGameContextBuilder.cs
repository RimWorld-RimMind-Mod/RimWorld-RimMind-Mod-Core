namespace RimMind.Application.Common.Interfaces.Context
{
    public interface IGameContextBuilder
    {
        string CollectBasicGameState(string npcId);
        string BuildMapContextInstance(object map, bool brief);
    }
}
