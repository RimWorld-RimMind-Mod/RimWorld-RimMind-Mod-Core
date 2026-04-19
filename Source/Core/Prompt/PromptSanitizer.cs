namespace RimMind.Core.Prompt
{
    public static class PromptSanitizer
    {
        public static string Sanitize(string prompt)
        {
            if (string.IsNullOrEmpty(prompt)) return prompt;
            return prompt.Replace("{{", "{").Replace("}}", "}");
        }
    }
}
