using System.Threading.Tasks;
using RimMind.Application.Common.Constants;
using RimMind.Application.Common.Interfaces.Abstractions;
using RimMind.Application.Common.Interfaces.Npc;
using RimMind.Application.Common.Interfaces.Pipeline;
using RimMind.Application.Common.Models;
using RimMind.Application.Common.Models.Pipeline;

namespace RimMind.Application.Features.Pipeline.Unified
{
    internal sealed class NpcEnrichMiddleware : IMiddleware<LlmRequestContext>
    {
        public string Name => "UnifiedNpcEnrich";
        public int Order => RimMindDefaults.MiddlewareOrder.NpcEnrich;
        public string Id => "UnifiedNpcEnrich";
        public string OwnerModId => RimMindOwnerConsts.CoreModId;

        private readonly INpcManagerAccessor? _npcManagers;
        private readonly ILogSink? _log;

        public NpcEnrichMiddleware(INpcManagerAccessor? npcManagers = null, ILogSink? log = null)
        {
            _npcManagers = npcManagers;
            _log = log;
        }

        public NpcEnrichMiddleware(INpcManager? npcManager, ILogSink? log = null)
            : this(
                npcManager == null ? null : new FixedNpcManagerAccessor(npcManager),
                log)
        {
        }

        public async Task InvokeAsync(LlmRequestContext context, MiddlewareDelegate<LlmRequestContext> next)
        {
            var envelope = context.Envelope;
            var npcId = envelope?.NpcId;
            var npcManager = _npcManagers?.Current;
            if (npcId is { Length: > 0 } npcIdText)
            {
                // NPC profiles are optional context. Provider-specific lifecycle management
                // must not block the shared request pipeline when no profile is registered.
                if (envelope!.GameStateInfo == null)
                {
                    var npcProfile = npcManager?.GetNpc(npcIdText);
                    if (npcProfile != null)
                    {
                        context.Items["NpcProfile"] = npcProfile;
                        _log?.Message($"[UnifiedNpcEnrich] NPC {npcIdText} enriched");
                    }
                }
            }

            await next(context);
        }

        private sealed class FixedNpcManagerAccessor : INpcManagerAccessor
        {
            public FixedNpcManagerAccessor(INpcManager current)
            {
                Current = current;
            }

            public INpcManager? Current { get; }
        }
    }
}
