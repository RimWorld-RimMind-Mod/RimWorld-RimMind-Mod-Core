using System;
using System.Collections.Generic;
using System.Linq;

namespace RimMind.Domain.Llm
{
    /// <summary>
    /// A system prompt fragment inserted after the final context-provided system message.
    /// </summary>
    public sealed record PromptAugmentation(string Id, string Content, int Order)
    {
        public static void InsertAfterLastSystem(
            List<ChatMessage>? messages,
            IEnumerable<PromptAugmentation>? augmentations)
        {
            if (messages == null || augmentations == null)
                return;

            var ordered = augmentations
                .Where(augmentation => augmentation != null && !string.IsNullOrWhiteSpace(augmentation.Content))
                .OrderBy(augmentation => augmentation.Order)
                .ThenBy(augmentation => augmentation.Id, StringComparer.Ordinal)
                .ToList();

            if (ordered.Count == 0)
                return;

            var insertIndex = 0;
            for (var index = messages.Count - 1; index >= 0; index--)
            {
                if (string.Equals(messages[index].Role, "system", StringComparison.OrdinalIgnoreCase))
                {
                    insertIndex = index + 1;
                    break;
                }
            }

            messages.InsertRange(insertIndex, ordered.Select(augmentation => new ChatMessage
            {
                Role = "system",
                Content = augmentation.Content,
            }));
        }
    }
}
