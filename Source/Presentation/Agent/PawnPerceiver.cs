using System;
using System.Collections.Generic;
using System.Linq;
using RimMind.Application.Common.Interfaces;
using RimMind.Application.Common.Interfaces.Agent;
using RimMind.Application.Common.Interfaces.Agent.Perception;
using RimMind.Application.Common.Models;
using RimMind.Application.Common.Models.Pipeline;
using RimMind.Domain.Enums;
using RimMind.Domain.Events;
using RimMind.Presentation.Runtime;
using RimMind.Application.Common.Interfaces.Extension;
using Verse;

namespace RimMind.Presentation.Agent
{
    public class PawnPerceiver : IPawnPerceiver
    {
        private const int DefaultPerceptionInterval = RimMindDefaults.AgentTickInterval;
        private const float MoodImportance = RimMindDefaults.PerceptionLowThreshold;
        private const float HediffSeverityThreshold = RimMindDefaults.PerceptionMediumThreshold;
        private const float HealthImportance = RimMindDefaults.PerceptionHighThreshold;
        private const float CombatImportance = RimMindDefaults.PerceptionCriticalThreshold;

        private readonly IPawnAgentVerse _agent;
        private readonly IAgentBus _agentBus;
        private readonly IExtensionRegistry<IPerceptionSource>? _sourceRegistry;
        private readonly HashSet<string> _sensedHediffs = new HashSet<string>();
        private int _lastPerceptionTick;
        private int _perceptionInterval = DefaultPerceptionInterval;

        public PawnPerceiver(IPawnAgentVerse agent, IAgentBus agentBus,
            IExtensionRegistry<IPerceptionSource>? sourceRegistry = null)
        {
            _agent = agent ?? throw new ArgumentNullException(nameof(agent));
            _agentBus = agentBus ?? throw new ArgumentNullException(nameof(agentBus));
            _sourceRegistry = sourceRegistry;
        }

        public void Tick()
        {
            if (_agent.State != AgentState.Active) return;
            if (Find.TickManager.TicksGame - _lastPerceptionTick < _perceptionInterval) return;
            _lastPerceptionTick = Find.TickManager.TicksGame;
            Sense();
        }

        private void Sense()
        {
            var pawn = _agent.Pawn;
            if (pawn == null || pawn.Dead) return;

            _sensedHediffs.Clear();

            // Use registered perception sources if available
            var sources = _sourceRegistry?.All;
            if (sources != null && sources.Count > 0)
            {
                var sortedSources = sources.OrderBy(s => s.Priority).ToList();
                foreach (var source in sortedSources)
                {
                    if (!source.ShouldSense(_agent)) continue;
                    var entries = source.Sense(_agent);
                    foreach (var entry in entries)
                    {
                        _agent.PerceptionBuffer.Add(entry);
                    }
                }
            }
            else
            {
                // Fallback: inline perception (backward compatibility)
                SenseInline();
            }

            // Publish PerceptionEvent so subscribers can react to perception changes
            _agentBus.Publish(new PerceptionEvent(
                _agent.Identity.NpcId,
                _agent.Pawn?.thingIDNumber ?? -1,
                "composite",
                "Perception cycle completed",
                0f,
                Find.TickManager.TicksGame));
        }

        /// <summary>
        /// Inline perception logic used as fallback when no IPerceptionSource is registered.
        /// </summary>
        private void SenseInline()
        {
            var pawn = _agent.Pawn;
            if (pawn == null || pawn.Dead) return;

            if (pawn.needs?.mood != null)
            {
                _agent.PerceptionBuffer.Add(new PerceptionBufferEntry
                {
                    PerceptionType = "mood",
                    Content = $"Mood: {pawn.needs.mood.CurLevel:P0}",
                    Importance = MoodImportance,
                    Tick = Find.TickManager.TicksGame
                });
            }

            if (pawn.health?.hediffSet?.hediffs != null)
            {
                foreach (var hediff in pawn.health.hediffSet.hediffs)
                {
                    if (hediff != null && hediff.Visible && hediff.Severity > HediffSeverityThreshold
                        && _sensedHediffs.Add(hediff.def.defName))
                    {
                        _agent.PerceptionBuffer.Add(new PerceptionBufferEntry
                        {
                            PerceptionType = "health",
                            Content = $"Health issue: {hediff.Label}",
                            Importance = HealthImportance,
                            Tick = Find.TickManager.TicksGame
                        });
                    }
                }
            }

            if (pawn.Drafted)
            {
                _agent.PerceptionBuffer.Add(new PerceptionBufferEntry
                {
                    PerceptionType = "combat",
                    Content = "Currently drafted for combat",
                    Importance = CombatImportance,
                    Tick = Find.TickManager.TicksGame
                });
            }

            // Needs perception: collect needs below 30%
            if (pawn.needs?.AllNeeds != null)
            {
                foreach (var need in pawn.needs.AllNeeds)
                {
                    if (need != null && need.CurLevel < 0.3f)
                    {
                        var importance = (1f - need.CurLevel) * 0.8f;
                        _agent.PerceptionBuffer.Add(new PerceptionBufferEntry
                        {
                            PerceptionType = "need",
                            Content = $"Need: {need.def.label} at {need.CurLevel:P0}",
                            Importance = importance,
                            Tick = Find.TickManager.TicksGame
                        });
                    }
                }
            }

            // Social perception: nearby pawns with relationships
            if (pawn.relations?.DirectRelations != null)
            {
                foreach (var rel in pawn.relations.DirectRelations)
                {
                    if (rel == null) continue;
                    var other = rel.otherPawn;
                    if (other == null || other == pawn || other.Dead) continue;
                    if (other.Position.DistanceTo(pawn.Position) > 10) continue;

                    _agent.PerceptionBuffer.Add(new PerceptionBufferEntry
                    {
                        PerceptionType = "social",
                        Content = $"Social: {other.Name?.ToStringFull ?? other.Label} ({rel.def.label})",
                        Importance = 0.5f,
                        Tick = Find.TickManager.TicksGame
                    });
                }
            }

            // Environment perception: extreme weather or temperature
            var map = pawn.Map;
            if (map != null)
            {
                var weather = map.weatherManager?.curWeather;
                var temperature = GenTemperature.GetTemperatureForCell(pawn.Position, map);

                bool isExtremeWeather = weather != null && weather.defName != "Clear";
                bool isExtremeTemp = temperature < -10f || temperature > 45f;

                if (isExtremeWeather || isExtremeTemp)
                {
                    var weatherLabel = weather?.label ?? "unknown";
                    _agent.PerceptionBuffer.Add(new PerceptionBufferEntry
                    {
                        PerceptionType = "environment",
                        Content = $"Environment: {weatherLabel}, {temperature:F0}°C",
                        Importance = 0.6f,
                        Tick = Find.TickManager.TicksGame
                    });
                }
            }
        }
    }
}
