namespace RimMind.Core.Client
{
    public static class JsonRepairHelper
    {
        public static string? TryRepairTruncatedJson(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            string trimmed = json.TrimEnd();
            if (trimmed.EndsWith("}")) return null;

            int openBraces = 0, openBrackets = 0;
            bool inString = false;
            char prev = '\0';
            foreach (char c in trimmed)
            {
                if (prev != '\\' && c == '"') inString = !inString;
                if (!inString)
                {
                    if (c == '{') openBraces++;
                    else if (c == '}') openBraces--;
                    else if (c == '[') openBrackets++;
                    else if (c == ']') openBrackets--;
                }
                prev = c;
            }

            if (inString)
            {
                int lastQuote = trimmed.LastIndexOf('"');
                if (lastQuote > 0)
                    trimmed = trimmed.Substring(0, lastQuote);
            }

            if (trimmed.EndsWith(",")) trimmed = trimmed.TrimEnd(',');

            trimmed += new string('}', openBraces > 0 ? openBraces : 0);
            trimmed += new string(']', openBrackets > 0 ? openBrackets : 0);

            return trimmed;
        }
    }
}
