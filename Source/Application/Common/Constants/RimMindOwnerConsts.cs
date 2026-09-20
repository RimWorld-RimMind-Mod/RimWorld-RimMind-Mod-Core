namespace RimMind.Application.Common.Constants
{
    /// <summary>
    /// OwnerModId 统一常量。所有 Core 内置 IExtension 实现应引用此类，
    /// 避免散落的字符串字面量导致 UnregisterByOwner 查询不一致。
    /// </summary>
    /// <remarks>
    /// 已知不一致（待后续统一）：
    /// - <c>"RimMind.Core"</c>：用于 ScopedAgent.cs、ToolManifest.cs（2 处）
    /// - <c>"Core"</c>：用于 OutputGuardrailMiddleware.cs、CoreContextProviders.cs 的 ownerMod 参数（~21 处）
    /// 这两种写法暂不修改，避免破坏存档序列化或潜在的 UnregisterByOwner 查询。
    /// 后续应统一为 <see cref="CoreModId"/>。
    /// </remarks>
    public static class RimMindOwnerConsts
    {
        /// <summary>Core 模组的 OwnerModId 标识。</summary>
        public const string CoreModId = "RimMindCore";

        /// <summary>Actions 模组的 OwnerModId 标识。</summary>
        public const string ActionsModId = "RimMindActions";

        /// <summary>Advisor 模组的 OwnerModId 标识。</summary>
        public const string AdvisorModId = "RimMindAdvisor";

        /// <summary>Dialogue 模组的 OwnerModId 标识。</summary>
        public const string DialogueModId = "RimMindDialogue";

        /// <summary>Memory 模组的 OwnerModId 标识。</summary>
        public const string MemoryModId = "RimMindMemory";

        /// <summary>Personality 模组的 OwnerModId 标识。</summary>
        public const string PersonalityModId = "RimMindPersonality";

        /// <summary>Storyteller 模组的 OwnerModId 标识。</summary>
        public const string StorytellerModId = "RimMindStoryteller";

        /// <summary>Bridge-RimTalk 模组的 OwnerModId 标识。</summary>
        public const string BridgeRimTalkModId = "RimMindBridgeRimTalk";

        /// <summary>Bridge-RimChat 模组的 OwnerModId 标识。</summary>
        public const string BridgeRimChatModId = "RimMindBridgeRimChat";
    }
}
