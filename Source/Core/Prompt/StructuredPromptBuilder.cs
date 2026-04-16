using System;
using System.Collections.Generic;
using System.Text;
using Verse;

namespace RimMind.Core.Prompt
{
    public class StructuredPromptBuilder
    {
        private string _role = string.Empty;
        private string _goal = string.Empty;
        private string _process = string.Empty;
        private readonly List<string> _constraints = new List<string>();
        private string _output = string.Empty;
        private string _example = string.Empty;
        private string _fallback = string.Empty;
        private string _custom = string.Empty;

        private bool _useSectionLabels;

        private static readonly string[] SectionSuffixes = { "Role", "Goal", "Process", "Constraint", "Example", "Output", "Fallback" };

        public static StructuredPromptBuilder FromKeyPrefix(string prefix)
        {
            var builder = new StructuredPromptBuilder();
            builder.RoleFromKey($"{prefix}.Role");
            builder.GoalFromKey($"{prefix}.Goal");
            builder.ProcessFromKey($"{prefix}.Process");
            builder.ConstraintFromKey($"{prefix}.Constraint");
            builder.ExampleFromKey($"{prefix}.Example");
            builder.OutputFromKey($"{prefix}.Output");
            builder.FallbackFromKey($"{prefix}.Fallback");
            return builder;
        }

        public StructuredPromptBuilder Role(string role) { _role = role ?? string.Empty; return this; }
        public StructuredPromptBuilder Goal(string goal) { _goal = goal ?? string.Empty; return this; }
        public StructuredPromptBuilder Process(string process) { _process = process ?? string.Empty; return this; }
        public StructuredPromptBuilder Constraint(string constraint) { if (!string.IsNullOrEmpty(constraint)) _constraints.Add(constraint); return this; }
        public StructuredPromptBuilder Output(string output) { _output = output ?? string.Empty; return this; }
        public StructuredPromptBuilder Example(string example) { _example = example ?? string.Empty; return this; }
        public StructuredPromptBuilder Fallback(string fallback) { _fallback = fallback ?? string.Empty; return this; }
        public StructuredPromptBuilder Custom(string custom) { _custom = custom ?? string.Empty; return this; }

        public StructuredPromptBuilder RoleFromKey(string translationKey) =>
            Role(TranslateOrEmpty(translationKey));

        public StructuredPromptBuilder GoalFromKey(string translationKey) =>
            Goal(TranslateOrEmpty(translationKey));

        public StructuredPromptBuilder ProcessFromKey(string translationKey) =>
            Process(TranslateOrEmpty(translationKey));

        public StructuredPromptBuilder ConstraintFromKey(string translationKey) =>
            Constraint(TranslateOrEmpty(translationKey));

        public StructuredPromptBuilder OutputFromKey(string translationKey) =>
            Output(TranslateOrEmpty(translationKey));

        public StructuredPromptBuilder ExampleFromKey(string translationKey) =>
            Example(TranslateOrEmpty(translationKey));

        public StructuredPromptBuilder FallbackFromKey(string translationKey) =>
            Fallback(TranslateOrEmpty(translationKey));

        public StructuredPromptBuilder WithCustom(string? custom, string? headerKey = null)
        {
            if (string.IsNullOrWhiteSpace(custom)) return this;
            string header = string.IsNullOrEmpty(headerKey)
                ? "RimMind.Core.Prompt.CustomHeader".Translate()
                : headerKey.Translate();
            _custom = (header ?? string.Empty) + "\n" + custom.Trim();
            return this;
        }

        public StructuredPromptBuilder WithSectionLabels(bool enable = true)
        {
            _useSectionLabels = enable;
            return this;
        }

        public string Build()
        {
            var sb = new StringBuilder();
            string combinedConstraint = _constraints.Count > 0
                ? string.Join("\n", _constraints)
                : string.Empty;

            AppendSection(sb, "Role", _role);
            AppendSection(sb, "Goal", _goal);
            AppendSection(sb, "Process", _process);
            AppendSection(sb, "Constraint", combinedConstraint);
            AppendSection(sb, "Example", _example);
            AppendSection(sb, "Output", _output);
            AppendSection(sb, "Fallback", _fallback);
            AppendSection(sb, "Custom", _custom);

            return sb.ToString().TrimEnd();
        }

        public PromptSection ToSection(string tag = "system_prompt", int priority = 0)
        {
            return new PromptSection(tag, Build(), priority);
        }

        private void AppendSection(StringBuilder sb, string sectionName, string content)
        {
            if (string.IsNullOrEmpty(content)) return;
            if (_useSectionLabels)
            {
                string label = $"RimMind.Core.Prompt.Section.{sectionName}".Translate();
                if (!string.IsNullOrEmpty(label) && label != $"RimMind.Core.Prompt.Section.{sectionName}")
                    sb.AppendLine($"[{label}]");
            }
            sb.AppendLine(content);
        }

        private static string TranslateOrEmpty(string key)
        {
            try
            {
                string result = key.Translate();
                return result == key ? string.Empty : result;
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
