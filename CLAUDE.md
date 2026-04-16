# AGENTS.md — RimMind-Core

本文件供 AI 编码助手阅读，描述 RimMind-Core 的架构、代码约定和扩展模式。

## 项目定位

RimMind-Core 是 RimMind AI 模组套件的核心基础设施层。所有子模组（Actions、Personality、Advisor、Memory 等）均依赖本模组。职责：

1. **LLM 客户端**：OpenAI Chat Completions 兼容，通过 `UnityWebRequest` 发送
2. **异步请求队列**：后台线程发请求，主线程回调，`ConcurrentQueue` 桥接
3. **游戏上下文构建**：将游戏状态打包为中文文本，供 AI Prompt 使用
4. **Prompt 组装系统**：`StructuredPromptBuilder` + `PromptSection` + `PromptBudget` + `ContextComposer`，支持优先级排序与 Token 预算裁剪
5. **Provider 注册机制**：子模组通过注册 API 注入上下文段，实现解耦
6. **请求审批悬浮窗**：`RequestOverlay` + `RequestEntry`，子模组可注册待审批请求供玩家选择
7. **设置 UI**：多分页设置界面，子模组可注册额外分页
8. **调试工具**：AI Debug Log 窗口、Dev DebugAction

## 源码结构

```
Source/
├── AICoreMod.cs              Mod 入口，注册 Harmony，持有 Settings 单例
├── AICoreAPI.cs              静态公共 API（RimMindAPI），供子模组调用
├── Client/
│   ├── IAIClient.cs          AI 客户端接口
│   ├── AIRequest.cs          请求数据结构（含 ChatMessage 多轮支持）
│   ├── AIResponse.cs         响应数据结构
│   └── OpenAI/
│       ├── OpenAIClient.cs   OpenAI 兼容客户端实现
│       └── OpenAIDto.cs      请求/响应 DTO
├── Core/
│   ├── AIRequestQueue.cs     GameComponent，异步请求队列 + 冷却管理
│   ├── GameContextBuilder.cs 静态工具类，构建地图/Pawn 上下文文本
│   ├── JsonTagExtractor.cs   从 AI 响应提取 <Tag>JSON</Tag> 内容
│   ├── AIDebugLog.cs         GameComponent，存储最近 200 条请求记录
│   └── Prompt/
│       ├── StructuredPromptBuilder.cs  流式 Prompt 构建器（链式 API）
│       ├── PromptSection.cs            Prompt 段落（Tag/Content/Priority/EstimatedTokens）
│       ├── PromptBudget.cs             Token 预算管理（按优先级裁剪）
│       └── ContextComposer.cs          段落排序 + 历史压缩
├── Settings/
│   ├── AICoreSettings.cs     模组设置（API 配置 + 性能 + 调试）
│   └── ContextSettings.cs    上下文过滤器（控制哪些字段注入 Prompt）
├── UI/
│   ├── AICoreSettingsUI.cs   多分页设置界面
│   ├── Window_AIDebugLog.cs  AI Debug Log 浮动窗口
│   ├── RequestOverlay.cs     请求审批悬浮窗（可拖拽/可缩放）
│   ├── RequestEntry.cs       悬浮窗请求条目数据结构
│   ├── Window_RequestLog.cs  请求日志窗口
│   └── SettingsUIHelper.cs   设置 UI 辅助工具类
├── Patch/
│   ├── AITogglePatch.cs      右下角 AI 图标按钮注入
│   └── Patch_UIRoot_OnGUI.cs 每帧调用 RequestOverlay.OnGUI
└── Debug/
    └── AICoreDebugActions.cs Dev 菜单调试动作
```

## 关键类与 API

### RimMindAPI（AICoreAPI.cs）

所有子模组通过此静态类与 Core 交互：

```csharp
// 发起 AI 请求
RimMindAPI.RequestAsync(AIRequest request, Action<AIResponse> onComplete)
RimMindAPI.RequestImmediate(AIRequest request, Action<AIResponse> onComplete) // 绕过队列/冷却

// 上下文构建
RimMindAPI.BuildMapContext(Map map, bool brief = false)
RimMindAPI.BuildPawnContext(Pawn pawn)
RimMindAPI.BuildStaticContext()
RimMindAPI.BuildHistoryContext(int maxEntries = 10)
RimMindAPI.BuildFullPawnPrompt(Pawn pawn, string? currentQuery, string[]? excludeProviders)
RimMindAPI.BuildFullPawnPrompt(Pawn pawn, PromptBudget budget, string? currentQuery, string[]? excludeProviders)
RimMindAPI.BuildFullPawnSections(Pawn pawn, string? currentQuery, string[]? excludeProviders)

// Provider 注册（子模组在 Mod 构造时调用）
RimMindAPI.RegisterStaticProvider(string category, Func<string> provider, int priority = PromptSection.PriorityAuxiliary)
RimMindAPI.RegisterDynamicProvider(string category, Func<string, string> provider, int priority = PromptSection.PriorityAuxiliary)
RimMindAPI.RegisterPawnContextProvider(string category, Func<Pawn, string?> provider, int priority = PromptSection.PriorityAuxiliary)

// UI 扩展
RimMindAPI.RegisterSettingsTab(string tabId, Func<string> labelFn, Action<Rect> drawFn)
RimMindAPI.RegisterToggleBehavior(string id, Func<bool> isActive, Action toggle)

// 冷却控制
RimMindAPI.RegisterModCooldown(string modId, Func<int> getCooldownTicks)
RimMindAPI.GetModCooldownGetter(string modId)

// 对话触发
RimMindAPI.RegisterDialogueTrigger(Action<Pawn, string, Pawn?> triggerFn)
RimMindAPI.TriggerDialogue(Pawn pawn, string context, Pawn? recipient)
RimMindAPI.CanTriggerDialogue // getter

// 请求审批悬浮窗
RimMindAPI.RegisterPendingRequest(RequestEntry entry)
RimMindAPI.GetPendingRequests()
RimMindAPI.RemovePendingRequest(RequestEntry entry)

// 状态查询
RimMindAPI.IsConfigured()
RimMindAPI.IsAnyToggleActive()
RimMindAPI.ToggleAll()
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
    bool UseJsonMode;            // 默认 true，设 false 绕过 response_format
}

class AIResponse {
    bool Success;
    string Content;
    string Error;
    int TokensUsed;
    string RequestId;
    static AIResponse Failure(string requestId, string error);
    static AIResponse Ok(string requestId, string content, int tokens);
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
```

翻译键版本：`RoleFromKey()`, `GoalFromKey()` 等，配合 Keyed 翻译系统使用。

#### PromptSection

```csharp
class PromptSection {
    string Tag;          // 段落标识
    string Content;      // 段落内容
    int Priority;        // 优先级（低=重要，不可裁剪）
    int EstimatedTokens; // 估算 Token 数

    // 优先级常量
    const int PriorityCore = 0;         // 核心指令，不可裁剪
    const int PriorityCurrentInput = 1; // 当前输入
    const int PriorityKeyState = 3;     // 关键状态
    const int PriorityMemory = 5;       // 记忆上下文
    const int PriorityAuxiliary = 8;    // 辅助上下文
    const int PriorityCustom = 10;      // 自定义内容

    bool IsTrimable => Priority > PriorityCore;
    static int EstimateTokens(string text); // 混合 CJK/Latin 估算
}
```

#### PromptBudget

```csharp
class PromptBudget {
    int TotalBudget = 4000;       // 总 Token 预算
    int ReserveForOutput = 800;   // 为输出预留
    int AvailableForInput;        // 可用于输入的 Token 数

    List<PromptSection> Compose(List<PromptSection> sections); // 按预算裁剪
    string ComposeToString(List<PromptSection> sections);      // 裁剪后拼接
}
```

裁剪逻辑：从最高优先级（最不重要）开始删除，直到总 Token 数在预算内。

#### ContextComposer

```csharp
static class ContextComposer {
    List<PromptSection> Reorder(List<PromptSection> sections); // 按优先级排序
    string BuildFromSections(List<PromptSection> sections);    // 排序后拼接
    string CompressHistory(string historyText, int maxLines, string summaryLine); // 历史压缩
}
```

### BuildFullPawnPrompt 组装顺序

```
1. 静态段  → RegisterStaticProvider 注册的段（Rules、Skills 等）
2. Pawn段  → RegisterPawnContextProvider 注册的段（人格、记忆等）
3. 游戏状态 → BuildPawnContext(pawn)（客观游戏状态）
4. 地图状态 → BuildMapContext(pawn.Map)
5. 动态段  → RegisterDynamicProvider 注册的段（Memory 语义检索等）
```

所有段落以 `PromptSection` 形式传递，按 `Priority` 排序后拼接。带 `PromptBudget` 的版本会在拼接前裁剪。

### JsonTagExtractor

统一 AI 响应解析工具。所有子模组应使用 `<TagName>{JSON}</TagName>` 格式：

```csharp
T? result = JsonTagExtractor.Extract<T>(aiResponse, "TagName");
List<T> results = JsonTagExtractor.ExtractAll<T>(aiResponse, "TagName");
string? raw = JsonTagExtractor.ExtractRaw(aiResponse, "TagName");
List<string> allRaw = JsonTagExtractor.ExtractAllRaw(aiResponse, "TagName");
```

### RequestEntry / RequestOverlay

请求审批悬浮窗系统，供子模组注册需要玩家决策的请求：

```csharp
class RequestEntry {
    string source;          // 来源模组标识
    Pawn? pawn;             // 相关小人
    string title;           // 请求标题
    string? description;    // 请求描述
    string[] options;       // 选项列表
    Action<string>? callback; // 选择回调（参数为选中的选项文本）
    bool systemBlocked;     // 是否被系统拦截
    int tick;               // 创建时间
    int expireTicks;        // 过期 tick 数
}
```

过期后自动触发最后一个选项（视为"忽略"）。

### SettingsUIHelper

设置 UI 辅助工具类：

```csharp
SettingsUIHelper.DrawSectionHeader(listing, "标题");
SettingsUIHelper.DrawCustomPromptSection(listing, "标签", ref prompt, height);
SettingsUIHelper.SplitContentArea(inRect);  // 分割内容区域
SettingsUIHelper.SplitBottomBar(inRect);    // 分割底部栏
SettingsUIHelper.DrawBottomBar(barRect, onReset); // 重置按钮
```

## 线程安全规则

- **主线程**：读写游戏状态、消费 `ConcurrentQueue` 结果、所有 RimWorld/Unity API
- **后台线程**：HTTP 请求、JSON 解析、生产 `ConcurrentQueue` 结果
- **严禁**在后台线程调用任何 RimWorld/Unity API
- 后台线程日志必须通过 `AIRequestQueue.LogFromBackground()` 写入，主线程 Tick 时输出

## 数据流

```
游戏主线程 (Tick)
    │
    ├── 子模组触发条件满足
    │       ▼
    │   构建 AIRequest（SystemPrompt + UserPrompt）
    │       ▼
    │   RimMindAPI.RequestAsync(request, callback)
    │       ▼
    │   AIRequestQueue.Enqueue()
    │       ├── 检查冷却 → 跳过或接受
    │       └── Task.Run → OpenAIClient.SendAsync()
    │                         ▼ (后台线程)
    │                       HTTP 请求 → 解析响应
    │                         ▼
    │                       _results.Enqueue((response, callback))
    │
    ├── GameComponentTick()
    │       ▼
    │   消费 _results 队列
    │       ▼
    │   callback(response)  ← 主线程安全
    │       ▼
    │   子模组处理响应（解析 JSON、执行动作等）
    └── ...
```

## RimMind 套件架构

```
                    ┌─────────────────┐
                    │    Harmony      │
                    └────────┬────────┘
                             │
                    ┌────────▼────────┐
                    │  RimMind-Core   │
                    └──┬──┬──┬──┬──┬─┘
                       │  │  │  │  │
          ┌────────────┘  │  │  │  └──────────────┐
          │               │  │  │                  │
   ┌──────▼──────┐  ┌─────▼──┐ │  ┌───────────────▼──────┐
   │RimMind-     │  │RimMind-│ │  │ RimMind-Personality   │
   │Actions      │  │Memory  │ │  └──────────────────────┘
   └──────┬──────┘  └────────┘ │
          │                    │
   ┌──────▼──────┐    ┌───────▼──────┐
   │RimMind-     │    │RimMind-      │
   │Advisor      │    │Dialogue      │
   └─────────────┘    └──────────────┘
                             │
                    ┌────────▼────────┐
                    │RimMind-         │
                    │Storyteller      │
                    └─────────────────┘
```

### 上下文注入方式

| 子模组 | 注入方式 | 注册的 Provider |
|--------|---------|----------------|
| RimMind-Personality | `RegisterPawnContextProvider` | personality_profile + personality_state + personality_shaping |
| RimMind-Memory | `RegisterPawnContextProvider` + `RegisterStaticProvider` | memory_pawn + memory_narrator |
| RimMind-Dialogue | `RegisterPawnContextProvider` | dialogue_state + dialogue_relation |
| RimMind-Advisor | `RegisterPawnContextProvider` | advisor_history |
| RimMind-Actions | 以记忆方式注入（通过 Memory） | - |
| RimMind-Storyteller | 不注入上下文 | - |

### 数据依赖关系

```
(人格, 记忆) → 想法 → 行动 或 对话
    ↑           ↑
    └── 想法和记忆反哺人格
```

- 人格通过 `RegisterPawnContextProvider` 注入上下文
- 想法自动打包进入上下文（Thought 系统）
- 记忆通过 `RegisterPawnContextProvider` + `RegisterStaticProvider` 注入上下文
- 行动以记忆方式注入上下文
- 对话状态通过 `RegisterPawnContextProvider` 注入上下文
- Advisor 历史通过 `RegisterPawnContextProvider` 注入上下文

## 代码约定

### 命名空间

- `RimMind.Core` — 顶层（Mod 入口、API）
- `RimMind.Core.Client` — AI 客户端
- `RimMind.Core.Client.OpenAI` — OpenAI 实现
- `RimMind.Core.Internal` — 内部组件（队列、上下文构建、日志、JSON 提取）
- `RimMind.Core.Prompt` — Prompt 组装（段落、预算、排序、结构化构建）
- `RimMind.Core.Settings` — 设置
- `RimMind.Core.UI` — 界面
- `RimMind.Core.Patch` — Harmony 补丁
- `RimMind.Core.Debug` — 调试动作

### 序列化

- `ModSettings` → `ExposeData()`，需调 `base.ExposeData()`
- `GameComponent` → `ExposeData()`
- `ThingComp` → `PostExposeData()`（不是 ExposeData）
- `WorldComponent` → `ExposeData()`

### GameComponent 自动发现

GameComponent / WorldComponent 不需要 XML 注册。RimWorld 自动扫描并实例化，前提是构造函数签名正确：

```csharp
public AIRequestQueue(Game game) { _instance = this; }
```

RimWorld 1.6 的 GameComponent 基类无参构造，但 `Game.InitNewGame` 仍用 `Activator.CreateInstance(type, game)`，所以必须保留 `(Game game)` 签名。

### UI 本地化

所有 UI 文本通过 `Languages/ChineseSimplified/Keyed/RimMind.xml` 的 Keyed 翻译，禁止硬编码中文。代码中使用 `"Key".Translate()`。

### Harmony

- Harmony ID：`mcocdaa.RimMindCore`
- 优先使用 PostFix
- Patch 类放在 `Patch/` 目录

### 构建

- 目标框架：`net48`
- C# 语言版本：9.0
- RimWorld 版本：1.6
- 输出路径：`../1.6/Assemblies/`
- 部署：设置 `RIMWORLD_DIR` 环境变量后自动部署

### 测试

- 单元测试项目：`Tests/`，使用 xUnit，目标 `net10.0`
- 测试纯逻辑层，不依赖 RimWorld
- 已有测试：`JsonTagExtractorTests`

## 扩展指南（子模组开发）

### 1. 编译期引用

在 `.csproj` 中引用 RimMindCore.dll（Private=false）：

```xml
<Reference Include="RimMindCore">
  <HintPath>../../RimMind-Core/$(GameVersion)/Assemblies/RimMindCore.dll</HintPath>
  <Private>false</Private>
</Reference>
```

### 2. 注册 Provider

在 Mod 构造函数中注册：

```csharp
public class MyMod : Mod
{
    public MyMod(ModContentPack content) : base(content)
    {
        RimMindAPI.RegisterPawnContextProvider("my_category", pawn =>
        {
            return $"[{pawn.Name.ToStringShort} 自定义信息]\n...";
        }, PromptSection.PriorityMemory);

        RimMindAPI.RegisterSettingsTab("my_tab", () => "我的设置", rect =>
        {
            // 绘制设置 UI
        });
    }
}
```

### 3. 发起 AI 请求

```csharp
var request = new AIRequest
{
    SystemPrompt = "你是一个...",
    UserPrompt = RimMindAPI.BuildFullPawnPrompt(pawn),
    MaxTokens = 400,
    Temperature = 0.7f,
    RequestId = $"MyMod_{pawn.ThingID}",
    ModId = "MyMod",
};

RimMindAPI.RequestAsync(request, response =>
{
    if (!response.Success) { Log.Warning($"失败: {response.Error}"); return; }
    var result = JsonTagExtractor.Extract<MyDto>(response.Content, "MyTag");
    // 处理结果...
});
```

### 4. 使用 StructuredPromptBuilder 构建 System Prompt

```csharp
var systemPrompt = new StructuredPromptBuilder()
    .RoleFromKey("RimMind.MyMod.Role")
    .GoalFromKey("RimMind.MyMod.Goal")
    .ProcessFromKey("RimMind.MyMod.Process")
    .ConstraintFromKey("RimMind.MyMod.Constraint")
    .OutputFromKey("RimMind.MyMod.Output")
    .ExampleFromKey("RimMind.MyMod.Example")
    .FallbackFromKey("RimMind.MyMod.Fallback")
    .Custom(customPrompt)
    .Build();
```

### 5. 注册请求审批

```csharp
RimMindAPI.RegisterPendingRequest(new RequestEntry
{
    source = "my_mod",
    pawn = pawn,
    title = "标题",
    description = "描述",
    options = new[] { "选项A", "选项B", "忽略" },
    expireTicks = 30000,
    callback = choice =>
    {
        if (choice == "选项A") { /* 处理 */ }
    }
});
```

### 6. 响应格式约定

所有子模组统一使用 `<TagName>{JSON}</TagName>` 格式，AI 可在标签前后输出思考过程：

```
让我分析一下当前局势...
<Advice>
{"action": "social_relax", "target": "Alice", "reason": "渴望社交"}
</Advice>
```

### 7. 冷却机制

- Core 层冷却：`globalCooldownTicks`（默认 3600），按 ModId 独立
- 子模组通过 `RegisterModCooldown` 注册自定义冷却 Getter
- DebugAction 可清除冷却：`AIRequestQueue.Instance?.ClearCooldown(modId)`

## AI 响应格式标准

| 子模组 | 标签 | JSON Schema |
|--------|------|------------|
| RimMind-Storyteller | `<Incident>` | `{"defName": string, "reason": string, "params": {...}?, "chain": {...}?}` |
| RimMind-Advisor | `<Advice>` | `{"advices": [{action, pawn?, target?, param?, reason, request_type?}]}` |
| RimMind-Personality | `<Personality>` | `{"thoughts": [{type, label, description, intensity, duration_hours?}], "narrative": string}` |
| RimMind-Dialogue | `<Thought>` | `{"reply": string, "thought": {"tag": string, "description": string}, "relation_delta"?: float}` |
