namespace RimMind.Application.Common.Constants
{
    /// <summary>
    /// OwnerModId 统一常量。所有 Core 内置 IExtension 实现应引用此类，
    /// 避免散落的字符串字面量导致 UnregisterByOwner 查询不一致。
    /// </summary>
    public static class RimMindOwnerConsts
    {
        /// <summary>Core 模组的 OwnerModId 标识。</summary>
        public const string CoreModId = "RimMindCore";

        /// <summary>Core Agent 请求来源 ModId 标识。</summary>
        public const string AgentModId = "RimMind.Agent";

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

        /// <summary>Extension-ModelService 模组的 OwnerModId 标识。</summary>
        public const string ExtensionModelServiceModId = "RimMindExtensionModelService";
    }
}
