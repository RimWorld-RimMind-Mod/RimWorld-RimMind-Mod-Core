using System.Collections.Generic;
using RimMind.Application.Common.Interfaces.Sensor;
using RimMind.Domain.Llm;
using Verse;

namespace RimMind.Presentation.Sensor
{
    public class SensorManager : ISensorManager
    {
        public List<StructuredTool> BuildAgentTools(object pawn)
        {
            return new List<StructuredTool>();
        }

        public void RegisterSensorContextKeys()
        {
            // No sensor providers registered; no context keys to register.
        }
    }
}
