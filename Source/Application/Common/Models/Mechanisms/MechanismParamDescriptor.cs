using System;
using System.Collections.Generic;

namespace RimMind.Application.Common.Models.Mechanisms
{
    public sealed record MechanismParamDescriptor
    {
        public string Name { get; init; } = "";
        public string JsonType { get; init; } = "string";
        public IReadOnlyList<string>? Aliases { get; init; }
        public bool Required { get; init; }
        public string Description { get; init; } = "";

        public MechanismParamDescriptor()
        {
        }

        public MechanismParamDescriptor(
            string name,
            string jsonType = "string",
            string description = "",
            bool required = false,
            IReadOnlyList<string>? aliases = null)
        {
            Name = name ?? "";
            JsonType = string.IsNullOrWhiteSpace(jsonType) ? "string" : jsonType;
            Description = description ?? "";
            Required = required;
            Aliases = aliases ?? Array.Empty<string>();
        }
    }
}
