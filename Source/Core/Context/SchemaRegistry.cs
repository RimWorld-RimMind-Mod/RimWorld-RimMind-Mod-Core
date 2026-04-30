namespace RimMind.Core.Context
{
    public static class SchemaRegistry
    {
        public const string AgentDecision = "{\"type\":\"object\",\"properties\":{\"action\":{\"type\":\"string\"},\"target\":{\"type\":\"string\"},\"reason\":{\"type\":\"string\"}},\"required\":[\"action\",\"reason\"]}";

        public const string AdviceOutput = "{\"type\":\"object\",\"properties\":{\"advices\":{\"type\":\"array\",\"items\":{\"type\":\"object\",\"properties\":{\"action\":{\"type\":\"string\"},\"target\":{\"type\":\"string\"},\"param\":{\"type\":\"string\"},\"reason\":{\"type\":\"string\"}},\"required\":[\"action\",\"reason\"]}}},\"required\":[\"advices\"]}";

        public const string PersonalityOutput = "{\"type\":\"object\",\"properties\":{\"thoughts\":{\"type\":\"array\",\"items\":{\"type\":\"object\",\"properties\":{\"type\":{\"type\":\"string\"},\"label\":{\"type\":\"string\"},\"description\":{\"type\":\"string\"},\"intensity\":{\"type\":\"number\"},\"duration_hours\":{\"type\":\"number\"}}},\"required\":[\"type\",\"label\",\"description\",\"intensity\"]}},\"narrative\":{\"type\":\"string\"},\"identity\":{\"type\":\"object\",\"properties\":{\"motivations\":{\"type\":\"array\",\"items\":{\"type\":\"string\"}},\"traits\":{\"type\":\"array\",\"items\":{\"type\":\"string\"}},\"core_values\":{\"type\":\"array\",\"items\":{\"type\":\"string\"}}}}},\"required\":[\"thoughts\"]}";

        public const string IncidentOutput = "{\"type\":\"object\",\"properties\":{\"defName\":{\"type\":\"string\"},\"reason\":{\"type\":\"string\"},\"params\":{\"type\":\"object\"},\"chain\":{\"type\":\"object\"}},\"required\":[\"defName\",\"reason\"]}";

        public const string DarkMemoryOutput = "{\"type\":\"object\",\"properties\":{\"dark\":{\"type\":\"array\",\"items\":{\"type\":\"string\"}}},\"required\":[\"dark\"]}";

        public const string DialogueOutput = "{\"type\":\"object\",\"properties\":{\"reply\":{\"type\":\"string\"},\"thought\":{\"type\":\"object\",\"properties\":{\"tag\":{\"type\":\"string\"},\"description\":{\"type\":\"string\"}}},\"relation_delta\":{\"type\":\"number\"}},\"required\":[\"reply\"]}";
    }
}
