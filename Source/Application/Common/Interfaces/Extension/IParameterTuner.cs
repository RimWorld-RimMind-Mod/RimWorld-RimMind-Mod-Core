using System.Collections.Generic;

namespace RimMind.Application.Common.Interfaces.Extension
{
    public interface IParameterTuner : IExtension
    {
        string Name { get; }
        string TunerId { get; }
        float TuneParameter(string parameterName, float currentValue);
        bool ShouldApply(string npcId);
    }
}
