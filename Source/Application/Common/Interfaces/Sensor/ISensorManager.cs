using System.Collections.Generic;
using RimMind.Domain.Llm;

namespace RimMind.Application.Common.Interfaces.Sensor
{
    public interface ISensorManager
    {
        List<StructuredTool> BuildAgentTools(object pawn);
        void RegisterSensorContextKeys();
    }
}
