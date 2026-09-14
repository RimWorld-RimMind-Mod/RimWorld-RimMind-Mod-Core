# Context construction

- Entry: `IContextBuilder.BuildSnapshotFromEnvelopeAsync`, used by pipeline middleware and previews.
- Coordinator: `ContextOrchestrator` schedules keys, builds layers, includes scenario history, trims the prompt budget, and stores commit metadata.
- State and policy: `ProviderCache` owns async provider staleness and event invalidation; `HistoryManager` owns conversation turns; `ContextDiffTracker` retains diff and previous-value state used by lifecycle/commit processing.
- Ports: `IContextKeyRegistry`, `IContextLayerBuilder`, `IHistoryManager`, `IContextCacheManager`, `IContextDiffTracker`, and `IBudgetScheduler`.
- Implementation: `AsyncContextLayerBatchBuilder` isolates layer failures while propagating cancellation. `ContextLayerBuilder` awaits registered async providers and supports existing synchronous value providers without blocking on async work. Default wiring is in `Runtime/Composition/ContextComposition.cs`.
- Tests: `Tests/Contracts/AsyncContextBuildContracts.cs` executes the real engine, builders, cache, history, diff state, and budget logic; `ContextRegistryLifecycleContracts.cs` covers registration/invalidation ownership.

Keep Verse reads on their owning thread and async providers free of direct game mutations. Do not add a second synchronous snapshot builder or synchronous waits. Preview callers must deliver game/UI effects on the main thread and fence stale runtime generations.

From the repository root:

```powershell
dotnet test RimMind-Core/Tests/RimMindCore.Tests.csproj -c Release --filter FullyQualifiedName~AsyncContextBuildContracts
dotnet build RimMind-Core/Source/RimMindCore.csproj -c Release
```
