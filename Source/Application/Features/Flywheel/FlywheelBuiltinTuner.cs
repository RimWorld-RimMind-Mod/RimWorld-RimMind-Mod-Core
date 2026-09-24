using System;
using RimMind.Application.Common.Constants;
using RimMind.Application.Common.Interfaces.Extension;

namespace RimMind.Application.Features.Flywheel
{
    public class FlywheelBuiltinTuner : IParameterTuner
    {
        public string Id => TunerId;
        public string OwnerModId => RimMindOwnerConsts.CoreModId;
        public string Name => "FlywheelBuiltin";
        public string TunerId => "flywheel_builtin";

        public float TuneParameter(string parameterName, float currentValue)
        {
            return currentValue;
        }

        public bool ShouldApply(string npcId)
        {
            return true;
        }

    }
}
