using System;
using System.Collections.Generic;
using System.Linq;
using RimMind.Core.Context;
using RimMind.Core.Extensions;
using Verse;

namespace RimMind.Core.Sensor
{
    /// <summary>
    /// SensorManager: Central coordinator for all Sensor providers.
    /// Handles timed polling, Agent Tool aggregation, and multi-source fusion.
    /// </summary>
    public class SensorManager : GameComponent
    {
        public static SensorManager? Instance { get; private set; }

        private int _lastPollTick;

        public SensorManager(Game game)
        {
            Instance = this;
        }

        public override void FinalizeInit()
        {
            base.FinalizeInit();
            Instance = this;
            RegisterSensorContextKeys();
        }

        private void RegisterSensorContextKeys()
        {
            foreach (var sensor in RimMindAPI.SensorProviders)
            {
                string key = $"sensor_{sensor.SensorId}";
                var captured = sensor;
                ContextKeyRegistry.Register(key, ContextLayer.L5_Sensor, captured.Priority / 100f,
                    pawn =>
                    {
                        string? data = captured.Sense(pawn);
                        if (string.IsNullOrEmpty(data))
                            return new List<ContextEntry>();
                        return new List<ContextEntry> { new ContextEntry(data!) };
                    }, "Core");
            }
        }

        public override void GameComponentTick()
        {
            base.GameComponentTick();
            int now = Find.TickManager.TicksGame;

            foreach (var sensor in RimMindAPI.SensorProviders.ToArray())
            {
                if (sensor.TickInterval <= 0) continue;
                if (now % sensor.TickInterval != 0) continue;

                try
                {
                    foreach (var map in Find.Maps)
                    {
                        foreach (var pawn in map.mapPawns.FreeColonists)
                        {
                            string? data = sensor.Sense(pawn);
                            if (!string.IsNullOrEmpty(data))
                                global::RimMind.Core.Perception.PerceptionBridge.PublishPerception(
                                    pawn.thingIDNumber, sensor.SensorId, data!,
                                    sensor.Priority / 100f);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning($"[RimMind] Sensor '{sensor.SensorId}' tick error: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Build Agent Tools list from all registered sensors for the given pawn.
        /// Converts AgentToolDefinition to StructuredTool for AI tool calling.
        /// </summary>
        public List<Client.StructuredTool> BuildAgentTools(Pawn pawn)
        {
            var tools = new List<Client.StructuredTool>();
            foreach (var sensor in RimMindAPI.SensorProviders)
            {
                try
                {
                    var defs = sensor.GetAgentTools(pawn);
                    foreach (var def in defs)
                    {
                        tools.Add(new Client.StructuredTool
                        {
                            Name = def.Name,
                            Description = def.Description,
                            Parameters = def.Parameters
                        });
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning($"[RimMind] Sensor '{sensor.SensorId}' GetAgentTools error: {ex.Message}");
                }
            }
            return tools;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref _lastPollTick, "lastPollTick");
        }
    }
}
