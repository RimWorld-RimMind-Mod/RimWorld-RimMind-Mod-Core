using System;
using System.Collections.Generic;
using RimMind.Application.Common.Interfaces;
using RimMind.Application.Common.Interfaces.Abstractions;
using RimMind.Application.Common.Interfaces.Agent;
using RimMind.Application.Common.Interfaces.Agent.Psychology;
using RimMind.Domain.Agent.Psychology;
using RimMind.Domain.Enums;
using RimMind.Domain.Events;

namespace RimMind.Application.Features.Agent.Psychology;

internal sealed class DefaultPsychologyWatcher : IPsychologyWatcher
{
    private readonly ITickProvider _tickProvider;
    private readonly IAgentBus _agentBus;
    private readonly IPawnPsychologyDataProvider _psychologyDataProvider;

    private const int CheckIntervalTicks = 1500;
    private const float MoodNormalThreshold = 0.6f;
    private const float MoodLowThreshold = 0.3f;
    private const float NeedHighThreshold = 0.3f;
    private const float NeedCriticalThreshold = 0.1f;
    private const float MentalBreakApproachFactor = 1.2f;

    private readonly Dictionary<string, int> _lastCheckTick = new();
    private readonly Dictionary<string, float> _lastMoodLevel = new();
    private readonly Dictionary<string, Dictionary<string, float>> _lastNeedLevels = new();
    private readonly HashSet<string> _urgentEvents = new();

    public DefaultPsychologyWatcher(
        ITickProvider tickProvider,
        IAgentBus agentBus,
        IPawnPsychologyDataProvider psychologyDataProvider)
    {
        _tickProvider = tickProvider ?? throw new ArgumentNullException(nameof(tickProvider));
        _agentBus = agentBus ?? throw new ArgumentNullException(nameof(agentBus));
        _psychologyDataProvider = psychologyDataProvider ?? throw new ArgumentNullException(nameof(psychologyDataProvider));
    }

    public void CheckAndPublish(IAgentInfo agent, int pawnId)
    {
        var npcId = agent.NpcId;
        var currentTick = _tickProvider.TicksGame;

        if (_lastCheckTick.TryGetValue(npcId, out var lastTick)
            && currentTick - lastTick < CheckIntervalTicks)
        {
            return;
        }
        _lastCheckTick[npcId] = currentTick;

        CheckMood(npcId, pawnId);
        CheckNeeds(npcId, pawnId);
        CheckMentalState(npcId, pawnId);
    }

    public bool HasUrgentEvent(string npcId)
    {
        return _urgentEvents.Contains(npcId);
    }

    private void CheckMood(string npcId, int pawnId)
    {
        var currentMood = _psychologyDataProvider.GetMoodLevel(pawnId);

        if (_lastMoodLevel.TryGetValue(npcId, out var lastMood))
        {
            var previousThreshold = ClassifyMoodThreshold(lastMood);
            var currentThreshold = ClassifyMoodThreshold(currentMood);

            if (previousThreshold != currentThreshold
                && (previousThreshold == MoodThreshold.Normal && currentThreshold == MoodThreshold.Low
                    || previousThreshold == MoodThreshold.Low && currentThreshold == MoodThreshold.Critical))
            {
                _agentBus.Publish(new MoodThresholdCrossedEvent(
                    npcId, pawnId, lastMood, currentMood, currentThreshold));
            }
        }

        _lastMoodLevel[npcId] = currentMood;
    }

    private void CheckNeeds(string npcId, int pawnId)
    {
        var currentNeeds = _psychologyDataProvider.GetNeedLevels(pawnId);
        var hasCriticalNeed = false;

        foreach (var need in currentNeeds)
        {
            var urgency = ClassifyNeedUrgency(need.CurrentLevel);

            if (urgency == NeedUrgency.High || urgency == NeedUrgency.Critical)
            {
                if (_lastNeedLevels.TryGetValue(npcId, out var lastNeeds)
                    && lastNeeds.TryGetValue(need.NeedId, out var lastLevel))
                {
                    var lastUrgency = ClassifyNeedUrgency(lastLevel);
                    if (urgency != lastUrgency)
                    {
                        _agentBus.Publish(new NeedCriticalEvent(
                            npcId, pawnId, need.NeedId, need.CurrentLevel, urgency));
                    }
                }
                else
                {
                    _agentBus.Publish(new NeedCriticalEvent(
                        npcId, pawnId, need.NeedId, need.CurrentLevel, urgency));
                }
            }

            if (urgency == NeedUrgency.Critical)
            {
                hasCriticalNeed = true;
            }
        }

        if (hasCriticalNeed)
        {
            _urgentEvents.Add(npcId);
        }
        else
        {
            _urgentEvents.Remove(npcId);
        }

        var needsDict = new Dictionary<string, float>();
        foreach (var need in currentNeeds)
        {
            needsDict[need.NeedId] = need.CurrentLevel;
        }
        _lastNeedLevels[npcId] = needsDict;
    }

    private void CheckMentalState(string npcId, int pawnId)
    {
        var currentMood = _psychologyDataProvider.GetMoodLevel(pawnId);
        var breakThreshold = _psychologyDataProvider.GetMentalBreakThreshold(pawnId);
        var approachThreshold = breakThreshold * MentalBreakApproachFactor;

        if (currentMood <= breakThreshold)
        {
            _agentBus.Publish(new MentalStateWarningEvent(
                npcId, pawnId, breakThreshold, currentMood, "imminent"));
            _urgentEvents.Add(npcId);
        }
        else if (currentMood <= approachThreshold)
        {
            _agentBus.Publish(new MentalStateWarningEvent(
                npcId, pawnId, breakThreshold, currentMood, "approaching"));
        }
    }

    private static MoodThreshold ClassifyMoodThreshold(float moodLevel)
    {
        if (moodLevel >= MoodNormalThreshold) return MoodThreshold.Normal;
        if (moodLevel >= MoodLowThreshold) return MoodThreshold.Low;
        return MoodThreshold.Critical;
    }

    private static NeedUrgency ClassifyNeedUrgency(float level)
    {
        if (level < NeedCriticalThreshold) return NeedUrgency.Critical;
        if (level < NeedHighThreshold) return NeedUrgency.High;
        return NeedUrgency.Low;
    }
}
