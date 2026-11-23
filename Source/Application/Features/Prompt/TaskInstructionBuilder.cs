using System.Collections.Generic;
using System.Text;
using RimMind.Application.Common.Interfaces.Abstractions;

namespace RimMind.Application.Features.Prompt
{
    public sealed class TaskInstructionBuilder
    {
        private readonly StringBuilder _sb = new StringBuilder();

        public TaskInstructionBuilder AddRole(string role)
        {
            _sb.AppendLine($"You are {role}.");
            return this;
        }

        public TaskInstructionBuilder AddObjective(string objective)
        {
            _sb.AppendLine($"Objective: {objective}");
            return this;
        }

        public TaskInstructionBuilder AddConstraint(string constraint)
        {
            _sb.AppendLine($"Constraint: {constraint}");
            return this;
        }

        public TaskInstructionBuilder AddContext(string context)
        {
            _sb.AppendLine($"Context: {context}");
            return this;
        }

        public TaskInstructionBuilder AddFormat(string format)
        {
            _sb.AppendLine($"Response format: {format}");
            return this;
        }

        public string Build()
        {
            return _sb.ToString();
        }

        public void Reset() => _sb.Clear();

        public static string Build(string keyPrefix, ITranslationService? translationService, params string[] subKeys)
        {
            if (translationService == null || subKeys == null || subKeys.Length == 0)
                return string.Empty;

            var parts = new List<string>();
            foreach (var subKey in subKeys)
            {
                if (string.IsNullOrEmpty(subKey)) continue;
                string fullKey = $"{keyPrefix}.{subKey}";
                if (!translationService.CanTranslate(fullKey))
                    continue;
                string translated = translationService.Translate(fullKey);
                if (string.IsNullOrEmpty(translated) || translated == fullKey)
                    continue;
                parts.Add(translated);
            }
            return parts.Count > 0 ? string.Join("\n\n", parts) : string.Empty;
        }
    }
}
