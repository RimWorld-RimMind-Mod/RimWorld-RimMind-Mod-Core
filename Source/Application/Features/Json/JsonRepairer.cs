using System.Text;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace RimMind.Application.Features.Json
{
    public static class JsonRepairer
    {
        public static string? TryRepair(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return null;
            try
            {
                JToken.Parse(input);
                return null;
            }
            catch
            {
                var repaired = Repair(input);
                try
                {
                    JToken.Parse(repaired);
                    return repaired;
                }
                catch
                {
                    return null;
                }
            }
        }

        public static string? TryRepairTruncatedJson(string input) => TryRepair(input);

        public static string Repair(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return "{}";

            return BalanceTruncatedJson(RemoveTrailingCommas(input));
        }

        private static string RemoveTrailingCommas(string input)
        {
            var result = new StringBuilder(input.Length);
            bool inString = false;
            bool escaped = false;

            for (int i = 0; i < input.Length; i++)
            {
                char c = input[i];
                if (inString)
                {
                    result.Append(c);
                    if (escaped)
                        escaped = false;
                    else if (c == '\\')
                        escaped = true;
                    else if (c == '"')
                        inString = false;
                    continue;
                }

                if (c == '"')
                {
                    inString = true;
                    result.Append(c);
                    continue;
                }

                if (c == ',')
                {
                    int next = i + 1;
                    while (next < input.Length && char.IsWhiteSpace(input[next]))
                        next++;
                    if (next == input.Length || input[next] == '}' || input[next] == ']')
                        continue;
                }

                result.Append(c);
            }

            return result.ToString();
        }

        private static string BalanceTruncatedJson(string input)
        {
            var result = new StringBuilder(input.TrimEnd());
            var closers = new Stack<char>();
            bool inString = false;
            bool escaped = false;

            foreach (char c in result.ToString())
            {
                if (inString)
                {
                    if (escaped)
                    {
                        escaped = false;
                    }
                    else if (c == '\\')
                    {
                        escaped = true;
                    }
                    else if (c == '"')
                    {
                        inString = false;
                    }
                    continue;
                }

                switch (c)
                {
                    case '"':
                        inString = true;
                        break;
                    case '{':
                        closers.Push('}');
                        break;
                    case '[':
                        closers.Push(']');
                        break;
                    case '}':
                    case ']':
                        if (closers.Count > 0 && closers.Peek() == c)
                            closers.Pop();
                        break;
                }
            }

            if (inString)
            {
                if (escaped)
                    result.Append('\\');
                result.Append('"');
            }

            while (result.Length > 0 && char.IsWhiteSpace(result[result.Length - 1]))
                result.Length--;
            if (result.Length > 0 && result[result.Length - 1] == ',')
                result.Length--;

            while (closers.Count > 0)
                result.Append(closers.Pop());

            return result.ToString();
        }
    }
}
