# AGENTS.md — RimMind-Core

本文件供 AI 编码助手阅读，描述 RimMind-Core 的架构、代码约定和扩展模式。

## 项目定位

RimMind-Core 是 RimMind AI 模组套件的核心基础设施层。所有子模组（Actions、Personality、Advisor、Memory 等）均依赖本模组。职责：

1. **LLM 客户端**：OpenAI Chat Completions 兼容 + Player2 服务，通过 `UnityWebRequest` 发送
2. **异步请求队列**：后台线程发请求，主线程回调，`ConcurrentQueue` 桥接，支持重试/暂停/取消
3. **游戏上下文构建**：将游戏状态打包为文本，供 AI Prompt 使用
4. **统一上下文引擎**：`ContextEngine` + `ContextKeyRegistry` + `BudgetScheduler`，L0-L4 分层构建，Diff 注入与合并，LRU 缓存
5. **Provider 注册机制**：子模组通过 `ContextKeyRegistry.Register` 注入上下文 Provider，支持卸载与覆盖
6. **Agent 认知架构**：`PawnAgent` 统一认知主体（Perceive→Think→Act→Record），`AgentBus` 事件总线，`AgentGoalStack` 目标栈
7. **NPC 对话系统**：`NpcManager` + `IStorageDriver` + `Window_AgentDialogue`，支持 LocalStorage 和 Player2 双驱动
8. **感知桥接**：6 个 Harmony Patch 将游戏事件转为 `PerceptionEvent`，经 `PerceptionPipeline` 过滤后注入 Agent
9. **数据飞轮（Flywheel）**：`FlywheelGameComponent` + `FlywheelRuleEngine` + `FlywheelParameterStore`，自动分析并调优上下文参数
10. **SkipCheck 互斥机制**：子模组可注册对话/浮菜单/动作/叙事者的跳过检查
11. **设置 UI**：多分页设置界面（API/队列/提示词/上下文），子模组可注册额外分页

## 源码结构

```
Source/
├── AICoreMod.cs              Mod 入口，注册 Harmony，持有 Settings 单例
├── AICoreAPI.cs              静态公共 API（RimMindAPI），供子模组调用
├── Client/
│   ├── IAIClient.cs          AI 客户端接口
│   ├── AIRequest.cs          请求数据结构（含 ChatMessage 多轮支持）
│   ├── AIResponse.cs         响应数据结构（含状态/优先级/重试/遥测）
│   ├── AIRequestState.cs     请求状态枚举 + 优先级枚举
│   ├── StructuredTool.cs     工具调用定义
│   ├── QuotaExceededException.cs  配额异常
│   ├── OpenAI/
│   │   ├── OpenAIClient.cs   OpenAI 兼容客户端实现
│   │   └── OpenAIDto.cs      请求/响应 DTO（internal）
│   └── Player2/
│       ├── Player2Client.cs  Player2 客户端实现（本地应用 + 远程 API）
│       └── Player2Models.cs  Player2 请求/响应 DTO（internal）
├── Core/
│   ├── AIRequestQueue.cs     GameComponent，异步请求队列 + 冷却 + 重试 + 暂停/取消
│   ├── GameContextBuilder.cs 静态工具类，构建地图/Pawn 上下文文本
│   ├── JsonTagExtractor.cs   从 AI 响应提取 <Tag>JSON</Tag> 内容
│   ├── AIDebugLog.cs         GameComponent，存储最近 200 条请求记录
│   ├── Agent/
│   │   ├── PawnAgent.cs      统一认知主体（Perceive→Think→Act→Record）
│   │   ├── IAgentProvider.cs Agent 状态查询接口
│   │   ├── DefaultAgentProvider.cs 默认实现，委托 CompPawnAgent
│   │   ├── AgentGoalStack.cs 目标栈（3活跃/10总上限，版本号缓存）
│   │   ├── AgentGoal.cs      目标数据结构
│   │   ├── AgentIdentity.cs  身份（动机/特质/价值观）
│   │   ├── AgentState.cs     Agent 状态枚举
│   │   ├── AgentStateTransition.cs 状态转换逻辑
│   │   ├── GoalGenerator.cs  目标生成器（身份/状态/事件）
│   │   ├── StrategyOptimizer.cs 策略优化器
│   │   ├── RiskLevel.cs      风险等级枚举（RimMind-Actions 使用）
│   │   ├── BehaviorRecord.cs 行为记录数据结构
│   │   ├── IAgentActionBridge.cs 动作桥接接口
│   │   ├── PerceptionBuffer.cs 感知缓冲区（容量20，FIFO）
│   │   ├── PerceptionPipeline.cs 感知过滤管道
│   │   ├── IPerceptionFilter.cs  感知过滤器接口
│   │   ├── DedupFilter.cs    去重过滤器
│   │   ├── PriorityFilter.cs 优先级过滤器
│   │   └── CooldownFilter.cs 冷却过滤器
│   ├── AgentBus/
│   │   ├── AgentBus.cs       事件总线（ConcurrentDictionary，主线程同步+后台队列）
│   │   ├── AgentBusEvent.cs  事件基类
│   │   ├── AgentBusEventType.cs 事件类型枚举
│   │   ├── IEventBus.cs      事件总线接口
│   │   ├── EventBusAdapter.cs 适配器，委托到 AgentBus
│   │   └── Events/           6种事件类（Perception/Decision/Goal/Memory/Action/AgentLifecycle）
│   ├── Context/
│   │   ├── ContextEngine.cs  统一上下文引擎（L0-L4分层，分块缓存，Diff注入，Tick过期）
│   │   ├── ContextKeyRegistry.cs 上下文Key注册表（per-key Provider）
│   │   ├── BudgetScheduler.cs 预算调度器（Score = W1*P + W2*R）
│   │   ├── BudgetSchedulerConfig.cs 调度器配置
│   │   ├── BudgetScheduleResult.cs 调度结果
│   │   ├── KeyMeta.cs        Key元数据（优先级/自适应/更新计数）
│   │   ├── HistoryManager.cs 对话历史管理（独立于状态缓存，支持 ReplaceLastAssistantTurn）
│   │   ├── HistoryEntry.cs   历史条目数据结构
│   │   ├── HistoryGameComponent.cs GameComponent 桥接 HistoryManager
│   │   ├── ContextSnapshot.cs 上下文快照
│   │   ├── ContextRequest.cs 上下文请求
│   │   ├── ContextLayer.cs   层级枚举（L0-L4）
│   │   ├── ContextScenario.cs 场景枚举
│   │   ├── ContextDiff.cs    Diff数据结构（Tick 过期，默认 600 ticks）
│   │   ├── ContextEntry.cs   缓存条目数据结构
│   │   ├── RelevanceTable.cs 场景-键相关性表
│   │   ├── ScenarioRegistry.cs 场景注册表
│   │   ├── SchemaRegistry.cs Schema 注册表
│   │   ├── SemanticEmbedding.cs 语义向量（硬编码+动态扩展）[Experimental]
│   │   └── EmbedCache.cs     嵌入缓存 [Experimental]
│   ├── Flywheel/
│   │   ├── FlywheelGameComponent.cs  GameComponent，定期分析并调优
│   │   ├── FlywheelRuleEngine.cs     规则引擎，分析指标并生成调优建议
│   │   ├── FlywheelParameterStore.cs GameComponent，参数存储（14个参数）
│   │   ├── FlywheelAutoApplyMode.cs  自动应用模式枚举
│   │   ├── FlywheelTelemetryCollector.cs 遥测数据采集
│   │   ├── FlywheelAnalysisReport.cs 分析报告
│   │   └── EmbeddingSnapshotStore.cs 嵌入快照存储
│   ├── Perception/
│   │   └── PerceptionBridge.cs 感知桥接静态工具
│   ├── Prompt/
│   │   ├── StructuredPromptBuilder.cs  流式 Prompt 构建器
│   │   ├── PromptSection.cs            Prompt 段落（支持 Clone 深拷贝）
│   │   ├── PromptBudget.cs             预算管理（Compose 时 Clone 防污染）
│   │   ├── ContextComposer.cs          上下文排序与拼接
│   │   └── PromptSanitizer.cs          Prompt 清理（去除 {{ }} 模板注入）
│   └── Extensions/
│       ├── IStreamingResponseHandler.cs 流式响应处理接口
│       ├── ISensorProvider.cs          传感器提供者接口
│       ├── IParameterTuner.cs          参数调优接口
│       ├── IAgentModeProvider.cs       Agent 模式提供者接口
│       └── IAudioPlayer.cs            音频播放接口
├── Comps/
│   └── CompPawnAgent.cs      ThingComp，每个 Agent Pawn 挂载
├── Npc/
│   ├── NpcManager.cs         GameComponent，NPC 生命周期管理
│   ├── NpcModels.cs          NPC 数据模型（NpcProfile + NpcTtsConfig，均实现 IExposable）
│   ├── NpcProfileBuilder.cs  NPC 配置构建器
│   ├── MapNpcComponent.cs    MapComponent，地图级 NPC 管理
│   ├── IStorageDriver.cs     存储驱动接口
│   ├── LocalStorageDriver.cs 本地存储驱动（120s 超时）
│   ├── Player2StorageDriver.cs Player2 存储驱动
│   ├── HybridStorageDriver.cs 混合存储驱动
│   ├── StorageDriverFactory.cs 存储驱动工厂（Player2 降级时 Log.Warning）
│   ├── ResponseDispatcher.cs 响应分发器
│   └── Patch_MapNpcLifecycle.cs NPC 生命周期 Patch
├── Settings/
│   ├── AICoreSettings.cs     模组设置（Provider + API + 请求控制 + Temperature + Flywheel）
│   ├── AIProvider.cs         AI 提供者枚举（OpenAI / Player2）
│   └── ContextSettings.cs    上下文过滤器（28 个 Include* 字段 + ContextPreset + BudgetW1/W2）
├── UI/
│   ├── AICoreSettingsUI.cs   多分页设置界面（API/队列/提示词/上下文）
│   ├── Window_AgentDialogue.cs Agent 对话窗口（thinking 占位 + ReplaceLastAssistantTurn）
│   ├── Window_AIDebugLog.cs  AI Debug Log 浮动窗口
│   ├── RequestOverlay.cs     请求审批悬浮窗
│   ├── RequestEntry.cs       悬浮窗请求条目
│   ├── Window_RequestLog.cs  请求日志窗口
│   └── SettingsUIHelper.cs   设置 UI 辅助
├── Patch/
│   ├── AITogglePatch.cs      右下角 AI 图标按钮注入
│   ├── Patch_UIRoot_OnGUI.cs 每帧调用 RequestOverlay.OnGUI
│   ├── PerceptionBridge_PatchDowned.cs  倒地感知（含 Agent 活跃检查 + 死亡清理）
│   ├── PerceptionBridge_PatchHealth.cs  健康感知（高严重度绕过节流）
│   ├── PerceptionBridge_PatchMentalState.cs 精神崩溃感知（无节流）
│   ├── PerceptionBridge_PatchMood.cs    心情感知（含死亡清理 + 临界检测）
│   └── PerceptionBridge_PatchRaid.cs    袭击感知
└── Debug/
    └── AICoreDebugActions.cs Dev 菜单调试动作
```

## 关键类与 API

### RimMindAPI（AICoreAPI.cs）

所有子模组通过此静态类与 Core 交互：

```csharp
// ── 请求 ──
RimMindAPI.RequestAsync(AIRequest request, Action<AIResponse> onComplete)
RimMindAPI.RequestImmediate(AIRequest request, Action<AIResponse> onComplete) // 绕过队列/冷却
RimMindAPI.RequestStructuredAsync(AIRequest request, string jsonSchema, Action<AIResponse> onComplete, List<StructuredTool>? tools)  // 结构化路径，回调在主线程
RimMindAPI.CancelRequest(string requestId)
RimMindAPI.PauseQueue() / ResumeQueue() / IsQueuePaused

// ── 上下文构建 ──
RimMindAPI.GetContextEngine()  // 获取共享 ContextEngine 实例
RimMindAPI.Chat(ContextRequest request, CancellationToken ct)  // NpcChat 对话路径

// ── Provider 注册（子模组在 Mod 构造时调用）──
RimMindAPI.RegisterStaticProvider(string category, Func<string> provider, int priority, string modId, bool overrideExisting)
RimMindAPI.RegisterDynamicProvider(string category, Func<string, string> provider, int priority, string modId, bool overrideExisting)
RimMindAPI.RegisterPawnContextProvider(string category, Func<Pawn, string?> provider, int priority, string modId, bool overrideExisting)

// ── ContextKeyRegistry 注册（推荐路径）──
ContextKeyRegistry.Register(string key, ContextLayer layer, float priority, Func<Pawn, string> provider, string ownerMod)

// ── AgentIdentity 注册 ──
RimMindAPI.RegisterAgentIdentityProvider(Func<Pawn, AgentIdentity?> provider)
RimMindAPI.GetAgentIdentity(Pawn pawn)

// ── Agent 扩展接口 ──
RimMindAPI.GetAgentProvider() / RegisterAgentProvider(IAgentProvider)
RimMindAPI.GetEventBus() / RegisterEventBus(IEventBus)

// ── Provider 查询 ──
RimMindAPI.GetProviderData(string category, Pawn pawn)
RimMindAPI.GetStaticProviderData(string category)
RimMindAPI.GetDynamicProviderData(string category, string query)
RimMindAPI.GetRegisteredCategories()

// ── Provider 卸载 ──
RimMindAPI.UnregisterModProviders(string modId) // 卸载某模组注册的所有 Provider

// ── UI 扩展 ──
RimMindAPI.RegisterSettingsTab(string tabId, Func<string> labelFn, Action<Rect> drawFn)
RimMindAPI.RegisterToggleBehavior(string id, Func<bool> isActive, Action toggle)

// ── 冷却控制 ──
RimMindAPI.RegisterModCooldown(string modId, Func<int> getCooldownTicks)

// ── 对话触发 ──
RimMindAPI.RegisterDialogueTrigger(Action<Pawn, string, Pawn?> triggerFn)
RimMindAPI.TriggerDialogue(Pawn pawn, string context, Pawn? recipient)

// ── SkipCheck（互斥控制）──
RimMindAPI.RegisterDialogueSkipCheck / ShouldSkipDialogue
RimMindAPI.RegisterFloatMenuSkipCheck / ShouldSkipFloatMenu
RimMindAPI.RegisterActionSkipCheck / ShouldSkipAction
RimMindAPI.RegisterStorytellerIncidentSkipCheck / ShouldSkipStorytellerIncident

// ── 请求审批悬浮窗 ──
RimMindAPI.RegisterPendingRequest(RequestEntry entry)
RimMindAPI.GetPendingRequests() / RemovePendingRequest(RequestEntry entry)

// ── Incident 回调 ──
RimMindAPI.RegisterIncidentExecutedCallback(Action callback)
RimMindAPI.NotifyIncidentExecuted()

// ── 状态查询 ──
RimMindAPI.IsConfigured() / IsAnyToggleActive() / ToggleAll()
RimMindAPI.InvalidateClientCache()
```

### AIRequest / AIResponse

```csharp
class AIRequest {
    string SystemPrompt;
    string UserPrompt;           // 单轮模式
    List<ChatMessage>? Messages; // 多轮模式（非 null 时忽略 UserPrompt）
    int MaxTokens;               // 默认 800
    float Temperature;           // 默认 0.7
    string RequestId;            // 格式："ModName_Purpose_Tick"
    string ModId;                // 模组标识，用于冷却分组
    int ExpireAtTicks;           // 过期时间，0=不过期
    bool UseJsonMode;            // 默认 true
    AIRequestPriority Priority;  // 默认 Normal
    int MaxRetryCount;           // 默认 -1（使用全局设置）
}

class AIResponse {
    bool Success;
    string Content;
    string Error;
    int TokensUsed;
    string RequestId;
    AIRequestState State;
    AIRequestPriority Priority;
    int QueuePosition, AttemptCount;
    long QueueWaitMs, ProcessingMs, HttpStatusCode;
    int RequestPayloadBytes;
    string CancelReason;
    string ToolCallsJson;        // 工具调用 JSON

    static AIResponse Failure(string requestId, string error);
    static AIResponse Ok(string requestId, string content, int tokens);
    static AIResponse Cancelled(string requestId, string reason);
}
```

### IAIClient

```csharp
interface IAIClient {
    Task<AIResponse> SendAsync(AIRequest request);
    bool IsConfigured();
    bool IsLocalEndpoint { get; }  // 本地端点串行处理
}
```

### Prompt 组装系统

#### StructuredPromptBuilder

流式构建 System Prompt，链式 API：

```csharp
var prompt = new StructuredPromptBuilder()
    .Role("你是一个 RimWorld 殖民者")
    .Goal("根据状态做出决策")
    .Process("1. 分析状态 2. 选择动作")
    .Constraint("只能选择候选列表中的动作")
    .Output("JSON 格式")
    .Example("{\"action\": \"assign_work\", ...}")
    .Fallback("选择 force_rest")
    .Build();

// 从翻译键前缀一键构建
StructuredPromptBuilder.FromKeyPrefix("RimMind.MyMod").Build();

// 转为 PromptSection 注入上下文系统
.ToSection(tag: "system_prompt", priority: PromptSection.PriorityCore)
```

#### PromptSection

```csharp
class PromptSection {
    string Tag;
    string Content;
    int Priority;        // 低=重要，不可裁剪
    int EstimatedTokens;
    Func<string, string>? Compress; // 压缩回调
    string? LayerTag;

    PromptSection Clone(); // 深拷贝，防止缓存污染

    const int PriorityCore = 0;
    const int PriorityCurrentInput = 1;
    const int PriorityKeyState = 3;
    const int PriorityMemory = 5;
    const int PriorityAuxiliary = 8;
    const int PriorityCustom = 10;
}
```

#### PromptBudget

```csharp
class PromptBudget {
    int TotalBudget = 4000;
    int ReserveForOutput = 800;
    int AvailableForInput;

    List<PromptSection> Compose(List<PromptSection> sections); // Clone 后按预算裁剪
    string ComposeToString(List<PromptSection> sections);
}
```

裁剪逻辑（两阶段）：压缩（Compress 回调）→ 裁剪（删除低优先级段）

### ContextEngine 上下文构建流程

```
1. L0_Static    → 缓存层，system_instruction + npc_identity + npc_commands + world_rules
2. L1_Baseline  → 版本化缓存 + Diff 注入，map_structure + pawn_base_info + ...
3. Diff         → [环境突变] 消息，L1 key 值变化时记录，Tick 过期后合并回 L1 block
4. L2_Environment → current_area + weather + time_of_day + nearby_pawns + ...
5. L3_State     → health + mood + current_job + combat_status + ...
6. L4_History   → HistoryManager 对话历史（按 Budget 调整轮数）
7. CurrentQuery → 当前用户输入
```

ContextDiff: `ExpireTick` 字段，默认 600 ticks（约 10 秒），`IsExpired(currentTick)` 判断过期。

BudgetScheduler: Score = W1 * GetEffectivePriority() + W2 * ComputeRelevance(scenario, key)

### AgentBus

线程安全的事件总线：
- `_handlers`: `ConcurrentDictionary<Type, List<Delegate>>`，每个 handler list 有独立锁
- `Subscribe/Unsubscribe`: 在 handler list 上加锁
- `Publish`: 主线程同步分发，遍历前取快照数组
- `PublishFromBackground`: 后台线程通过 `ConcurrentQueue` 入队，主线程 Tick 时消费

### PawnAgent

统一认知主体，Tick 驱动：
- `Perceive`: 从 PerceptionBuffer 取感知事件
- `Think`: 构建 AIRequest，通过 RimMindAPI.RequestStructuredAsync 发送
- `Act`: 解析 AI 响应，执行工具调用
- `Record`: 记录行为到 `_behaviorHistory`（Queue，O(1) 限容）

回调在主线程执行（`LongEventHandler.ExecuteWhenFinished`）。

### JsonTagExtractor

统一 AI 响应解析工具：

```csharp
T? result = JsonTagExtractor.Extract<T>(aiResponse, "TagName");
List<T> results = JsonTagExtractor.ExtractAll<T>(aiResponse, "TagName");
```

### RimMindCoreSettings

```csharp
class RimMindCoreSettings : ModSettings {
    AIProvider provider;
    string apiKey, apiEndpoint, modelName, player2RemoteUrl;
    bool forceJsonMode;
    int maxTokens;                // 默认 800
    float defaultTemperature;     // 默认 0.7，范围 0.0-2.0
    int maxConcurrentRequests;    // 默认 3
    int maxRetryCount;            // 默认 2
    int requestTimeoutMs;         // 默认 120000
    bool debugLogging;
    bool requestOverlayEnabled;
    float requestOverlayX/Y/W/H;
    ContextSettings Context;
    string customPawnPrompt, customMapPrompt;
    string telemetryDataPath, embeddingSnapshotPath, analysisReportPath;
    FlywheelAutoApplyMode autoApplyMode;
    float autoApplyConfidenceThreshold;
}
```

### ContextSettings

```csharp
class ContextSettings : IExposable {
    // 28 个 Include* bool 字段
    int MinSkillLevel;           // 默认 4
    float BudgetW1;              // 默认 0.4f，Pawn 信息权重
    float BudgetW2;              // 默认 0.6f，环境信息权重
    int ContextBudget;           // 总 Token 预算
    void ApplyPreset(ContextPreset preset); // 含 Custom no-op 分支
}
```

## 已知问题（审查 2026-04-26 第四轮）

> 详细列表见 `docs/06-problem/RimMind-Core/`

### P1 — 必须修复

1. **RimMindAPI 静态字典非线程安全**: `_pawnProviders`、`_actionSkipChecks` 等使用普通 `Dictionary`/`List`，后台线程读取与主线程写入存在竞态
2. **PawnAgent 硬编码请求参数**: `MaxTokens=400`、`Temperature=0.7f` 不使用用户设置（`Window_AgentDialogue` 已修复）

### P2 — 应修复

- 双路径注册导致数据不一致：`RimMindAPI._pawnProviders` 与 `ContextKeyRegistry._keys` 不同步
- `BudgetW1`/`BudgetW2` 无 UI 控件
- `CompressToBrief` 简单截断可能破坏 JSON/Unicode
- `BudgetScheduler.ComputeRelevance` 硬编码 0.6/0.4 混合权重
- `ContextDiff.DefaultLifetimeTicks=600` 过短（对低频 AI 请求）
- `FlywheelParameterStore._parameters` 非线程安全

### 已修复（本轮）

- ✅ GameComponent 未注册 → 已添加 `CoreGameComponent_Register` Patch
- ✅ 13 处空 catch 块 → 全部消除
- ✅ `autoApplyMode` 无 UI → 已添加 Checkbox + Slider
- ✅ `ShouldRefreshBalance` / `CheckAndUpdateThinking` 死代码 → 已删除

### 可扩展性限制

- `ContextEngine`、`PawnAgent`、`GameContextBuilder` 均为非 virtual/static，子 mod 无法继承定制
- `PerceptionPipeline` Filter 链在 `PawnAgent` 构造函数中硬编码
- `SchemaRegistry`/`ScenarioIds`/`RelevanceTable` 不可扩展

### Mod 集成注意事项

- **禁止**外部 mod 直接访问 `RimMindCoreMod.Settings`，应使用 `RimMindAPI.GetContextBudget()`
- **禁止**外部 mod 访问 `RimMind.Core.Internal` 命名空间（如 `AIRequestQueue.Instance`），应使用 `RimMindAPI.ClearModCooldown()`
- **禁止**外部 mod 直接使用 `CompPawnAgent.IsAgentActive`，应使用 `RimMindAPI.IsPawnAgentControlled`
- 外部 mod 注册 TaskInstruction 应通过 `RimMindAPI.RegisterTaskInstruction`（待添加），而非直接调用 `ContextKeyRegistry.Register`

## 线程安全规则

- **主线程**：读写游戏状态、消费 `ConcurrentQueue` 结果、所有 RimWorld/Unity API
- **后台线程**：HTTP 请求、JSON 解析
- **严禁**在后台线程调用任何 RimWorld/Unity API
- 异步回调通过 `LongEventHandler.ExecuteWhenFinished` 调度到主线程
- AgentBus: `Publish` 主线程同步，`PublishFromBackground` 后台入队主线程消费
- 后台线程日志通过 `AIRequestQueue.LogFromBackground()` 写入

## 数据流

```
游戏主线程 (Tick)
    │
    ├── 子模组触发条件满足
    │       ▼
    │   构建 AIRequest
    │       ▼
    │   RimMindAPI.RequestAsync(request, callback)
    │       ▼
    │   AIRequestQueue.Enqueue()
    │       ├── 检查冷却 → 跳过或接受
    │       ├── 检查过期 → 丢弃过期请求
    │       └── Task.Run → IAIClient.SendAsync()
    │                         ▼ (后台线程)
    │                       HTTP 请求 → 解析响应
    │                         ▼
    │                       _pendingFireResults.Enqueue()
    │
    ├── GameComponentTick()
    │       ▼
    │   消费 _pendingFireResults
    │       ├── 瞬态错误 → 重试
    │       └── 成功/最终失败 → LongEventHandler.ExecuteWhenFinished(() => callback(response))
    │
    │   AgentBus 消费后台队列
    │       ▼
    │   同步分发事件到 handlers
```

## 代码约定

### 命名空间

| 命名空间 | 目录 | 职责 |
|---------|------|------|
| `RimMind.Core` | Source/ 根目录 | Mod 入口、API |
| `RimMind.Core.Client` | Client/ | AI 客户端接口与数据结构 |
| `RimMind.Core.Client.OpenAI` | Client/OpenAI/ | OpenAI 兼容实现 |
| `RimMind.Core.Client.Player2` | Client/Player2/ | Player2 实现 |
| `RimMind.Core.Internal` | Core/ | 内部组件（队列、上下文构建、日志） |
| `RimMind.Core.Prompt` | Core/Prompt/ | Prompt 组装 |
| `RimMind.Core.Settings` | Settings/ | 设置 |
| `RimMind.Core.UI` | UI/ | 界面 |
| `RimMind.Core.Patch` | Patch/ | Harmony 补丁 |
| `RimMind.Core.Debug` | Debug/ | 调试动作 |

### 序列化

- `ModSettings` → `ExposeData()`，需调 `base.ExposeData()`
- `GameComponent` → `ExposeData()`，构造函数必须有 `(Game game)` 签名
- `ThingComp` → `PostExposeData()`（不是 ExposeData）
- `IExposable` 子对象用 `Scribe_Deep.Look`，集合用 `Scribe_Collections.Look`

### GameComponent 自动发现

RimWorld 用 `Activator.CreateInstance(type, game)` 创建 GameComponent，**必须保留 `(Game game)` 签名**。无参构造函数会导致存档加载时无法反序列化。

### UI 本地化

所有 UI 文本通过 `Languages/ChineseSimplified/Keyed/RimMind_Core.xml` 的 Keyed 翻译，禁止硬编码中文。代码中使用 `"Key".Translate()`。

### Harmony

- Harmony ID：`mcocdaa.RimMindCore`
- 优先使用 Postfix
- Patch 类放在 `Patch/` 目录

### 构建

| 配置项 | 值 |
|--------|-----|
| 目标框架 | `net48` |
| C# 语言版本 | 9.0 |
| Nullable | enable |
| RimWorld 版本 | 1.6 |
| 输出路径 | `../1.6/Assemblies/` |
| 部署 | 设置 `RIMWORLD_DIR` 环境变量后自动部署 |
| NuGet 依赖 | `Krafs.Rimworld.Ref 1.6.*-*`, `Lib.Harmony.Ref 2.*`, `Newtonsoft.Json 13.0.*` |

## 扩展指南（子模组开发）

### 1. 编译期引用

```xml
<Reference Include="RimMindCore">
  <HintPath>../../RimMind-Core/$(GameVersion)/Assemblies/RimMindCore.dll</HintPath>
  <Private>false</Private>
</Reference>
```

### 2. 注册 Provider

```csharp
public class MyMod : Mod
{
    public MyMod(ModContentPack content) : base(content)
    {
        RimMindAPI.RegisterPawnContextProvider("my_category", pawn =>
        {
            return $"[{pawn.Name.ToStringShort} 自定义信息]\n...";
        }, PromptSection.PriorityMemory, modId: "MyMod");
    }
}
```

### 3. 发起 AI 请求

```csharp
var request = new AIRequest
{
    SystemPrompt = "你是一个...",
    UserPrompt = "...",
    MaxTokens = 400,
    Temperature = 0.7f,
    RequestId = $"MyMod_{pawn.ThingID}",
    ModId = "MyMod",
};

RimMindAPI.RequestAsync(request, response =>
{
    if (!response.Success) { Log.Warning($"失败: {response.Error}"); return; }
    var result = JsonTagExtractor.Extract<MyDto>(response.Content, "MyTag");
});
```

### 4. 使用 StructuredPromptBuilder

```csharp
var systemPrompt = StructuredPromptBuilder.FromKeyPrefix("RimMind.MyMod").Build();
var section = StructuredPromptBuilder.FromKeyPrefix("RimMind.MyMod")
    .ToSection("my_system_prompt", PromptSection.PriorityCore);
```

### 5. 响应格式约定

所有子模组统一使用 `<TagName>{JSON}</TagName>` 格式：

```
让我分析一下当前局势...
<Advice>
{"action": "social_relax", "target": "Alice", "reason": "渴望社交"}
</Advice>
```

## AI 响应格式标准

| 子模组 | 标签 | JSON Schema |
|--------|------|------------|
| RimMind-Storyteller | `<Incident>` | `{"defName": string, "reason": string, "params": {...}?, "chain": {...}?}` |
| RimMind-Advisor | `<Advice>` | `{"advices": [{action, pawn?, target?, param?, reason, request_type?}]}` |
| RimMind-Personality | `<Personality>` | `{"thoughts": [{type, label, description, intensity, duration_hours?}], "narrative": string}` |
| RimMind-Dialogue | `<Thought>` | `{"reply": string, "thought": {"tag": string, "description": string}, "relation_delta"?: float}` |
