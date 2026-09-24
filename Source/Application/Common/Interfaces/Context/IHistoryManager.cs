using System.Collections.Generic;
using RimMind.Domain.ValueObjects;

namespace RimMind.Application.Common.Interfaces.Context
{
    public interface IHistoryManager
    {
        void AddTurn(string npcId, string userMessage, string assistantMessage, string? scenario = null);
        void AddPendingTurn(
            string npcId,
            string turnId,
            string userMessage,
            string assistantPlaceholder,
            string? scenario = null);
        List<(string role, string content)> GetHistory(string npcId, int maxRounds, string? scenario = null);
        List<(string role, string content)> GetHistoryForDisplay(string npcId, int maxRounds, string? scenario = null);
        int GetHistoryCount(string npcId);
        void ClearHistory(string npcId);
        void CompressIfNeeded(string npcId);
        void ReplaceLastAssistantTurn(string npcId, string content);
        bool ReplaceAssistantTurn(string npcId, string turnId, string content);
        bool RemoveTurn(string npcId, string turnId);
        string GetAllForSave();
        Dictionary<string, List<HistoryEntry>> GetAllForSaveDict();
        void LoadFromSave(Dictionary<string, List<HistoryEntry>> data);
    }
}
