using System;
using System.Text;
using RimMind.Domain.Llm;

namespace RimMind.Application.Features.Llm
{
    /// <summary>
    /// Formats envelope chat messages by role for trace logging without duplicating role-filtering logic.
    /// </summary>
    internal static class EnvelopePromptFormatter
    {
        public static string FormatRolePrompt(LlmRequestEnvelope? envelope, string role)
        {
            var messages = envelope?.Messages;
            if (messages == null || messages.Count == 0)
                return string.Empty;

            var text = new StringBuilder();
            foreach (var message in messages)
            {
                if (message == null
                    || !string.Equals(message.Role, role, StringComparison.OrdinalIgnoreCase)
                    || string.IsNullOrWhiteSpace(message.Content))
                    continue;

                if (text.Length > 0)
                    text.AppendLine().AppendLine();
                if (!string.IsNullOrWhiteSpace(message.LayerTag))
                    text.Append('[').Append(message.LayerTag).Append("] ");
                text.Append(message.Content);
            }

            return text.ToString();
        }
    }
}
