# AGENTS.md — RimMind-Core

RimMind 的运行时与公共 API 前置。Core 负责 LLM 请求、上下文、Agent、ToolCall、生命周期、设置和调试基础设施；其他模组只通过公开边界接入。

## Start here

先按变更类型选择入口，不要从全仓搜索开始：

- AI 请求：`Source/Application/Features/Requests/README.md`
- 上下文：`Source/Presentation/Context/README.md`
- Agent：`Source/Presentation/Agent/PawnAgent.cs`
- 自治分级治理：`Source/Application/Features/Agent/AgentAutonomyPolicyService.cs`（`AutonomyDomain` / `AutonomyLevel`）
- ToolCall 与渐进式帮助：`Source/Application/Features/Pipeline/Unified/ToolCallDispatchMiddleware.cs`、`Source/Application/Features/Tools/QueryToolHelpTool.cs`
- 主备端点容灾：`Source/Domain/Settings/BackupEndpointNode.cs`、`Source/Infrastructure/Services/Clients/OpenAI/OpenAIClient.cs`
- 组合与生命周期：`Source/Presentation/Runtime/RimMindCompositionRoot.cs`
- 游戏内调试：`Source/Infrastructure/UI/DebugActions/README.md`
- Agent 流程实验室：`Source/Infrastructure/UI/AgentFlow/README.md`

## Structure

```text
Source/
├── Domain/          纯模型、值对象和领域规则（0_RimMindDomain.dll）
├── Application/     用例、端口、管线和调度（1_RimMindApplication.dll）
├── Infrastructure/ 客户端、Mechanism、存储、Verse 与 UI 实现（2_RimMindCore.dll）
└── Presentation/   公共 API、组合根、运行时和游戏入口（2_RimMindCore.dll）
```

依赖方向是 Presentation/Infrastructure → Application → Domain。Domain 和 Application 不得引用 Verse、Unity 或 Harmony。

## Main request flow

```text
RimMindAPI.Request
  → IRequestSubmissionService
  → RequestSubmissionService
  → IRequestQueue / RequestQueue
  → IPipeline<LlmRequestContext>
  → IAIClient
```

请求队列的后台结果只能通过 `RequestCompletionInbox` 回到主线程。稳定中间件仍位于 `Application/Features/Pipeline/Unified`，客户端位于 `Infrastructure/Services/Clients`。

## Public boundary

子模组使用 `RimMind.Presentation.Api.RimMindAPI`（含 `Request`、`Context`、`Tools`、`Autonomy`、`Mechanisms`、`Npc`、`Memory`）、Domain 模型和明确公开的 Application 合同。不要访问 `RimMind.Core.Internal`、`RimMindCoreMod.Settings`、组合根或具体队列实现。

- **三层自治治理（`RimMindAPI.Autonomy`）**：按五大自治领域（`Survival`、`Work`、`Social`、`Colony`、`Governance`）与三档自治级别（`Auto`、`AutoWithNotify`、`RecommendOnly`）结合 `ToolManifest.RiskLevel` 进行动作门控。
- **Pawn 存活与生命周期（`RimMindPawnLookup`）**：所有 Pawn 查询与交互必须通过 `RimMindPawnLookup.IsActiveSpawnedColonist`、`IsEligibleColonist` 或 `IsActiveColonyAnimal` 过滤；Pawn 销毁或解绑时必须调用 `RimMindPawnLookup.UncachePawn` 清理缓存。
- **跨模组数据与渐进式工具加载**：跨子模组的同步字符串数据通过 `RimMindAPI.Providers` 注册和读取；工具注册支持通过 `QueryToolHelpTool`（`query_tool_help`）按需获取完整参数 Schema，降低常驻 Prompt Token 消耗。

上下文快照统一使用 `IContextBuilder.BuildSnapshotFromEnvelopeAsync`；不要另建同步快照管线或阻塞等待异步 Provider。默认 Agent 保留 Reactive / Proactive 模式、周期与感知触发；反思、日规划、梦境、社交组织、性格演化不装配占位实现，仅通过明确可选的策略扩展接入。

## Local invariants

- AI 请求异步执行；后台网络探针与请求统一使用 `HttpClient`（禁止在后台线程创建 `UnityWebRequest`）；Verse/Unity 副作用仅在主线程发生。
- 生命周期代际退役后，旧回调不得继续产生副作用；`StopGameServices()` 必须停止 `Player2Client` 后台健康检查与异步栅栏。
- 请求入口只转发；客户端选择、追踪和取消属于 Application；断路器（`CircuitBreakerMiddleware`）忽略取消与本地配置/短接错误，仅针对真实传输故障计数熔断。
- 调度、活动请求和断路状态共享转移规则，不任意拆成接口层级。
- 伪本地化防御：所有翻译 Key 调用前须经 `Translator.CanTranslate` 守卫并提供英文退化默认值。
- API 密钥和玩家数据不得写入日志。
- Core 全部测试项目累计少于 1000 个发现用例；参数化测试每个数据行计数。
- 测试覆盖真实行为、失败边界和模块协作；替身仅隔离外部依赖，不锁定私有实现形状，不为压低数量合并无关场景。

## Smallest useful verification

```powershell
dotnet test Tests/RimMindCore.Tests.csproj -c Release
dotnet test ArchTests/RimMindCore.ArchTests.csproj -c Release
dotnet test IntegrationTests/RimMindCore.Integration.Tests.csproj -c Release
dotnet build Source/RimMindCore.csproj -c Release
```

仅修改请求切片时，先运行 `Source/Application/Features/Requests/README.md` 中的聚焦命令。

## Do not

- 不在 Tick 中遍历全地图小人或直接发起网络请求。
- 不用 `Task.Run` 包装 Verse/Unity 调用。
- 不新增服务定位器、DI 框架、镜像队列或仅有一个实现的装饰性接口。
- 不硬编码 UI 中文；使用 Keyed XML。
- 不运行或宣称已通过当前缺失资源所阻塞的游戏内 E2E。
