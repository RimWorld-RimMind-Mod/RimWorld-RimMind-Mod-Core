# RimMind - Core

RimMind 套件的核心基础设施，为 AI 驱动的 RimWorld 体验提供底层支撑。

## 核心能力

**智能上下文构建** - 自动采集游戏状态：殖民者心情、健康、技能、装备、地图财富、威胁等级、季节天气等，为 AI 决策提供完整情报。

**异步请求队列** - 智能管理 LLM 请求，避免阻塞游戏主线程，支持请求优先级和冷却控制。

**模块化 API 设计** - 其他 RimMind 组件通过统一接口调用，无需关心底层实现细节。

**上下文过滤器** - 28+ 个可配置选项，精确控制注入 Prompt 的信息量，节省 Token 开销。

**调试工具集** - 内置 AI 请求日志窗口，实时查看每次 AI 调用的输入输出，方便排查问题。

## 技术亮点

- 兼容 OpenAI / DeepSeek / Ollama 等所有 OpenAI Chat Completions 格式的 API
- 可配置的上下文过滤器，提供最小/标准/完整三种预设
- JSON 模式支持，确保 AI 返回结构化数据
- 请求悬浮窗，实时显示 AI 请求状态

## 建议配图

1. 设置界面截图（展示 API 配置和上下文选项）
2. 调试日志窗口截图（展示 AI 请求/响应详情）
3. 请求悬浮窗截图

---

# RimMind - Core (English)

The core infrastructure of the RimMind suite, providing foundational support for AI-driven RimWorld experiences.

## Key Features

**Smart Context Builder** - Automatically captures game state: colonist mood, health, skills, equipment, map wealth, threat level, season, weather, and more, providing complete intelligence for AI decision-making.

**Async Request Queue** - Intelligently manages LLM requests without blocking the game main thread, supporting request prioritization and cooldown control.

**Modular API Design** - Other RimMind components call through a unified interface without worrying about underlying implementation details.

**Context Filter** - 28+ configurable options for fine-grained control over what information gets injected into AI prompts, saving token costs.

**Debug Toolkit** - Built-in AI request log window for real-time viewing of inputs and outputs for each AI call, facilitating troubleshooting.

## Technical Highlights

- Compatible with OpenAI / DeepSeek / Ollama and any OpenAI Chat Completions API
- Configurable context filters with Minimal / Standard / Full presets
- JSON mode support for structured AI responses
- Request overlay showing real-time AI request status

## Suggested Screenshots

1. Settings interface (showing API configuration and context options)
2. Debug log window (showing AI request/response details)
3. Request overlay screenshot
