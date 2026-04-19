# AGENTS.md — RimMind-Core

本文件供 AI 编码助手阅读，描述 RimMind-Core 的架构、代码约定和扩展模式。

## 项目定位

RimMind-Core 是 RimMind AI 模组套件的核心基础设施层。所有子模组（Actions、Personality、Advisor、Memory 等）均依赖本模组。职责：

1. **LLM 客户端**：OpenAI Chat Completions 兼容，通过 `UnityWebRequest` 发送
2. **异步请求队列**：后台线程发请求，主线程回调，`ConcurrentQueue` 桥接
3. **游戏上下文构建**：将游戏状态打包为中文文本，供 AI Prompt 使用
4. **Prompt 组装系统**：`StructuredPromptBuilder` + `PromptSection` + `PromptBudget` + `ContextComposer`，支持优先级排序、压缩与 Token 预算裁剪
5. **Provider 注册机制**：子模组通过注册 API 注入上下文段，支持卸载与覆盖，实现解耦
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
│       └── OpenAIDto.cs      请求/响应 DTO（internal）
├── Core/
│   ├── AIRequestQueue.cs     GameComponent，异步请求队列 + 冷却管理
│   ├── GameContextBuilder.cs 静态工具类，构建地图/Pawn 上下文文本（含 Section 和精简版本）
│   ├── JsonTagExtractor.cs   从 AI 响应提取 <Tag>JSON</Tag> 内容
│   ├── AIDebugLog.cs         GameComponent，存储最近 200 条请求记录（含 AIDebugEntry）
│   └── Prompt/
│       ├── StructuredPromptBuilder.cs  流式 Prompt 构建器（链式 API + FromKeyPrefix + ToSection）
│       ├── PromptSection.cs            Prompt 段落（Tag/Content/Priority/EstimatedTokens/Compress）
│       ├── PromptBudget.cs             Token 预算管理（先压缩后裁剪）
│       └── ContextComposer.cs          段落排序 + 历史压缩
├── Settings/
│   ├── AICoreSettings.cs     模组设置（API 配置 + 性能 + 调试 + 流式）
│   └── ContextSettings.cs    上下文过滤器（21 个 Include* 字段 + ContextPreset 预设）
├── UI/
│   ├── AICoreSettingsUI.cs   多分页设置界面
│   ├── Window_AIDebugLog.cs  AI Debug Log 浮动窗口
│   ├── RequestOverlay.cs     请求审批悬浮窗（可拖拽/可缩放/可持久化位置）
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
// ── 请求 ──
RimMindAPI.RequestAsync(AIRequest request, Action<AIResponse> onComplete)
RimMindAPI.RequestImmediate(AIRequest request, Action<AIResponse> onComplete) // 绕过队列/冷却

// ── 上下文构建 ──
RimMindAPI.BuildMapContext(Map map, bool brief = false)
RimMindAPI.BuildPawnContext(Pawn pawn)
RimMindAPI.BuildStaticContext()
RimMindAPI.BuildHistoryContext(int maxEntries = 10)
RimMindAPI.BuildFullPawnPrompt(Pawn pawn, string? currentQuery, string[]? excludeProviders)
RimMindAPI.BuildFullPawnPrompt(Pawn pawn, PromptBudget budget, string? currentQuery, string[]? excludeProviders)
RimMindAPI.BuildFullPawnSections(Pawn pawn, string? currentQuery, string[]? excludeProviders)

// ── Provider 注册（子模组在 Mod 构造时调用）──
RimMindAPI.RegisterStaticProvider(string category, Func<string> provider, int priority, string modId = "", bool overrideExisting = true)
RimMindAPI.RegisterDynamicProvider(string category, Func<string, string> provider, int priority, string modId = "", bool overrideExisting = true)
RimMindAPI.RegisterPawnContextProvider(string category, Func<Pawn, string?> provider, int priority, string modId = "", bool overrideExisting = true)

// ── Provider 卸载 ──
RimMindAPI.UnregisterStaticProvider(string category)
RimMindAPI.UnregisterDynamicProvider(string category)
RimMindAPI.UnregisterPawnContextProvider(string category)
RimMindAPI.UnregisterModProviders(string modId) // 卸载某模组注册的所有 Provider

// ── UI 扩展 ──
RimMindAPI.RegisterSettingsTab(string tabId, Func<string> labelFn, Action<Rect> drawFn)
RimMindAPI.SettingsTabs // IReadOnlyList<(tabId, labelFn, drawFn)>
RimMindAPI.RegisterToggleBehavior(string id, Func<bool> isActive, Action toggle)
RimMindAPI.HasToggleBehaviors // getter

// ── 冷却控制 ──
RimMindAPI.RegisterModCooldown(string modId, Func<int> getCooldownTicks)
RimMindAPI.GetModCooldownGetter(string modId) // Func<int>?
RimMindAPI.ModCooldownGetters // IReadOnlyDictionary<string, Func<int>>

// ── 对话触发 ──
RimMindAPI.RegisterDialogueTrigger(Action<Pawn, string, Pawn?> triggerFn)
RimMindAPI.TriggerDialogue(Pawn pawn, string context, Pawn? recipient)
RimMindAPI.CanTriggerDialogue // getter

// ── 请求审批悬浮窗 ──
RimMindAPI.RegisterPendingRequest(RequestEntry entry)
RimMindAPI.GetPendingRequests() // IReadOnlyList<RequestEntry>
RimMindAPI.RemovePendingRequest(RequestEntry entry)

// ── 状态查询 ──
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

class ChatMessage {
    string Role;    // "system" / "user" / "assistant"
    string Content;
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

快捷方式：

```csharp
// 从翻译键前缀一键构建（自动拼接 .Role/.Goal/.Process/... 后缀）
StructuredPromptBuilder.FromKeyPrefix("RimMind.MyMod")

// 带翻译键表头的自定义段
.WithCustom(customText, "RimMind.MyMod.CustomHeader")

// 启用段落标签输出（[角色] [目标] ...）
.WithSectionLabels(true)

// 直接转为 PromptSection（用于注入上下文系统）
.ToSection(tag: "system_prompt", priority: PromptSection.PriorityCore)
```

#### PromptSection

```csharp
class PromptSection {
    string Tag;          // 段落标识
    string Content;      // 段落内容
    int Priority;        // 优先级（低=重要，不可裁剪）
    int EstimatedTokens; // 估算 Token 数
    Func<string, string>? Compress; // 压缩回调（非 null 时可在超预算时压缩而非删除）

    // 优先级常量
    const int PriorityCore = 0;         // 核心指令，不可裁剪
    const int PriorityCurrentInput = 1; // 当前输入
    const int PriorityKeyState = 3;     // 关键状态
    const int PriorityMemory = 5;       // 记忆上下文
    const int PriorityAuxiliary = 8;    // 辅助上下文
    const int PriorityCustom = 10;      // 自定义内容

    bool IsTrimable => Priority > PriorityCore;
    bool IsCompressible => Compress != null && IsTrimable;
    static int EstimateTokens(string text); // 混合 CJK/Latin 估算
}
```

#### PromptBudget

```csharp
class PromptBudget {
    int TotalBudget = 4000;       // 总 Token 预算
    int ReserveForOutput = 800;   // 为输出预留
    int AvailableForInput;        // 可用于输入的 Token 数

    PromptBudget()
    PromptBudget(int totalBudget, int reserveForOutput = 800)

    List<PromptSection> Compose(List<PromptSection> sections); // 按预算裁剪
    string ComposeToString(List<PromptSection> sections);      // 裁剪后拼接
}
```

裁剪逻辑（两阶段）：
1. **压缩阶段**：对 `IsCompressible` 的段，按优先级从高到低调用 `Compress` 回调，直到总 Token 在预算内
2. **裁剪阶段**：压缩后仍超预算，对 `IsTrimable` 的段按优先级从高到低删除，直到总 Token 在预算内

#### ContextComposer

```csharp
static class ContextComposer {
    List<PromptSection> Reorder(List<PromptSection> sections); // 按优先级排序
    string BuildFromSections(List<PromptSection> sections);    // 排序后拼接
    string CompressHistory(string historyText, int maxLines = 6, string summaryLine = ""); // 历史压缩
}
```

### GameContextBuilder

```csharp
static class GameContextBuilder {
    // 纯文本版本
    string BuildMapContext(Map map, bool brief = false);
    string BuildPawnContext(Pawn pawn);
    string BuildCompactPawnContext(Pawn pawn);       // 精简版
    string BuildHistoryContext(int maxEntries = 10);

    // PromptSection 版本（带压缩回调，可参与 PromptBudget 压缩）
    PromptSection BuildMapContextSection(Map map, bool brief = false);
    PromptSection BuildPawnContextSection(Pawn pawn);
    PromptSection BuildCompactPawnContextSection(Pawn pawn);
}
```

### BuildFullPawnPrompt 组装顺序

```
1. 静态段  → RegisterStaticProvider 注册的段（Rules、Skills 等）
2. Pawn段  → RegisterPawnContextProvider 注册的段（人格、记忆等）
3. 游戏状态 → BuildPawnContextSection(pawn)（带压缩回调）
4. 地图状态 → BuildMapContextSection(pawn.Map)（带压缩回调）
5. 动态段  → RegisterDynamicProvider 注册的段（Memory 语义检索等）
```

所有段落以 `PromptSection` 形式传递，按 `Priority` 排序后拼接。带 `PromptBudget` 的版本会先压缩再裁剪。

### JsonTagExtractor

统一 AI 响应解析工具。所有子模组应使用 `<TagName>{JSON}</TagName>` 格式：

```csharp
T? result = JsonTagExtractor.Extract<T>(aiResponse, "TagName");
List<T> results = JsonTagExtractor.ExtractAll<T>(aiResponse, "TagName");
string? raw = JsonTagExtractor.ExtractRaw(aiResponse, "TagName");
List<string> allRaw = JsonTagExtractor.ExtractAllRaw(aiResponse, "TagName");
```

### AIRequestQueue

```csharp
class AIRequestQueue : GameComponent {
    static AIRequestQueue Instance { get; }
    static void LogFromBackground(string msg, bool isWarning = false);

    void Enqueue(AIRequest request, Action<AIResponse> callback, IAIClient client);
    void EnqueueImmediate(AIRequest request, Action<AIResponse> callback, IAIClient client);

    int GetCooldownTicksLeft(string modId);
    int GetQueueDepth(string modId);
    void ClearCooldown(string modId);
    void ClearAllCooldowns();
    void ClearAllQueues();
    IReadOnlyDictionary<string, int> GetAllCooldowns();
    IReadOnlyDictionary<string, int> GetAllQueueDepths();
}
```

### AIDebugLog / AIDebugEntry

```csharp
class AIDebugLog : GameComponent {
    static AIDebugLog? Instance { get; }
    IReadOnlyList<AIDebugEntry> Entries { get; }
    void Clear();
    static void Record(AIRequest request, AIResponse response, int elapsedMs);
}

class AIDebugEntry {
    int GameTick;
    string Source;
    string ModelName;
    string FullSystemPrompt;
    string FullUserPrompt;
    string FullResponse;
    int ElapsedMs;
    int TokensUsed;
    bool IsError;
    string ErrorMsg;
    string FormattedTime; // 格式化时间
}
```

### RequestEntry / RequestOverlay

请求审批悬浮窗系统：

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

static class RequestOverlay {
    void Register(RequestEntry entry);
    IReadOnlyList<RequestEntry> Pending { get; }
    void Remove(RequestEntry entry);
    Rect GetWindowRect();       // 获取悬浮窗位置（持久化用）
    void SetWindowRect(Rect rect);
    void OnGUI();
}
```

过期后自动触发最后一个选项（视为"忽略"）。

### SettingsUIHelper

```csharp
static class SettingsUIHelper {
    DrawSectionHeader(Listing_Standard listing, string label);
    DrawCustomPromptSection(Listing_Standard listing, string label, ref string prompt, float height = 80f);
    SplitContentArea(inRect);  // 分割内容区域
    SplitBottomBar(inRect);    // 分割底部栏
    DrawBottomBar(barRect, onReset); // 重置按钮
}
```

### RimMindCoreSettings

```csharp
class RimMindCoreSettings : ModSettings {
    string apiKey;
    string apiEndpoint;
    string modelName;
    bool forceJsonMode;
    bool useStreaming;           // 流式响应（预留）
    int maxTokens;
    bool debugLogging;
    ContextSettings Context;     // 上下文过滤器
    string customPawnPrompt;
    string customMapPrompt;
    bool requestOverlayEnabled;
    float requestOverlayX, requestOverlayY, requestOverlayW, requestOverlayH;

    bool IsConfigured();
    override void ExposeData();
}
```

### ContextSettings / ContextPreset

```csharp
class ContextSettings : IExposable {
    // 21 个 Include* bool 字段控制上下文注入
    // 例：IncludePawnSkills, IncludePawnNeeds, IncludeMapWeather ...
    int MinSkillLevel;           // 技能显示阈值
    List<string> disabledProviders; // 禁用的 Provider category 列表

    void ExposeData();
    void ApplyPreset(ContextPreset preset);
}

enum ContextPreset { Minimal, Standard, Full, Custom }
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

| 命名空间 | 目录 | 职责 |
|---------|------|------|
| `RimMind.Core` | Source/ 根目录 | Mod 入口、API |
| `RimMind.Core.Client` | Client/ | AI 客户端接口与数据结构 |
| `RimMind.Core.Client.OpenAI` | Client/OpenAI/ | OpenAI 实现 |
| `RimMind.Core.Internal` | Core/ | 内部组件（队列、上下文构建、日志、JSON 提取） |
| `RimMind.Core.Prompt` | Core/Prompt/ | Prompt 组装（段落、预算、排序、结构化构建） |
| `RimMind.Core.Settings` | Settings/ | 设置 |
| `RimMind.Core.UI` | UI/ | 界面 |
| `RimMind.Core.Patch` | Patch/ | Harmony 补丁 |
| `RimMind.Core.Debug` | Debug/ | 调试动作 |

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

所有 UI 文本通过 `Languages/ChineseSimplified/Keyed/RimMind_Core.xml` 的 Keyed 翻译，禁止硬编码中文。代码中使用 `"Key".Translate()`。

### Harmony

- Harmony ID：`mcocdaa.RimMindCore`
- 优先使用 PostFix
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

### 测试

- 单元测试项目：`Tests/`，使用 xUnit，目标 `net10.0`
- 测试纯逻辑层，不依赖 RimWorld
- 测试文件直接 `<Compile Include>` 引用源码（不引用主项目 DLL）
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

在 Mod 构造函数中注册，传入 `modId` 以支持卸载：

```csharp
public class MyMod : Mod
{
    public MyMod(ModContentPack content) : base(content)
    {
        RimMindAPI.RegisterPawnContextProvider("my_category", pawn =>
        {
            return $"[{pawn.Name.ToStringShort} 自定义信息]\n...";
        }, PromptSection.PriorityMemory, modId: "MyMod");

        RimMindAPI.RegisterSettingsTab("my_tab", () => "我的设置", rect =>
        {
            // 绘制设置 UI
        });
    }
}
```

### 3. 卸载 Provider

模组卸载时清理注册：

```csharp
RimMindAPI.UnregisterModProviders("MyMod");
// 或按 category 卸载：
RimMindAPI.UnregisterPawnContextProvider("my_category");
```

### 4. 发起 AI 请求

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

### 5. 使用 StructuredPromptBuilder 构建 System Prompt

```csharp
// 方式一：逐项构建
var systemPrompt = new StructuredPromptBuilder()
    .RoleFromKey("RimMind.MyMod.Role")
    .GoalFromKey("RimMind.MyMod.Goal")
    .ProcessFromKey("RimMind.MyMod.Process")
    .ConstraintFromKey("RimMind.MyMod.Constraint")
    .OutputFromKey("RimMind.MyMod.Output")
    .ExampleFromKey("RimMind.MyMod.Example")
    .FallbackFromKey("RimMind.MyMod.Fallback")
    .Build();

// 方式二：从翻译键前缀一键构建
var systemPrompt = StructuredPromptBuilder.FromKeyPrefix("RimMind.MyMod")
    .Build();

// 方式三：转为 PromptSection 注入上下文系统
var section = StructuredPromptBuilder.FromKeyPrefix("RimMind.MyMod")
    .ToSection("my_system_prompt", PromptSection.PriorityCore);
```

### 6. 注册请求审批

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

### 7. 响应格式约定

所有子模组统一使用 `<TagName>{JSON}</TagName>` 格式，AI 可在标签前后输出思考过程：

```
让我分析一下当前局势...
<Advice>
{"action": "social_relax", "target": "Alice", "reason": "渴望社交"}
</Advice>
```

### 8. 冷却机制

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
