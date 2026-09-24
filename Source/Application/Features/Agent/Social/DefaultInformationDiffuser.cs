using System;
using System.Collections.Generic;
using RimMind.Application.Common.Interfaces;
using RimMind.Application.Common.Interfaces.Abstractions;
using RimMind.Application.Common.Interfaces.Agent;
using RimMind.Application.Common.Interfaces.Agent.Social;
using RimMind.Domain.Agent.Social;
using RimMind.Domain.Events;
using RimMind.Domain.ValueObjects;

namespace RimMind.Application.Features.Agent.Social;

public sealed class DefaultInformationDiffuser : IInformationDiffuser
{
    private readonly IAgentBus _agentBus;
    private readonly ITickProvider _tickProvider;
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
        return _random.NextDouble() < probability;
    }

    public Result<RumorEntry, RimMindError> Diffuse(IAgentInfo source, IAgentInfo target, RumorEntry original)
    {
        var diffused = original with
        {
            DistortionLevel = original.DistortionLevel + 1,
            SourceNpcId = source.NpcId,
        };
        AddRumor(target.NpcId, diffused);
        _agentBus.Publish(new InformationDiffusionEvent(
            target.NpcId, 0, diffused.RumorId, diffused.Content,
            diffused.SourceNpcId, diffused.Importance, diffused.DistortionLevel,
            _tickProvider.TicksGame));
        return Result<RumorEntry, RimMindError>.Ok(diffused);
    }

    public IReadOnlyList<RumorEntry> GetKnownRumors(string npcId)
        => _knownRumors.TryGetValue(npcId, out var list) ? list : Array.Empty<RumorEntry>();

    public void AddRumor(string npcId, RumorEntry rumor)
    {
        if (!_knownRumors.TryGetValue(npcId, out var list))
        {
            list = new List<RumorEntry>();
            _knownRumors[npcId] = list;
        }
        list.Add(rumor);
    }
}
