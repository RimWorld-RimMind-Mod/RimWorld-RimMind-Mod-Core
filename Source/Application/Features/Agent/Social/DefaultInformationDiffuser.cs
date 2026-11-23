using System;
using System.Collections.Generic;
using RimMind.Application.Common.Interfaces;
using RimMind.Application.Common.Interfaces.Abstractions;
using RimMind.Application.Common.Interfaces.Agent;
using RimMind.Application.Common.Interfaces.Agent.Social;
using RimMind.Application.Features.Tools;
using RimMind.Domain.Agent.Social;
using RimMind.Domain.Events;
using RimMind.Domain.ValueObjects;

namespace RimMind.Application.Features.Agent.Social;

public sealed class DefaultInformationDiffuser : IInformationDiffuser
{
    private const int MaxRumorsPerNpc = 32;

    private readonly IAgentBus _agentBus;
    private readonly ITickProvider _tickProvider;
    private readonly object _syncLock = new object();
    private readonly Dictionary<string, List<RumorEntry>> _knownRumors = new();
    private readonly Random _random = new();

    public DefaultInformationDiffuser(IAgentBus agentBus, ITickProvider tickProvider)
    {
        _agentBus = agentBus;
        _tickProvider = tickProvider;
    }

    public bool ShouldDiffuse(IAgentInfo source, IAgentInfo target, RumorEntry rumor)
    {
        var probability = rumor.Importance * 0.7f * 0.5f;
        double sample;
        lock (_syncLock)
        {
            sample = _random.NextDouble();
        }
        return sample < probability;
    }

    public Result<RumorEntry, RimMindError> Diffuse(IAgentInfo source, IAgentInfo target, RumorEntry original)
    {
        var diffused = original with
        {
            DistortionLevel = original.DistortionLevel + 1,
            SourceNpcId = source.NpcId,
        };
        AddRumor(target.NpcId, diffused);
        PawnIdParser.TryParse(target.NpcId, out int targetPawnId);
        _agentBus.Publish(new InformationDiffusionEvent(
            target.NpcId, targetPawnId, diffused.RumorId, diffused.Content,
            diffused.SourceNpcId, diffused.Importance, diffused.DistortionLevel,
            _tickProvider.TicksGame));
        return Result<RumorEntry, RimMindError>.Ok(diffused);
    }

    public IReadOnlyList<RumorEntry> GetKnownRumors(string npcId)
    {
        lock (_syncLock)
        {
            return _knownRumors.TryGetValue(npcId, out var list)
                ? list.ToArray()
                : Array.Empty<RumorEntry>();
        }
    }

    public void AddRumor(string npcId, RumorEntry rumor)
    {
        if (string.IsNullOrEmpty(npcId) || rumor == null) return;
        lock (_syncLock)
        {
            if (!_knownRumors.TryGetValue(npcId, out var list))
            {
                list = new List<RumorEntry>();
                _knownRumors[npcId] = list;
            }
            list.Add(rumor);
            if (list.Count > MaxRumorsPerNpc)
            {
                list.RemoveRange(0, list.Count - MaxRumorsPerNpc);
            }
        }
    }

    public void ClearNpc(string npcId)
    {
        if (string.IsNullOrEmpty(npcId)) return;
        lock (_syncLock)
        {
            _knownRumors.Remove(npcId);
        }
    }

    public void Clear()
    {
        lock (_syncLock)
        {
            _knownRumors.Clear();
        }
    }
}
