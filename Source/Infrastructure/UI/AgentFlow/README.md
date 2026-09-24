# Agent Flow Lab

`Window_AgentFlowLab.cs` is the composition map for the game-side agent workflow debugger. Read only the partial that owns the behavior you are changing.

## Reading order

1. `../Window_AgentFlowLab.cs` — shared state and draw order.
2. `AgentFlowStepTracker.cs` — step lifecycle state.
3. `Window_AgentFlowLab.Target.cs` — scope, pawn, agent, and context.
4. `Window_AgentFlowLab.Request.cs` — offline/live requests and response parsing.
5. `Window_AgentFlowLab.Mechanism.cs` — dry run and confirmed execution.
6. `AgentFlowAsyncCoordinator.cs` — async completion and generation fences.
7. `Window_AgentFlowLab.Diagnostics.cs` — queue, log links, and errors.
8. `../../../../Tests/Contracts/UiLifecycleContract.cs` — lifecycle regression contract.

`Window_AgentFlowLab.Layout.cs` contains shared measurements and drawing primitives. It does not own workflow decisions.

## Invariants

- Verse and Unity side effects stay on the main thread.
- Live callbacks return through `LongEventHandler.ExecuteWhenFinished`.
- Runtime and target generations must both match before publication.
- Mechanisms require a successful dry run and explicit confirmation before execution.
