# RimMind - Core

RimMind 套件的核心基础设施，提供 LLM 客户端、异步请求队列和游戏上下文构建器，是所有 RimMind 子模组的前置依赖。

## RimMind 是什么

RimMind 是一套 AI 驱动的 RimWorld 模组套件，通过接入大语言模型（LLM），让殖民者拥有人格、记忆、对话和自主决策能力。

## 子模组列表与依赖关系

| 模组 | 职责 | 依赖 | GitHub |
|------|------|------|--------|
| **RimMind-Core** | API 客户端、请求调度、上下文打包 | Harmony | [链接](https://github.com/RimWorld-RimMind-Mod/RimWorld-RimMind-Mod-Core) |
| RimMind-Actions | AI 控制小人的动作执行库 | Core | [链接](https://github.com/RimWorld-RimMind-Mod/RimWorld-RimMind-Mod-Actions) |
| RimMind-Advisor | AI 扮演小人做出工作决策 | Core, Actions | [链接](https://github.com/RimWorld-RimMind-Mod/RimWorld-RimMind-Mod-Advisor) |
| RimMind-Dialogue | AI 驱动的对话系统 | Core | [链接](https://github.com/RimWorld-RimMind-Mod/RimWorld-RimMind-Mod-Dialogue) |
| RimMind-Memory | 记忆采集与上下文注入 | Core | [链接](https://github.com/RimWorld-RimMind-Mod/RimWorld-RimMind-Mod-Memory) |
| RimMind-Personality | AI 生成人格与想法 | Core | [链接](https://github.com/RimWorld-RimMind-Mod/RimWorld-RimMind-Mod-Personality) |
| RimMind-Storyteller | AI 叙事者，智能选择事件 | Core | [链接](https://github.com/RimWorld-RimMind-Mod/RimWorld-RimMind-Mod-Storyteller) |

```
Core ── Actions ── Advisor
  ├── Dialogue
  ├── Memory
  ├── Personality
  └── Storyteller
```

## 安装步骤

### 从源码安装

**Linux/macOS:**
```bash
git clone git@github.com:RimWorld-RimMind-Mod/RimWorld-RimMind-Mod-Core.git
cd RimWorld-RimMind-Mod-Core
./script/deploy-single.sh <your RimWorld path>
```

**Windows:**
```powershell
git clone git@github.com:RimWorld-RimMind-Mod/RimWorld-RimMind-Mod-Core.git
cd RimWorld-RimMind-Mod-Core
./script/deploy-single.ps1 <your RimWorld path>
```

### 从 Steam 安装

1. 安装 [Harmony](https://steamcommunity.com/sharedfiles/filedetails/?id=2009463077) 前置模组
2. 安装 RimMind-Core
3. 按需安装其他 RimMind 子模组
4. 在模组管理器中确保加载顺序：Harmony → Core → 其他子模组

<!-- ![安装步骤](images/install-steps.png) -->

## 快速开始

### 填写 API Key

1. 启动游戏，进入主菜单
2. 点击 **选项 → 模组设置 → RimMind-Core**
3. 选择 **AI Provider**（OpenAI 兼容 API 或 Player2）
4. 如果选择 OpenAI 兼容模式：
   - 填写你的 **API Key**
   - 填写 **API 端点**（见下方支持的端点列表）
   - 填写 **模型名称**（如 `gpt-4o-mini`、`deepseek-chat`）
5. 如果选择 Player2 模式：
   - 安装 Player2 本地应用后可自动检测，无需手动配置
   - 也可手动填写 Player2 API Key 使用远程服务
6. 点击 **测试连接**，确认显示"连接成功"

<!-- ![API 设置](images/api-settings.png) -->

### 支持的 API 端点

| 服务 | 端点 | 说明 |
|------|------|------|
| DeepSeek | `https://api.deepseek.com/v1` | deepseek-chat 等模型（默认） |
| OpenAI | `https://api.openai.com/v1` | GPT-4o-mini 等模型 |
| Ollama (本地) | `http://localhost:11434/v1` | 本地部署的模型 |
| Player2 | 自动检测 / 手动配置 | Player2 本地应用或远程 API |
| 其他 | 填入 Base URL | 任何 OpenAI 兼容接口 |

## 截图展示

<!-- ![设置界面](images/screenshot-settings.png) -->
<!-- ![调试日志](images/screenshot-debug.png) -->
<!-- ![请求悬浮窗](images/screenshot-overlay.png) -->

## 核心功能

### LLM 客户端

兼容 OpenAI / DeepSeek / 本地 Ollama 等所有 OpenAI Chat Completions 格式的 API，同时支持 Player2 服务（本地应用自动检测 + 远程 API）。支持 JSON 强制模式（`response_format: json_object`），本地模型可关闭。

### 异步请求队列

所有 AI 请求在后台线程执行，不阻塞游戏主线程。每个请求独立冷却，过期请求自动丢弃。支持瞬态错误自动重试（timeout / 429 / 502 / 503 等），本地模型串行处理避免资源竞争。

### 上下文构建

自动采集游戏状态并打包为文本供各模块使用：

- **地图上下文**：时间、殖民者、食物、威胁、季节、天气
- **小人上下文**：年龄、背景、心情、健康、技能、装备、工作分配、关系

### 上下文过滤器

通过"上下文过滤"设置页精确控制哪些游戏信息注入 Prompt，节省 Token。提供最小/标准/完整三种预设，也可自定义勾选 28+ 个选项。

### 调试工具

- **AI Debug Log**：浮动窗口，查看每次 AI 调用的完整 Prompt + Response
- **请求悬浮窗**：右下角实时显示 AI 请求状态
- **Dev 菜单**：测试连接、查看上下文、清除冷却、暂停/恢复队列等

## 设置项

| 设置 | 默认值 | 说明 |
|------|--------|------|
| AI Provider | OpenAI | 选择 OpenAI 兼容 API 或 Player2 |
| API Key | - | 你的 API 密钥（Player2 模式可选） |
| API 端点 | `https://api.deepseek.com/v1` | OpenAI 兼容端点 |
| 模型名称 | `deepseek-chat` | 任意模型 ID |
| 强制 JSON 模式 | 开启 | 不支持的本地模型请关闭 |
| 最大 Token | 800 | 响应长度上限（200-2000） |
| 最大并发请求数 | 3 | 同时发送请求的上限（1-10） |
| 最大重试次数 | 2 | 请求失败后重试次数（0-5） |
| 请求超时 | 120秒 | 等待 AI 响应的最大时间（10-300秒） |
| 详细日志 | 关闭 | 输出到 Player.log |
| 请求悬浮窗 | 开启 | 右下角显示请求状态 |
| 自定义人物提示词 | 空 | 追加在人物上下文末尾 |
| 自定义地图提示词 | 空 | 追加在地图上下文末尾 |
| 上下文过滤器 | 标准 | 28+ 个可选项，三种预设 |

## 常见问题

**Q: 支持哪些大模型？**
A: 任何兼容 OpenAI Chat Completions API 的模型均可使用，包括 OpenAI GPT 系列、DeepSeek、本地 Ollama 等。同时支持 Player2 服务（本地应用自动检测 + 远程 API）。

**Q: 会不会影响游戏帧率？**
A: 不会。所有 AI 请求在后台线程执行，主线程只处理回调结果。

**Q: API Key 安全吗？**
A: API Key 仅存储在本地 RimWorld 设置文件中，不会上传到任何服务器。

**Q: 不填 API Key 会怎样？**
A: Core 本身不会报错，但所有依赖 AI 的子模组功能将无法工作。

**Q: 推荐用什么模型？**
A: 推荐使用 `gpt-4o-mini` 或 `deepseek-chat`，性价比高且响应速度快。本地 Ollama 用户可使用 `qwen2.5:7b` 等模型。

## 致谢

本项目开发过程中参考了以下优秀的 RimWorld 模组：

- [RimTalk](https://github.com/jlibrary/RimTalk.git) - 对话系统参考
- [RimTalk-ExpandActions](https://github.com/sanguodxj-byte/RimTalk-ExpandActions.git) - 动作扩展参考
- [NewRatkin](https://github.com/solaris0115/NewRatkin.git) - 种族模组架构参考
- [VanillaExpandedFramework](https://github.com/Vanilla-Expanded/VanillaExpandedFramework.git) - 框架设计参考

## 贡献

欢迎提交 Issue 和 Pull Request！如果你有任何建议或发现 Bug，请通过 GitHub Issues 反馈。


---

# RimMind - Core (English)

The core infrastructure of the RimMind suite, providing LLM client, async request queue, and game context builder. Required by all other RimMind modules.

## What is RimMind

RimMind is an AI-driven RimWorld mod suite that connects to Large Language Models (LLMs), giving colonists personality, memory, dialogue, and autonomous decision-making.

## Sub-Modules & Dependencies

| Module | Role | Depends On | GitHub |
|--------|------|------------|--------|
| **RimMind-Core** | API client, request dispatch, context packaging | Harmony | [Link](https://github.com/RimWorld-RimMind-Mod/RimWorld-RimMind-Mod-Core) |
| RimMind-Actions | AI-controlled pawn action execution | Core | [Link](https://github.com/RimWorld-RimMind-Mod/RimWorld-RimMind-Mod-Actions) |
| RimMind-Advisor | AI role-plays colonists for work decisions | Core, Actions | [Link](https://github.com/RimWorld-RimMind-Mod/RimWorld-RimMind-Mod-Advisor) |
| RimMind-Dialogue | AI-driven dialogue system | Core | [Link](https://github.com/RimWorld-RimMind-Mod/RimWorld-RimMind-Mod-Dialogue) |
| RimMind-Memory | Memory collection & context injection | Core | [Link](https://github.com/RimWorld-RimMind-Mod/RimWorld-RimMind-Mod-Memory) |
| RimMind-Personality | AI-generated personality & thoughts | Core | [Link](https://github.com/RimWorld-RimMind-Mod/RimWorld-RimMind-Mod-Personality) |
| RimMind-Storyteller | AI storyteller, smart event selection | Core | [Link](https://github.com/RimWorld-RimMind-Mod/RimWorld-RimMind-Mod-Storyteller) |

## Installation

### Install from Source

**Linux/macOS:**
```bash
git clone git@github.com:RimWorld-RimMind-Mod/RimWorld-RimMind-Mod-Core.git
cd RimWorld-RimMind-Mod-Core
./script/deploy-single.sh <your RimWorld path>
```

**Windows:**
```powershell
git clone git@github.com:RimWorld-RimMind-Mod/RimWorld-RimMind-Mod-Core.git
cd RimWorld-RimMind-Mod-Core
./script/deploy-single.ps1 <your RimWorld path>
```

### Install from Steam

1. Install [Harmony](https://steamcommunity.com/sharedfiles/filedetails/?id=2009463077)
2. Install RimMind-Core
3. Install other RimMind sub-modules as needed
4. Ensure load order: Harmony → Core → other sub-modules

## Quick Start

### API Key Setup

1. Launch the game, go to main menu
2. Click **Options → Mod Settings → RimMind-Core**
3. Select **AI Provider** (OpenAI-compatible API or Player2)
4. If using OpenAI-compatible mode:
   - Enter your **API Key**
   - Enter your **API Endpoint** (see supported endpoints below)
   - Enter your **Model Name** (e.g., `gpt-4o-mini`, `deepseek-chat`)
5. If using Player2 mode:
   - Install Player2 local app for automatic detection, no manual configuration needed
   - Or manually enter a Player2 API Key for remote service
6. Click **Test Connection** and confirm "Connection Successful"

### Supported API Endpoints

| Service | Endpoint | Notes |
|---------|----------|-------|
| DeepSeek | `https://api.deepseek.com/v1` | deepseek-chat etc. (default) |
| OpenAI | `https://api.openai.com/v1` | GPT-4o-mini etc. |
| Ollama (local) | `http://localhost:11434/v1` | Locally deployed models |
| Player2 | Auto-detect / Manual | Player2 local app or remote API |
| Others | Enter Base URL | Any OpenAI-compatible API |

## Key Features

- **LLM Client**: Compatible with OpenAI / DeepSeek / local Ollama and any OpenAI Chat Completions API, plus Player2 service (local app auto-detect + remote API)
- **Async Request Queue**: All AI requests run on background threads, never blocking the game. Supports automatic retry for transient errors (timeout / 429 / 502 / 503 etc.), serial processing for local models
- **Context Builder**: Automatically collects game state (colonist stats, map info, etc.) for AI prompts
- **Context Filter**: Fine-grained control over what game info gets sent to AI, with Minimal/Standard/Full presets and 28+ configurable options
- **Debug Tools**: AI Debug Log window, request overlay, Dev menu actions (test connection, view context, clear cooldowns, pause/resume queue)

## FAQ

**Q: Which models are supported?**
A: Any model compatible with the OpenAI Chat Completions API, including OpenAI GPT, DeepSeek, and local Ollama. Also supports Player2 service (local app auto-detect + remote API).

**Q: Will it affect game FPS?**
A: No. All AI requests run on background threads; the main thread only processes callbacks.

**Q: Is my API Key safe?**
A: The API Key is stored locally in RimWorld settings files and never uploaded to any server.

**Q: What if I don't fill in an API Key?**
A: Core itself won't error, but all AI-dependent sub-module features will be unavailable.

**Q: What model do you recommend?**
A: `gpt-4o-mini` or `deepseek-chat` for good balance of cost and speed. Ollama users can try `qwen2.5:7b`.

## Acknowledgments

This project references the following excellent RimWorld mods:

- [RimTalk](https://github.com/jlibrary/RimTalk.git) - Dialogue system reference
- [RimTalk-ExpandActions](https://github.com/sanguodxj-byte/RimTalk-ExpandActions.git) - Action expansion reference
- [NewRatkin](https://github.com/solaris0115/NewRatkin.git) - Race mod architecture reference
- [VanillaExpandedFramework](https://github.com/Vanilla-Expanded/VanillaExpandedFramework.git) - Framework design reference

## Contributing

Issues and Pull Requests are welcome! If you have any suggestions or find bugs, please feedback via GitHub Issues.
