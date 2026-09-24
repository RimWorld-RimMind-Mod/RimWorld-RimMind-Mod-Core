namespace RimMind.Application.Common.Interfaces.Registry
{
    /// <summary>
    /// 支持按 OwnerModId 批量注销的注册表契约。
    /// 子 Mod 卸载时调用 <see cref="UnregisterByOwner"/> 清理其注册的资源，
    /// 避免内存泄漏和残留引用。
    /// </summary>
    /// <remarks>
    /// 适用条件：注册表项类型必须暴露 OwnerModId（或等价字段）。
    /// 不适用于：项无 owner 概念的注册表（如纯字符串缓存 SchemaRegistry）、
    /// 静态注册表（如 DebugCenterPageRegistry）、或注册时不存储 modId 的注册表（如 ProviderRegistry）。
    /// </remarks>
    public interface IOwnedRegistry
    {
        /// <summary>
        /// 注销指定 ownerModId 的所有注册项。
        /// </summary>
        /// <param name="ownerModId">Mod 所有者标识（如 <c>RimMindOwnerConsts.CoreModId</c>）。</param>
        /// <returns>实际注销的项数。若 ownerModId 为 null 抛出 <see cref="System.ArgumentNullException"/>。</returns>
        int UnregisterByOwner(string ownerModId);
    }
}
