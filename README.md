<div align="center">

# RimMind-Core 🧠
### Foundational LLM Runtime, Context Engine & ToolCall Framework for RimWorld 1.6

**English** | [简体中文](README_zh.md)

<p>
  <a href="https://rimworldgame.com/"><img src="https://img.shields.io/badge/RimWorld-1.6-brightgreen.svg" alt="RimWorld 1.6"></a>
  <a href="https://github.com/pardeike/Harmony"><img src="https://img.shields.io/badge/Harmony-2.x-blue.svg" alt="Harmony 2.x"></a>
  <a href="https://dotnet.microsoft.com/"><img src="https://img.shields.io/badge/.NET%20Framework-4.8%20%7C%20C%23%2010-purple.svg" alt="C# 10 / .NET Framework 4.8"></a>
  <a href="#"><img src="https://img.shields.io/badge/Architecture%20Tests-100%25%20Passing-success.svg" alt="Architecture Tests"></a>
  <a href="#"><img src="https://img.shields.io/badge/Unit%20Tests-110%2B%20Passing-success.svg" alt="Unit Tests"></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/License-MIT-yellow.svg" alt="License: MIT"></a>
</p>

<p><em>The foundational bedrock for the entire RimMind AI ecosystem.</em></p>

</div>

---

## 📖 Overview

**RimMind-Core** provides the foundational AI infrastructure, Clean Architecture runtime (`Domain` / `Application` / `Infrastructure` / `Presentation`), and public API boundary (`RimMindAPI`) for the RimMind mod suite. Every gameplay submodule (`Actions`, `Advisor`, `Dialogue`, `Memory`, `Personality`, `Storyteller`, `Bridges`, `Extension-ModelService`) depends solely on the public contracts exposed by `RimMind-Core`.

### Core Capabilities
- **4-Zone KV-Cache Orchestrator**: Separates invariant world rules (Zone 1) and permanent pawn profiles (Zone 2) from dynamic history (Zone 3) and volatile environment states (Zone 4), delivering an 80%+ prefix cache hit rate.
- **Three-Tier Agent Autonomy Governance (`RimMindAPI.Autonomy`)**: Fine-grained action gating across 5 domains (`Survival`, `Work`, `Social`, `Colony`, `Governance`) and 3 autonomy levels (`Auto`, `AutoWithNotify`, `RecommendOnly`) coupled with `ToolManifest.RiskLevel` verification.
- **Native ToolCall Registry & Progressive Schema Discovery (`query_tool_help`)**: Type-safe tool contracts with on-demand `QueryToolHelpTool` schema inspection, minimizing baseline prompt token footprint while eliminating markdown formatting errors.
- **Primary + Backup Endpoint Failover & Priority Request Queue**: Thread-safe background HTTP/JSON dispatching with `BackupEndpointNode` automatic failover on HTTP 429/5xx, transport-aware `CircuitBreakerMiddleware`, and main-thread `RequestCompletionInbox` callback marshaling.
- **Pawn Liveness & Lifecycle Guard (`RimMindPawnLookup`)**: Thread-safe pawn lookup and cache eviction (`IsActiveSpawnedColonist`, `IsEligibleColonist`, `IsActiveColonyAnimal`, `UncachePawn`) preventing ghost requests on dead, off-map, or imprisoned pawns.
- **F8 Central Debug & Inspection Hub**: In-game control station for live latency ping tests, queue flush/pause controls, autonomy policy tuning, and raw colonist context inspections.

---

## 🎮 In-Game Showcase

![RimMind-Core Showcase](docs/images/showcase.jpg)
*The in-game F8 RimMind Debug Center. Developers and players can test real-time LLM API connectivity, monitor token cache efficiency, and tune concurrency parameters live.*

---

## 🏛️ Architecture & Clean Dependencies

```mermaid
flowchart TD
    subgraph Submodules ["Dependent Submodules"]
        Actions["RimMind-Actions"]
        Advisor["RimMind-Advisor"]
        Dialogue["RimMind-Dialogue"]
        Memory["RimMind-Memory"]
        Personality["RimMind-Personality"]
        Storyteller["RimMind-Storyteller"]
        ModelService["RimMind-Extension-ModelService"]
    end

    subgraph Core ["RimMind-Core (Clean Architecture)"]
        API["Presentation: Public API (RimMindAPI & RimMindPawnLookup)"]
        Autonomy["Application: 3-Tier Autonomy Governance"]
        Context["Application: 4-Zone Context Orchestrator"]
        Queue["Application: Priority Queue & CircuitBreaker"]
        ToolRegistry["Application: ToolCall Registry & query_tool_help"]
        F8Hub["Infrastructure: F8 Debug Center & Mechanisms"]
    end

    subgraph Network ["Network Layer (Background Thread)"]
        ClientFactory["IAIClientFactory / Providers"]
        OpenAI["OpenAI / DeepSeek + BackupEndpointNode Failover"]
        Player2["Player2 Local/Cloud Client"]
    end

    Submodules -->|Depend solely on Public API| API
    API --> Autonomy
    API --> Context
    API --> Queue
    API --> ToolRegistry
    F8Hub --> Queue
    Queue --> ClientFactory
    ClientFactory --> OpenAI
    ClientFactory --> Player2
```

---

## 🛠️ Installation & Mod Load Order

In RimWorld's mod manager, ensure `RimMind-Core` is loaded right after Harmony and the vanilla Core game:

```text
1. Harmony
2. Core (Vanilla RimWorld)
3. RimMind-Core
4. [Other RimMind submodules, loaded in any order]
```

---

## ⚙️ Settings Reference

Configurable via **Options -> Mod Settings -> RimMind-Core**:

| Setting | Default | Description |
|---|---|---|
| **AI Provider** | `openai` | Supports OpenAI-compatible endpoints, DeepSeek, Player2, and extended gateways |
| **API Endpoint & Backup Nodes** | `https://api.deepseek.com/v1` | Primary OpenAI Chat Completions URL plus optional `BackupEndpointNode` failover list |
| **Model Name** | `deepseek-chat` | Target LLM model identifier |
| **Agent Autonomy Policies** | Domain-specific | Per-domain (`Survival`, `Work`, `Social`, `Colony`, `Governance`) level (`Auto`, `AutoWithNotify`, `RecommendOnly`) |
| **Max Concurrent Requests** | `3` | Maximum simultaneous requests sent to the LLM (1~10) |
| **Request Timeout** | `60s` | Maximum wait time for LLM response before timeout (10s~180s) |
| **Request Overlay** | `Enabled` | Floating status widget in the lower right showing active requests |
| **Verbose Logging** | `Disabled` | Outputs detailed payload traces to `Player.log` |

---

## 🧪 Developer Guide & Testing

Run unit tests and architectural invariant tests from the repository root:

```powershell
# Run all unit tests
dotnet test RimMind-Core/Tests/RimMindCore.Tests.csproj -c Release

# Run architecture contract guard tests
dotnet test RimMind-Core/ArchTests/RimMindCore.ArchTests.csproj -c Release
```

---

## 📜 License

Licensed under the [MIT License](LICENSE).
