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

## 已知问题

### P1 — 高优先级

1. AICoreAPI 中 8 个 `List<T>` 非线程安全（`_settingsTabs`, `_toggleBehaviors`, `_incidentExecutedCallbacks`, `_storytellerIncidentSkipChecks`, `_parameterTuners`, `_sensorProviders`, `_agentModeProviders`, `_streamingHandlers`）
2. ContextEngine / AIRequestQueue / ContextKeyRegistry / HistoryManager 内部 `Dictionary` 非线程安全
3. PawnAgent.RequestToolFeedback + LocalStorageDriver + Player2StorageDriver 硬编码 `MaxTokens=400, Temperature=0.7f/0.8f`，未使用 `RimMindCoreMod.Settings`

### P2 — 中等优先级

4. 双路径注册（`RimMindAPI` 旧字典 vs `ContextKeyRegistry`）数据不同步
5. `ContextSettings.BudgetW1`/`BudgetW2` 无 UI 控件
6. `ContextDiff.DefaultLifetimeTicks=600` 对低频 AI 请求过短
7. `CompressToBrief` 的 200 字符截断可能破坏 Unicode 代理对
8. `FlywheelParameterStore.ExposeData` 中 Keys/Values 快照不一致

### 本轮已修复

- ✅ RimMindAPI 静态字典 → ConcurrentDictionary（_staticProviders, _dynamicProviders, _pawnProviders, _modCooldownGetters, _dialogueSkipChecks, _floatMenuSkipChecks, _actionSkipChecks）
- ✅ FlywheelParameterStore._parameters → ConcurrentDictionary
- ✅ PawnAgent.Think() 硬编码 MaxTokens/Temperature → 已改用 Settings
- ✅ FlywheelTelemetryCollector.RecordSnapshotBuild 死代码 → 已删除

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
