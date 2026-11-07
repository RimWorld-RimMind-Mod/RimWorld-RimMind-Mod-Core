using System.Collections.Generic;

namespace RimMind.Application.Common.Models.Mechanisms
{
    public sealed record MechanismActionInfo
    {
        public string Action { get; init; }
        public string Description { get; init; }
        public string? DefNameHint { get; init; }
        public IReadOnlyList<string>? RequiredParams { get; init; }
        public IReadOnlyList<MechanismParamDescriptor>? ParameterDescriptors { get; init; }

        public MechanismActionInfo()
        {
            Action = "";
            Description = "";
        }

        public MechanismActionInfo(
            string action,
            string description,
            string? defNameHint = null,
            IReadOnlyList<string>? requiredParams = null,
            IReadOnlyList<MechanismParamDescriptor>? parameterDescriptors = null)
        {
            Action = action;
            Description = description;
            DefNameHint = defNameHint;
            RequiredParams = requiredParams;
            ParameterDescriptors = parameterDescriptors;
        }
    }
}
