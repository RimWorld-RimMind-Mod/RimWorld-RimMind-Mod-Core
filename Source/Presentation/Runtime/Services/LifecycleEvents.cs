using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;

namespace RimMind.Presentation.Runtime.Services
{
    public enum LifecycleEventKind
    {
        RuntimeBuildStarted,
        RuntimePublished,
        RuntimeBuildRejected,
        RuntimeRetired,
        GameServicesPublished,
        StaleCompletionDiscarded
    }

    public static class LifecycleEventSources
    {
        public const string Unknown = "unknown";
        public const string ApiRequest = "api_request";
        public const string RemoteSync = "remote_sync";
        public const string TestConnection = "test_connection";
        public const string PawnAgentTab = "pawn_agent_tab";
        public const string ToolCallDebug = "tool_call_debug";
        public const string AgentFlowLab = "agent_flow_lab";
        public const string AgentDialogue = "agent_dialogue";
        public const string NpcSync = "npc_sync";
        public const string RequestLog = "request_log";
        public const string DebugAction = "debug_action";
        public const string AgentFlow = "agent_flow";
        public const string AgentContextPreview = "agent_context_preview";
        public const string RuntimeLifetime = "runtime_lifetime";
        public const string SettingsUi = "settings_ui";
        public const string RequestOverlay = "RequestOverlay";

        private static readonly HashSet<string> Approved = new HashSet<string>(StringComparer.Ordinal)
        {
            ApiRequest,
            RemoteSync,
            TestConnection,
            PawnAgentTab,
            ToolCallDebug,
            AgentFlowLab,
            AgentDialogue,
            NpcSync,
            RequestLog,
            DebugAction,
            AgentFlow,
            AgentContextPreview,
            RuntimeLifetime,
            SettingsUi,
            RequestOverlay
        };

        public static string Normalize(string? source)
        {
            return source != null && Approved.Contains(source) ? source : Unknown;
        }
    }

    public sealed class LifecycleEvent
    {
        public LifecycleEvent(
            LifecycleEventKind kind,
            Guid runtimeId = default,
            long? runtimeGeneration = null,
            long? gameGeneration = null,
            int? serviceCount = null,
            string? lifecycleState = null,
            string? exceptionType = null,
            string? source = null)
        {
            Kind = kind;
            RuntimeId = runtimeId;
            RuntimeGeneration = runtimeGeneration;
            GameGeneration = gameGeneration;
            ServiceCount = serviceCount;
            LifecycleState = lifecycleState;
            ExceptionType = exceptionType;
            Source = source;
        }

        public LifecycleEventKind Kind { get; }
        public Guid RuntimeId { get; }
        public long? RuntimeGeneration { get; }
        public long? GameGeneration { get; }
        public int? ServiceCount { get; }
        public string? LifecycleState { get; }
        public string? ExceptionType { get; }
        public string? Source { get; }
    }

    public interface ILifecycleEventSink
    {
        void Emit(LifecycleEvent lifecycleEvent);
    }

    public static class LifecycleEventFormatter
    {
        public static string Format(LifecycleEvent lifecycleEvent)
        {
            if (lifecycleEvent == null) throw new ArgumentNullException(nameof(lifecycleEvent));
            var text = new StringBuilder("[RimMind-Core] ").Append(lifecycleEvent.Kind);
            if (lifecycleEvent.RuntimeId != Guid.Empty)
                text.Append(" runtimeId=").Append(lifecycleEvent.RuntimeId);
            if (lifecycleEvent.RuntimeGeneration.HasValue)
                text.Append(" runtimeGeneration=").Append(lifecycleEvent.RuntimeGeneration.Value);
            if (lifecycleEvent.GameGeneration.HasValue)
                text.Append(" gameGeneration=").Append(lifecycleEvent.GameGeneration.Value);
            if (lifecycleEvent.ServiceCount.HasValue)
                text.Append(" serviceCount=").Append(lifecycleEvent.ServiceCount.Value);
            if (lifecycleEvent.LifecycleState != null)
                text.Append(" lifecycleState=").Append(lifecycleEvent.LifecycleState);
            if (lifecycleEvent.ExceptionType != null)
                text.Append(" exceptionType=").Append(lifecycleEvent.ExceptionType);
            if (lifecycleEvent.Source != null)
                text.Append(" source=").Append(lifecycleEvent.Source);
            return text.ToString();
        }
    }

    internal sealed class TraceLifecycleEventSink : ILifecycleEventSink
    {
        public static readonly TraceLifecycleEventSink Instance = new TraceLifecycleEventSink();

        private TraceLifecycleEventSink()
        {
        }

        public void Emit(LifecycleEvent lifecycleEvent)
        {
            Trace.WriteLine(LifecycleEventFormatter.Format(lifecycleEvent));
        }
    }

    internal sealed class LifecycleEventPublisher : ILifecycleEventSink
    {
        private ILifecycleEventSink _sink = TraceLifecycleEventSink.Instance;

        public void Configure(ILifecycleEventSink sink)
        {
            if (sink == null) throw new ArgumentNullException(nameof(sink));
            Volatile.Write(ref _sink, sink);
        }

        public void Emit(LifecycleEvent lifecycleEvent)
        {
            Volatile.Read(ref _sink).Emit(lifecycleEvent);
        }
    }

    internal static class ProcessLifecycleEvents
    {
        public static readonly LifecycleEventPublisher Publisher = new LifecycleEventPublisher();
    }
}
