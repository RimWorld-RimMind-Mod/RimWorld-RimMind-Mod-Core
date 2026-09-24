using System.Threading.Tasks;
using RimMind.Application.Common.Constants;
using RimMind.Application.Common.Interfaces.Abstractions;
using RimMind.Application.Common.Interfaces.Pipeline;
using RimMind.Application.Common.Interfaces.Runtime;
using RimMind.Application.Common.Models;
using RimMind.Application.Common.Models.Pipeline;
using RimMind.Domain.Llm;
using RimMind.Domain.ValueObjects;

namespace RimMind.Application.Features.Pipeline.Unified
{
    internal sealed class ShortCircuitMiddleware : IMiddleware<LlmRequestContext>
    {
        public string Name => "UnifiedShortCircuit";
        public int Order => RimMindDefaults.MiddlewareOrder.ShortCircuit;
        public string Id => "UnifiedShortCircuit";
        public string OwnerModId => RimMindOwnerConsts.CoreModId;

        private readonly ILogSink? _log;
        private readonly IRimMindRuntime? _runtime;

        public ShortCircuitMiddleware(ILogSink? log = null, IRimMindRuntime? runtime = null)
        {
            _log = log;
            _runtime = runtime;
        }

        public Task InvokeAsync(LlmRequestContext context, MiddlewareDelegate<LlmRequestContext> next)
        {
            if (_runtime != null && _runtime.IsShutdown)
            {
                _log?.Warning("[UnifiedShortCircuit] Runtime is shutdown, short-circuiting request");
                context.Result = Result<LlmResponse, RimMindError>.Err(
                    RimMindErrors.Internal("Runtime is shutdown"));
                context.ShortCircuit("runtime_shutdown");
                return Task.CompletedTask;
            }

            if (context.Envelope == null)
            {
                _log?.Warning("[UnifiedShortCircuit] Null envelope, short-circuiting request");
                context.Result = Result<LlmResponse, RimMindError>.Err(
                    RimMindErrors.Internal("Null envelope"));
                context.ShortCircuit("null_envelope");
                return Task.CompletedTask;
            }

            if (string.IsNullOrWhiteSpace(context.Envelope.RequestId))
            {
                _log?.Warning("[UnifiedShortCircuit] Empty RequestId, short-circuiting request");
                context.Result = Result<LlmResponse, RimMindError>.Err(
                    RimMindErrors.Internal("Empty RequestId"));
                context.ShortCircuit("empty_request_id");
                return Task.CompletedTask;
            }

            return next(context);
        }
    }
}
