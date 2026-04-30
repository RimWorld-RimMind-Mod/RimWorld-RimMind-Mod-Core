# AGENTS.md — RimMind-Core

AI 客户端基础设施层，所有子模组的前置依赖。

## 项目定位

LLM 客户端(OpenAI+Player2)、异步请求队列、ContextEngine(L0-L4分层+Diff+BudgetScheduler)、Agent认知(PawnAgent+AgentBus+GoalStack)、NPC系统(NpcManager+IStorageDriver)、感知桥接(5个Patch)、数据飞轮(Flywheel)、SkipCheck互斥、审批悬浮窗、多分页设置UI。

## 构建

| 项 | 值 |
|----|-----|
| Target | net48, C#9.0, Nullable enable |
| RimWorld | 1.6 |
| Output | `../1.6/Assemblies/` |
| NuGet | Krafs.Rimworld.Ref 1.6.*, Lib.Harmony.Ref 2.*, Newtonsoft.Json 13.0.* |

## 源码结构

```
Source/
├── AICoreMod.cs / AICoreAPI.cs       Mod入口 + 静态公共API(RimMindAPI)
├── Client/                            OpenAI + Player2 客户端
├── Core/
│   ├── AIRequestQueue.cs             GameComponent异步队列
│   ├── Context/                       ContextEngine + KeyRegistry + BudgetScheduler + HistoryManager
│   ├── Agent/                         PawnAgent + AgentGoalStack + AgentBus + PerceptionPipeline
│   ├── Flywheel/                      FlywheelGameComponent + RuleEngine + ParameterStore
│   ├── Perception/PerceptionBridge.cs  感知桥接
│   └── Prompt/                        StructuredPromptBuilder + PromptSection + PromptBudget
├── Npc/                               NpcManager + StorageDriver(Local/Player2/Hybrid)
├── Comps/CompPawnAgent.cs            Agent ThingComp
├── Settings/                          AICoreSettings + ContextSettings
├── UI/                                SettingsUI + AgentDialogue + RequestOverlay
└── Patch/                             5个PerceptionBridge Patch + AITogglePatch + UIRoot
```

## 关键 API

```csharp
// 请求
RimMindAPI.RequestAsync(req, callback) / RequestStructuredAsync(req, schema, cb, tools)
RimMindAPI.Chat(ctxReq, ct) / CancelRequest(id) / PauseQueue() / ResumeQueue()

// Provider 注册(推荐路径)
ContextKeyRegistry.Register(key, layer, priority, pawn => content, ownerMod)

// 卸载
RimMindAPI.UnregisterModProviders(modId)

// UI扩展
RimMindAPI.RegisterSettingsTab(id, labelFn, drawFn)
RimMindAPI.RegisterToggleBehavior(id, isActive, toggle)

// SkipCheck互斥
RimMindAPI.RegisterDialogueSkipCheck / RegisterActionSkipCheck / RegisterFloatMenuSkipCheck

// 审批
RimMindAPI.RegisterPendingRequest(entry)
```

## 响应解析

统一使用 `<TagName>{JSON}</TagName>` → `JsonTagExtractor.Extract<T>(content, tag)` 解析。

## 代码约定

- Harmony ID: `mcocdaa.RimMindCore`，PostFix优先
- GameComponent 必须有 `(Game game)` 签名，RimWorld反射自动发现
- UI 文本通过 `Languages/*/Keyed/RimMind_Core.xml` Keyed翻译，禁止硬编码中文
- `ModSettings` → `ExposeData()` + `base.ExposeData()`；`ThingComp` → `PostExposeData()`
- 日志前缀 `[RimMind-Core]`

## 线程安全

- 主线程：读写游戏状态、消费ConcurrentQueue、所有RimWorld/Unity API
- 后台线程：HTTP请求、JSON解析，回调通过 `LongEventHandler.ExecuteWhenFinished` 调度回主线程
- AgentBus：Publish主线程同步，PublishFromBackground后台入队主线程消费
- **严禁**后台线程调用任何RimWorld/Unity API

## 已知问题（r6 审查 2026-04-29）

### P2 — 中等优先级

1. OpenAIClient._formatCapabilityCache 并发读写（static Dictionary）
2. LocalStorageDriver.KvStore 非线程安全（Dictionary，可从后台线程访问）
3. AICoreAPI.Chat 后台线程调用 RimWorld/Unity API（违反线程规则）
4. OpenAI 路径 Task.Run 无 try-catch（请求可能静默丢失）
5. RegisterIncidentExecutedCallback/Unregister key 生成不稳定（lambda 不可靠）
6. 单实例替换型 API 无覆盖警告（_dialogueTriggerFn 等 6 个字段）
7. ScenarioRegistry 硬编码中文（违反 UI 文本本地化规则）
8. PawnAgent/AgentGoalStack/NpcManager/PerceptionBridge 直接调用静态 AgentBus 绕过 IEventBus
9. HistoryManager 无持久化集成（存档/读档后对话历史丢失）
10. LocalStorageDriver.SupportsStreaming 返回 true 但实际假流式

### P3 — 低优先级

11. GameContextBuilder 威胁阈值不考虑难度缩放
12. ContextEngine.BuildL1 ContainsKey+索引器非原子
13. EmbedCache/SemanticEmbedding 非线程安全（独立使用时）
14. PawnAgent GUID 截断（32位熵，高频可能碰撞）
15. HybridStorageDriver 降级策略不完善（不处理超时/部分失败）
16. AgentBus 强引用存储 handler（可能内存泄漏）
17. ScenarioRegistry._scenarios 非线程安全
18. ContextKeyRegistry._coreRegistered 不可重置
19. StorageDriverFactory 无线程安全保护

### 设置项缺口（高优先级）

- ContextSnapshot/ContextRequest/AIRequest 默认 MaxTokens=400 与 Settings.maxTokens=800 不一致
- BudgetW1/BudgetW2 双源定义冲突（ContextSettings vs FlywheelParameterStore）
- PawnAgent 核心参数硬编码（ThinkCooldownTicks、DefaultTickInterval、MaxToolCallDepth）
- 感知管线参数硬编码（缓冲区容量、冷却时间、重要性阈值）

### Mod 结合度

- 3 个子 mod 直接访问 RimMindCoreMod.Settings
- 2 个子 mod 直接访问 AIRequestQueue.Instance
- 3 个子 mod 直接访问 RimMind.Core.Internal 命名空间
- Memory mod 复用其他 mod 的 ScenarioId

### 死代码（27 项）

- IStreamingResponseHandler 整套机制（接口+3个API+后端字段）
- IAgentModeProvider 整套机制（接口+3个API+后端字段）
- 15 个 RimMindAPI 方法/属性无调用者
- MemoryEvent 从未被实例化
- RequestOverlay.GetWindowRect/SetWindowRect 无调用者
- using System.Text 未使用

### 历史修复（r5-r9）

- ✅ RimMindAPI 静态字典 → ConcurrentDictionary
- ✅ AICoreAPI 8 个 List → ConcurrentDictionary
- ✅ ContextEngine/AIRequestQueue/ContextKeyRegistry/HistoryManager → 线程安全
- ✅ PawnAgent 硬编码参数 → 改用 Settings
- ✅ 双路径注册、Unicode 截断、ExposeData 快照 → 修复
- ✅ BudgetW1/W2 UI、autoApplyMode LogOnly → 修复
- ✅ ContextDiff lifetime、AIDebugLog O(1)、HistoryManager/SensorManager 线程安全 → 修复

## 操作边界

### ✅ 必须做
- 修改 `RimMindAPI` 后检查所有子模组调用方
- 修改 `ContextEngine`/`ContextKeyRegistry` 后验证已注册Provider兼容性
- 修改序列化字段后保持旧 Scribe key 向后兼容
- AI请求参数用 `AICoreSettings` 值，禁止硬编码

### ⚠️ 先询问
- 修改 `RimMindAPI` 静态字典线程模型
- 修改 `ContextLayer` 枚举层级(影响所有子模组上下文注入顺序)
- 修改 `ScenarioIds`(影响所有子模组上下文过滤)

### 🚫 绝对禁止
- 子模组访问 `RimMind.Core.Internal` 命名空间
- 子模组直接访问 `RimMindCoreMod.Settings`(用 `RimMindAPI.GetContextBudget()`)
- 后台线程调用任何RimWorld/Unity API
- 修改 `Newtonsoft.Json` 版本(RimWorld内置)
