using System;
using RimMind.Application.Common.Behaviours;
using RimMind.Application.Common.Interfaces;
using RimMind.Application.Common.Interfaces.Abstractions;
using RimMind.Application.Common.Interfaces.Extension;
using RimMind.Application.Common.Interfaces.Pipeline;
using RimMind.Application.Common.Models.Pipeline;
using RimMind.Domain.Events;

namespace RimMind.Application.Features.Pipeline.Bus
{
    public static class BusPublishPipelineFactory
    {
        public static IPipeline<BusPublishContext> Build(
            Action<AgentBusEvent> dispatch,
            ILogSink? logSink = null,
            IThreadChecker? threadChecker = null,
            IExtensionRegistry<IMiddleware<BusPublishContext>>? extensions = null)
        {
            var defaults = new IMiddleware<BusPublishContext>[]
            {
                new BusPublishTelemetryMiddleware(logSink),
                new ThreadAffinityCheckMiddleware(threadChecker, logSink),
                new ErrorIsolationMiddleware(logSink),
                new DispatchMiddleware(dispatch, logSink),
            };
            return PipelineFactory.Build(defaults, extensions);
        }
    }
}
