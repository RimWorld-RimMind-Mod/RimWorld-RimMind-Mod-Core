---
name: rimworld-mod-dev
description: This skill should be used when developing RimWorld mods, including creating C# Harmony patches, writing XML Defs, implementing AI integration for pawns or events, setting up mod structure, or debugging RimWorld mod issues. Activates for "rimworld mod", "harmony patch", "pawncomp", "incidentdef", "thinkTree", "storyteller", "jobgiver", "hediff", "defmodextension" topics.
version: 1.0.0
---

# RimWorld Mod Development Skill

This skill provides guidance for developing RimWorld mods, with a focus on C#/Harmony-based behavior modification and AI integration patterns.

---

## Project Context

- **Game version**: 1.5 / 1.6
- **Target framework**: .NET Framework 4.8 (net48)
- **Required dependency**: Harmony (brrainz.harmony)

---

## Mod Folder Structure

```
MyMod/
├── About/
│   ├── About.xml          # Required: mod metadata, dependencies, supported versions
│   └── Preview.png        # Required: 640x360 or 1280x720, < 1MB
├── Assemblies/            # Compiled DLL files
├── Defs/                  # XML definitions (ThingDef, IncidentDef, etc.)
├── Patches/               # PatchOperations XML (safe cross-mod patching)
├── Languages/             # Localization files
│   └── English/
│       └── Keyed/
│           └── Keys.xml   # Translation keys
├── Sounds/                # .ogg audio files
├── Textures/              # .png texture files (must be PoT: 32x32, 64x64, etc.)
├── Source/                # C# source code (not loaded by game)
│   └── MyMod/
│       ├── MyModMain.cs
│       └── ...
└── LoadFolders.xml        # Optional: conditional folder loading per version
```

---

## Core Technology Stack

| Tech | Purpose | Notes |
|------|---------|-------|
| **Harmony** | Runtime method patching | Use PostFix over Prefix to reduce conflicts |
| **C# async/await** | AI API calls | Never block game main thread |
| **ConcurrentQueue** | Thread-safe result passing | Main thread consumes on next Tick |
| **ThingComp** | Per-pawn state storage | Serialized via ExposeData |
| **DefModExtension** | Extend XML Def fields | No C# subclassing needed |
| **Newtonsoft.Json** | JSON parsing | Already bundled in RimWorld |

---

## AI Integration Pattern

```csharp
// 1. Harmony patch on TickManager - runs every game tick
[HarmonyPatch(typeof(TickManager), "DoSingleTick")]
public static class TickManagerPatch
{
    static void Postfix()
    {
        // Consume AI results from queue (main thread safe)
        while (AIRequestQueue.Results.TryDequeue(out var result))
            result.Apply();

        // Enqueue new requests (triggers async AI call)
        if (ShouldQueryAI())
            AIRequestQueue.Enqueue(new AIRequest(BuildContext()));
    }
}

// 2. Async AI call (runs on background thread)
public static async Task QueryAI(AIRequest request)
{
    var response = await httpClient.PostAsync(endpoint, content);
    var result = ParseResponse(await response.Content.ReadAsStringAsync());
    AIRequestQueue.Results.Enqueue(result);
}
```

---

## Key Game Systems & How to Hook Them

### Pawn Behavior (Jobs / ThinkTree)

```csharp
// Force a pawn to do a specific job
pawn.jobs.StartJob(new Job(JobDefOf.GotoWander, targetCell), JobCondition.InterruptForced);

// Hook into job giving (Harmony prefix on JobGiver_Work)
[HarmonyPatch(typeof(JobGiver_Work), "TryGiveJob")]
static void Postfix(Pawn pawn, ref Job __result)
{
    if (AIController.ShouldOverride(pawn))
        __result = AIController.GetAIJob(pawn);
}
```

### Incidents / Storyteller

```csharp
// Custom StorytellerComp - AI selects which incident to fire
public class StorytellerComp_AIDirector : StorytellerComp
{
    public override IEnumerable<FiringIncident> MakeIntervalIncidents(IIncidentTarget target)
    {
        var chosen = AIDirector.GetNextIncident(target); // reads from result queue
        if (chosen != null)
            yield return new FiringIncident(chosen, this);
    }
}
```

XML registration:
```xml
<StorytellerDef>
    <defName>AIDirectorStoryteller</defName>
    <comps>
        <li Class="MyMod.StorytellerComp_AIDirector" />
    </comps>
</StorytellerDef>
```

### Thoughts / Mood

```csharp
// Add a thought (affects mood)
pawn.needs.mood.thoughts.memories.TryGainMemory(ThoughtDefOf.SomeThought);

// Remove a thought
pawn.needs.mood.thoughts.memories.RemoveMemoriesOfDef(thoughtDef);
```

### Mental States

```csharp
// Trigger mental break
pawn.mindState.mentalStateHandler.TryStartMentalState(
    MentalStateDefOf.Berserk, forceWake: true);
```

### Faction Relations

```csharp
Faction.OfPlayer.TryAffectGoodwillWith(otherFaction, delta: 15,
    lookTarget: pawn, reason: HistoryEventDefOf.GiftsGiven);
```

---

## ThingComp Pattern (Per-Pawn State)

```csharp
// 1. Properties class
public class CompProperties_AIController : CompProperties
{
    public CompProperties_AIController() => compClass = typeof(CompAIController);
}

// 2. Comp class with save/load
public class CompAIController : ThingComp
{
    public bool AIEnabled = false;
    public string LastAIDecision = "";

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Values.Look(ref AIEnabled, "aiEnabled", false);
        Scribe_Values.Look(ref LastAIDecision, "lastAIDecision", "");
    }
}

// 3. XML patch to add comp to all Pawns
// Patches/AddAIComp.xml
```

```xml
<Patch>
  <Operation Class="PatchOperationAdd">
    <xpath>/Defs/ThingDef[thingClass="Pawn"]/comps</xpath>
    <value>
      <li Class="MyMod.CompProperties_AIController" />
    </value>
  </Operation>
</Patch>
```

---

## Harmony Patch Patterns

```csharp
// PostFix: runs after original method, can modify result
[HarmonyPatch(typeof(TargetClass), "MethodName")]
public static class MyPatch
{
    static void Postfix(ref ReturnType __result, Pawn __instance, OtherParam param)
    {
        // __result = modified return value
        // __instance = the object the method was called on
    }
}

// Prefix: runs before original, can skip original with return false
[HarmonyPatch(typeof(TargetClass), "MethodName")]
public static class MyPatch
{
    static bool Prefix(ref ReturnType __result, Pawn __instance)
    {
        if (ShouldSkip(__instance))
        {
            __result = myResult;
            return false; // skip original
        }
        return true; // run original
    }
}
```

---

## About.xml Template

```xml
<?xml version="1.0" encoding="utf-8"?>
<ModMetaData>
    <name>My Mod</name>
    <author>YourName</author>
    <packageId>YourName.MyMod</packageId>
    <supportedVersions>
        <li>1.5</li>
        <li>1.6</li>
    </supportedVersions>
    <description>My awesome mod.</description>
    <modDependencies>
        <li>
            <packageId>brrainz.harmony</packageId>
            <displayName>Harmony</displayName>
            <steamWorkshopUrl>steam://url/CommunityFilePage/2009463077</steamWorkshopUrl>
        </li>
    </modDependencies>
    <loadAfter>
        <li>brrainz.harmony</li>
    </loadAfter>
</ModMetaData>
```

---

## Mod Entry Point Pattern

```csharp
using HarmonyLib;
using Verse;

namespace MyMod
{
    public class MyModMain : Mod
    {
        public static MyModSettings Settings;

        public MyModMain(ModContentPack content) : base(content)
        {
            Settings = GetSettings<MyModSettings>();
            new Harmony("YourName.MyMod").PatchAll();
        }

        public override string SettingsCategory() => "My Mod";
        public override void DoSettingsWindowContents(Rect rect)
            => Settings.DoWindowContents(rect);
    }

    public class MyModSettings : ModSettings
    {
        public string ApiKey = "";
        public string Model = "gpt-4o-mini";
        public int CooldownTicks = 2500; // ~1 game hour

        public override void ExposeData()
        {
            Scribe_Values.Look(ref ApiKey, "apiKey", "");
            Scribe_Values.Look(ref Model, "model", "gpt-4o-mini");
            Scribe_Values.Look(ref CooldownTicks, "cooldownTicks", 2500);
        }
    }
}
```

---

## AI Context Building (Game State → Prompt)

Key game state to include in AI prompts:

```csharp
public static string BuildGameContext(Map map)
{
    var sb = new StringBuilder();
    // Colony stats
    sb.AppendLine($"Colonists: {map.mapPawns.FreeColonists.Count}");
    sb.AppendLine($"Food: {map.resourceCounter.GetCount(ThingDefOf.MealSimple)}");
    sb.AppendLine($"Threat: {StorytellerUtility.GetThreatBiggestGoodwill(Faction.OfPlayer)}");
    // Recent events (from WorldComponent history)
    // Current threats
    foreach (var raid in map.attackTargetsCache.TargetsHostileToColony)
        sb.AppendLine($"Threat: {raid.Thing.Label}");
    return sb.ToString();
}
```

---

## Common XML Def Types

| Def | Purpose |
|-----|---------|
| `ThingDef` (thingClass=Pawn) | Pawn/creature definition |
| `IncidentDef` | Events (raids, traders, etc.) |
| `StorytellerDef` | Storyteller configuration |
| `ThoughtDef` | Mood thoughts |
| `MentalStateDef` | Mental breaks |
| `JobDef` | Work job types |
| `HediffDef` | Health conditions/implants |
| `TraitDef` | Pawn traits |
| `InteractionDef` | Social interactions |
| `ThinkTreeDef` | AI behavior tree |
| `DutyDef` | Temporary behavior duties |

## Performance Guidelines

- AI calls: minimum 2500 ticks (~1 game hour) cooldown between calls per pawn
- Use `ConcurrentQueue` for all cross-thread data transfer
- Cache game state reads (don't traverse all pawns every tick)
- Use `ModsConfig.IsActive("packageId")` for conditional compatibility patches
- Limit AI context to ~500 tokens for routine decisions

---

## Debugging

```csharp
// Log to RimWorld debug log
Log.Message("[MyMod] Debug info here");
Log.Warning("[MyMod] Something unexpected");
Log.Error("[MyMod] Something broke");

// Dev mode actions (shows in debug menu)
[DebugAction("MyMod", "Trigger AI Event")]
static void TriggerAIEvent() { /* ... */ }
```

Enable dev mode in game: Options → Development Mode

---

## Pitfalls & Lessons Learned

### 1. UnityWebRequest 只能在主线程调用

**症状**：`UnityWebRequest.SendWebRequest()` 在 `Task.Run` 内调用时返回 HTTP 0，无任何错误日志。

**原因**：Unity 的 `UnityWebRequest` 不是线程安全的，必须在主线程发起。

**修复**：后台线程 HTTP 调用改用 `System.Net.Http.HttpClient`（net48 支持，线程安全）：

```csharp
private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

// 可在 Task.Run 内安全调用
using var req = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = content };
req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
using var res = await _httpClient.SendAsync(req);
```

---

### 2. OpenAI 兼容 API 的 FormatEndpoint 三段式逻辑

用户填写的 endpoint 格式不一，需统一处理：

```csharp
private static string FormatEndpoint(string baseUrl)
{
    if (string.IsNullOrEmpty(baseUrl)) return string.Empty;
    string trimmed = baseUrl.Trim().TrimEnd('/');
    // 已是完整 endpoint → 不变
    if (trimmed.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
        return trimmed;
    var uri = new Uri(trimmed);
    string path = uri.AbsolutePath.Trim('/');
    // 有版本路径（如 /v1）→ 只追加 /chat/completions
    if (!string.IsNullOrEmpty(path))
        return trimmed + "/chat/completions";
    // 裸域名 → 追加 /v1/chat/completions
    return trimmed + "/v1/chat/completions";
}
```

| 输入 | 输出 |
|------|------|
| `https://api.deepseek.com` | `https://api.deepseek.com/v1/chat/completions` |
| `https://api.deepseek.com/v1` | `https://api.deepseek.com/v1/chat/completions` |
| `https://api.deepseek.com/v1/chat/completions` | 原样返回 |

---

### 3. net48 中禁止使用 `dynamic` 类型

**症状**：编译报错，缺少 `Microsoft.CSharp` 引用。

**替代方案**：用 Newtonsoft.Json 的 `JObject`：

```csharp
// 不要用 dynamic
// dynamic obj = JsonConvert.DeserializeObject(text); // 编译失败

// 改用 JObject
var jobj = JObject.Parse(text);
string reply = jobj["choices"]?[0]?["message"]?["content"]?.ToString() ?? "";
int tokens = jobj["usage"]?["total_tokens"]?.Value<int>() ?? 0;
```

---

### 4. RimWorld 纹理必须是 2 的幂次方（PoT）

**症状**：`Texture X.png is being reloaded with reduced mipmap count (clamped to 2) due to non-power-of-two dimensions: (24x24)`

**原因**：RimWorld（Unity）要求 UI 纹理尺寸为 2 的幂次方（16、32、64…）。

**规则**：
- 不要用游戏内置的非 PoT 纹理（如 `UI/Buttons/Info` 是 24×24）
- 自己提供纹理放在 `Textures/UI/MyMod/Icon.png`，使用 32×32 或 64×64

```csharp
// 错误：复用游戏内置 24×24 纹理
ContentFinder<Texture2D>.Get("UI/Buttons/Info", reportFailure: false)

// 正确：使用自己的 32×32 纹理
ContentFinder<Texture2D>.Get("UI/MyMod/Icon", reportFailure: false)
```

---

### 5. `[DebugAction]` Dev 菜单搜索按动作名，不按分类名

Dev 菜单搜索框只匹配**动作名称**，不匹配 category 参数。

```csharp
// category="MyMod", name="Test API Connection"
[DebugAction("MyMod", "Test API Connection", actionType = DebugActionType.Action)]
```

- 搜索 `"MyMod"` → **找不到**
- 搜索 `"Test API"` → **可以找到**
- 或者在 Actions 列表滚动到 "MyMod" 分类下查看

---

### 6. Keyed XML 不会产生翻译错误

"Translation data has X errors" 这条日志**只来自 `DefInjected/` 翻译文件**（key 与 Def 不匹配时触发）。

`Languages/*/Keyed/*.xml` 无论多余还是缺失 key 都**不会**触发该错误：
- 多余的 key → 静默忽略
- 缺失的 key → 代码中显示 key 原文字符串

如果看到翻译错误，首先检查其他 mod 的 DefInjected 目录，而非自己的 Keyed 文件。

---

### 7. WorkGiver 三模式扫描：用 `wg.def.scanCells` 区分，而非类型判断

**背景**：枚举某 WorkType 可执行目标时，WorkGiver 有三种工作模式。

**错误做法**：`if (scanner is WorkGiver_Grower)` — 只覆盖 Growing，漏掉 SmoothWall、BuildRoof、RemoveRoof、ClearSnow、PaintFloor 等 8 个格子类 WorkGiver。

**正确做法**：用 `wg.def.scanCells` 属性区分，与具体类型无关：

```csharp
foreach (var wg in workType.workGiversByPriority)
{
    if (wg.Worker is not WorkGiver_Scanner scanner) continue;

    if (wg.scanCells)
    {
        // 格子类：Growing / SmoothWall / BuildRoof / RemoveRoof /
        //          ClearSnow / PaintFloor / ClearPollution 等
        var cells = scanner.PotentialWorkCellsGlobal(pawn);
        foreach (var cell in cells)
        {
            if (scanner.HasJobOnCell(pawn, cell, false))
                // 处理 cell 目标
        }
    }
    else
    {
        // 物体类（默认）：Mining / Hauling / Tend / Hunt 等
        // DoBill 工作台（Cooking / Crafting / Smithing）也在此分支，
        // 额外通过 IBillGiver.BillStack.FirstShouldDoNow 获取账单名称
        var things = scanner.PotentialWorkThingsGlobal(pawn);
        foreach (var thing in things)
        {
            if (scanner.HasJobOnThing(pawn, thing, false))
                // 处理 thing 目标
        }
    }
}
```

`IBillGiver` 账单名称：
```csharp
if (thing is IBillGiver billGiver)
{
    var bill = billGiver.BillStack?.FirstShouldDoNow;
    label = bill != null ? $"{thing.LabelShort} → {bill.LabelCap}" : thing.LabelShort;
}
```

所有 WorkGiver 调用包在 `try-catch` 内，部分 WorkGiver 在特定地图状态下会抛异常。

---

### 8. 插件 API 可扩展分类：C# 8 接口默认方法代替硬编码 HashSet

**问题**：`ExecuteBatch` 需要知道哪些动作是 Job 类（需要 `requestQueueing`），原实现用硬编码 HashSet：
```csharp
// 脆弱：外部 mod 注册的 Job 类动作不在此集合中
private static readonly HashSet<string> _jobBasedIntents = new() { "force_rest", "assign_work", ... };
```

**修复**：用 C# 8 接口默认方法声明扩展点：
```csharp
public interface IActionRule
{
    bool IsJobBased => false;  // 默认非 Job 类；Job 类实现覆盖为 true
    bool Execute(Pawn actor, Pawn? target, string? param, bool requestQueueing = false);
}

// 内置 Job 类动作
public class ForceRestAction : IActionRule
{
    public bool IsJobBased => true;
    // ...
}

// ExecuteBatch 直接读属性，无需查 HashSet
bool isJobAction = rule.IsJobBased;
```

**优点**：外部 mod 注册自定义 Job 类动作时，只需在自己的类中声明 `public bool IsJobBased => true;`，框架代码无需改动。

---

### 9. RimWorld AI 空闲判断：GotoWander ≠ 游戏空闲，但 = AI 可接管

**问题**：`pawn.jobs.curJob == null` 不足以判断"AI 是否可以重定向小人"。

- `GotoWander`：小人在漫步，`curJob != null`，游戏认为不空闲
- `playerForced = true`：玩家手动命令的 Job，AI 不应打断

**正确的 AI 可接管判断**：

```csharp
public static bool IsAdvisorIdle(Pawn pawn)
{
    var job = pawn.jobs?.curJob;
    if (job == null) return true;
    if (job.playerForced) return false;  // 玩家命令优先

    // 这些 Job 类型 AI 可安全打断
    var def = job.def;
    return def == JobDefOf.Wait
        || def == JobDefOf.Wait_Wander
        || def == JobDefOf.GotoWander
        || def == JobDefOf.Wait_MaintainPosture;
}
```

调试输出应明确区分：`"是否空闲（AI 可接管）"` 而非 `"游戏空闲"`，避免概念混淆。

---

### 10. ModSettings 孤儿条目：warn 但不删除，保留以备 mod 重启恢复

**场景**：玩家禁用了某外部 mod 的某动作（存入 `DisabledIntents`），然后卸载该外部 mod。
下次加载时该 intentId 不再注册，但 `DisabledIntents` 仍保存着它。

**策略**：`[StaticConstructorOnStartup]`（所有 Mod 构造器完成后运行，此时所有动作已注册）中验证并 **warn 但不删除**：

```csharp
[StaticConstructorOnStartup]
internal static class ActionsSettingsValidator
{
    static ActionsSettingsValidator()
    {
        if (MyModMain.Settings == null) return;
        var registered = new HashSet<string>(MyModAPI.GetSupportedIntents());
        foreach (var id in MyModMain.Settings.DisabledIntents)
        {
            if (!registered.Contains(id))
                Log.Warning($"[MyMod] 设置中有未注册的动作 '{id}'（对应 mod 未加载？），已保留以备恢复。");
        }
    }
}
```

**为什么不删除**：若玩家重新启用该外部 mod，孤儿条目会自动重新生效，无需玩家重新配置。
