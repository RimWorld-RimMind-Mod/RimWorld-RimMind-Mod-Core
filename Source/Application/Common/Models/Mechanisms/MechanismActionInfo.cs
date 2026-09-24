using System.Collections.Generic;

namespace RimMind.Application.Common.Models.Mechanisms
{
    public sealed record MechanismActionInfo
    {
        public string Action { get; init; }
        public string Description { get; init; }
        public string? DefNameHint { get; init; }
        public IReadOnlyList<string>? RequiredParams { get; init; }

        public MechanismActionInfo()
        {
            Action = "";
            Description = "";
        }

        public MechanismActionInfo(string action, string description, string? defNameHint = null, IReadOnlyList<string>? requiredParams = null)
        {
            Action = action;
            Description = description;
            DefNameHint = defNameHint;
            RequiredParams = requiredParams;
        }
    }
}
