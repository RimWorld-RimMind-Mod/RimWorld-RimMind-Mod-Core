using System;
using System.Threading.Tasks;
using RimMind.Application.Common.Constants;
using RimMind.Application.Common.Interfaces.Abstractions;
using RimMind.Application.Common.Interfaces.Pipeline;
using RimMind.Application.Common.Models.Pipeline;

namespace RimMind.Application.Features.Pipeline.Bus
{
    internal sealed class ErrorIsolationMiddleware : IMiddleware<BusPublishContext>
    {
        public string Name => "BusErrorIsolation";
        public int Order => 90;
        public string Id => "BusErrorIsolation";
        public string OwnerModId => RimMindOwnerConsts.CoreModId;

        private readonly ILogSink? _log;

        public ErrorIsolationMiddleware(ILogSink? log = null) { _log = log; }

        public async Task InvokeAsync(BusPublishContext context, MiddlewareDelegate<BusPublishContext> next)
        {
            try
            {
                await next(context);
            }
            catch (Exception ex)
            {
                _log?.Error($"[BusErrorIsolation] Swallowed error in bus publish: {ex.Message}");
            }
        }
    }
}
