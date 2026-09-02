# AGENTS.md — RimMind-Core

RimMind 的运行时与公共 API 前置。Core 负责 LLM 请求、上下文、Agent、ToolCall、生命周期、设置和调试基础设施；其他模组只通过公开边界接入。

## Start here

先按变更类型选择入口，不要从全仓搜索开始：

- AI 请求：`Source/Application/Features/Requests/README.md`
- 上下文：`Source/Presentation/Context/ContextOrchestrator.cs`
- Agent：`Source/Presentation/Agent/PawnAgent.cs`
- ToolCall：`Source/Application/Features/Pipeline/Unified/ToolCallDispatchMiddleware.cs`
- 组合与生命周期：`Source/Presentation/Runtime/RimMindCompositionRoot.cs`
- 游戏内调试：`Source/Infrastructure/UI/DebugActions/README.md`
- Agent 流程实验室：`Source/Infrastructure/UI/AgentFlow/README.md`

## Structure

```text
Source/
├── Domain/          纯模型、值对象和领域规则
├── Application/     用例、端口、管线和调度
├── Infrastructure/ 客户端、存储、Verse 与 UI 实现
└── Presentation/   公共 API、组合根、运行时和游戏入口
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

子模组使用 `RimMind.Presentation.Api.RimMindAPI`、Domain 模型和明确公开的 Application 合同。不要访问 `RimMind.Core.Internal`、`RimMindCoreMod.Settings`、组合根或具体队列实现。

跨子模组的同步字符串数据通过 `RimMindAPI.Providers` 注册和读取；Provider 必须声明稳定 owner ID，并保持 Verse 读取在主线程。

## Local invariants

- AI 请求异步执行；Verse/Unity 副作用仅在主线程发生。
- 生命周期代际退役后，旧回调不得继续产生副作用。
- 请求入口只转发；客户端选择、追踪和取消属于 Application。
- 调度、活动请求和断路状态共享转移规则，不任意拆成接口层级。
- API 密钥和玩家数据不得写入日志。
- 每个测试项目最终少于 100 个测试；优先扩展聚合契约。

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
