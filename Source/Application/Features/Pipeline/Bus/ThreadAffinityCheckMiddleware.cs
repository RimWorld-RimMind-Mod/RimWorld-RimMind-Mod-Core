using System;
using System.Threading.Tasks;
using RimMind.Application.Common.Constants;
using RimMind.Application.Common.Interfaces.Abstractions;
using RimMind.Application.Common.Interfaces.Pipeline;
using RimMind.Application.Common.Models.Pipeline;
using RimMind.Domain.Common;

namespace RimMind.Application.Features.Pipeline.Bus
{
    internal sealed class ThreadAffinityCheckMiddleware : IMiddleware<BusPublishContext>
    {
        public string Name => "BusThreadAffinity";
        public int Order => 50;
        public string Id => "BusThreadAffinity";
        public string OwnerModId => RimMindOwnerConsts.CoreModId;

        private readonly IThreadChecker? _threadChecker;
        private readonly ILogSink? _log;

        public ThreadAffinityCheckMiddleware(IThreadChecker? threadChecker = null, ILogSink? log = null)
        {
            _threadChecker = threadChecker;
            _log = log;
        }

        public Task InvokeAsync(BusPublishContext context, MiddlewareDelegate<BusPublishContext> next)
        {
            if (_threadChecker != null && !_threadChecker.IsMainThread)
            {
                _log?.Warning("[BusThreadAffinity] Bus publish called from non-main thread — this may cause race conditions in RimWorld's single-threaded simulation");
                throw new InvalidOperationException(
                    "Bus publish must be called from the main thread. " +
                    "Use LongEventHandler or QueueWorkItem to marshal to the main thread first.");
            }
            return next(context);
        }
    }
}
