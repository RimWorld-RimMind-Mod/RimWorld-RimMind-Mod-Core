using RimMind.Application.Common.Interfaces.Agent;

namespace RimMind.Application.Common.Interfaces.Agent.Psychology;

/// <summary>
/// 心理状态观察者——监控小人的心情/需求/精神状态变化，发布对应事件
/// </summary>
public interface IPsychologyWatcher
{
    /// <summary>检查并发布心理状态变化事件</summary>
    void CheckAndPublish(IAgentInfo agent, int pawnId);

    /// <summary>是否存在该 NPC 的未处理紧急心理事件</summary>
    bool HasUrgentEvent(string npcId);
}
