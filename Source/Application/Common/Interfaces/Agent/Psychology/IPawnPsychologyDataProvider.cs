using System.Collections.Generic;
using RimMind.Domain.Agent.Psychology;

namespace RimMind.Application.Common.Interfaces.Agent.Psychology;

/// <summary>
/// 小人心理数据提供者——提供心情/需求/精神状态数据，避免 Application 层直接依赖 Verse
/// </summary>
public interface IPawnPsychologyDataProvider
{
    /// <summary>获取心情水平 (0-1)</summary>
    float GetMoodLevel(int pawnId);

    /// <summary>获取所有需求水平</summary>
    IReadOnlyList<NeedLevel> GetNeedLevels(int pawnId);

    /// <summary>获取精神崩溃阈值</summary>
    float GetMentalBreakThreshold(int pawnId);

    /// <summary>当前是否处于精神崩溃状态</summary>
    bool IsInMentalState(int pawnId);
}
