using System;
using System.Threading.Tasks;
using RimMind.Application.Common.Constants;
using RimMind.Application.Common.Interfaces.Abstractions;
using RimMind.Application.Common.Interfaces.Pipeline;
using RimMind.Application.Common.Models;
using RimMind.Application.Common.Models.Pipeline;
using RimMind.Domain.Events;

namespace RimMind.Application.Features.Pipeline.Bus
{
    internal sealed class DispatchMiddleware : IMiddleware<BusPublishContext>
    {
        public string Name => "BusDispatch";
        public int Order => RimMindDefaults.MiddlewareOrder.Dispatch;
        public string Id => "BusDispatch";
        public string OwnerModId => RimMindOwnerConsts.CoreModId;

        private readonly Action<AgentBusEvent> _dispatch;
        private readonly ILogSink? _log;

        public DispatchMiddleware(Action<AgentBusEvent> dispatch, ILogSink? log = null)
        {
            _dispatch = dispatch;
            _log = log;
        }

        public Task InvokeAsync(BusPublishContext context, MiddlewareDelegate<BusPublishContext> next)
        {
            _dispatch(context.Event);
            return next(context);
        }
    }
}
