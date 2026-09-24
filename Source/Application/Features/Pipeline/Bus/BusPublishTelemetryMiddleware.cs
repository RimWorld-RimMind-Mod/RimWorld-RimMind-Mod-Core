using System;
using System.Threading.Tasks;
using RimMind.Application.Common.Constants;
using RimMind.Application.Common.Interfaces.Abstractions;
using RimMind.Application.Common.Interfaces.Pipeline;
using RimMind.Application.Common.Models.Pipeline;

namespace RimMind.Application.Features.Pipeline.Bus
{
    internal sealed class BusPublishTelemetryMiddleware : IMiddleware<BusPublishContext>
    {
        public string Name => "BusPublishTelemetry";
        public int Order => int.MinValue;
        public string Id => "BusPublishTelemetry";
        public string OwnerModId => RimMindOwnerConsts.CoreModId;

        private readonly ILogSink? _log;

        public BusPublishTelemetryMiddleware(ILogSink? log = null) { _log = log; }

        public async Task InvokeAsync(BusPublishContext context, MiddlewareDelegate<BusPublishContext> next)
        {
            var start = DateTime.UtcNow;
            try
            {
                await next(context);
            }
            finally
            {
                var elapsed = (DateTime.UtcNow - start).TotalMilliseconds;
                _log?.Message($"[BusTelemetry] Event {context.Event?.GetType().Name} published in {elapsed:F0}ms");
            }
        }
    }
}
