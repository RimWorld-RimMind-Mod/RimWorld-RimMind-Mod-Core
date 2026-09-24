using RimMind.Domain.Agent.Psychology;
using RimMind.Domain.ValueObjects;

namespace RimMind.Application.Common.Interfaces.Agent.Psychology;

/// <summary>
/// AI Thought 注入器——将 AI 生成的动态 Thought 注入到 RimWorld 的 Thought 系统
/// </summary>
public interface IThoughtInjector
{
    /// <summary>注入一个 AI 动态 Thought 到指定小人</summary>
    Result<RimMindDynamicThought, RimMindError> InjectThought(
        int pawnId,
        string thoughtText,
        float moodOffset,
        int durationTicks,
        string source);
}
