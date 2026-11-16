namespace RimMind.Domain.Llm
{
    /// <summary>
    /// Semantic reasoning-depth hint for LLM requests.
    /// Downstream protocol adapters translate this intent into vendor-specific parameters
    /// (for example, disabling thinking on real-time dialogue or enabling deeper reasoning on planning).
    /// </summary>
    public enum ReasoningEffort
    {
        /// <summary>Let the target endpoint or node policy decide.</summary>
        Auto = 0,

        /// <summary>Disable explicit chain-of-thought reasoning for low-latency responses.</summary>
        None = 1,

        /// <summary>Light reasoning budget for simple structured tasks.</summary>
        Low = 2,

        /// <summary>Moderate reasoning budget.</summary>
        Medium = 3,

        /// <summary>Deep reasoning budget for multi-step agent decisions and planning.</summary>
        High = 4,

        /// <summary>Maximum reasoning budget supported by the endpoint.</summary>
        Max = 5,
    }
}
