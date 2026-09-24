# Request submission slice

## Responsibility

This slice accepts an LLM envelope, selects the configured client, records one
trace, schedules execution, and returns completion on the queue's main-thread
boundary. It does not own middleware behavior or client transport details.

## Entries

- Public entry: `../../../Presentation/Api/RimMindAPI.Request.cs`
- Application entry: `RequestSubmissionService.cs`

## Reading order

1. `IRequestSubmissionService.cs` — the public application boundary.
2. `RequestSubmissionService.cs` — validation, selection, tracing, and submission.
3. `QueuedPipelineRequestExecutor.cs` — pipeline-to-queue adapter.
4. `Queue/IRequestQueue.cs` — scheduling contract.
5. `Queue/RequestQueue.cs` — scheduling and active-request state.
6. `Queue/RequestCompletionInbox.cs` — background-to-main-thread completion fence.

Stable middleware remains in `../Pipeline/Unified`. Client implementations remain
under `../../../Infrastructure/Services/Clients`; follow those links only when
the change concerns prompt processing or transport.

## Invariants

- AI work executes asynchronously; callbacks are consumed by the main-thread tick.
- Runtime and caller cancellation complete a task at most once.
- Retired runtime generations cannot deliver callbacks or mutate request traces.
- Queue scheduling state stays together because its transitions share one lock.

## Focused verification

```powershell
dotnet test RimMind-Core/Tests/RimMindCore.Tests.csproj -c Release --filter "FullyQualifiedName~RequestSubmissionServiceContract|FullyQualifiedName~AgentQueueContextContracts|FullyQualifiedName~RuntimeAsyncFenceContract"
```
