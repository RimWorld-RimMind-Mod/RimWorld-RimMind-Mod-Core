using System.Threading.Tasks;
using RimMind.Application.Common.Constants;
using RimMind.Application.Common.Interfaces.Abstractions;
using RimMind.Application.Common.Interfaces.Pipeline;
using RimMind.Application.Common.Models.Pipeline;

namespace RimMind.Application.Common.Behaviours
{
    /// <summary>
    /// Base class for IMiddleware implementations. Provides default Id, OwnerModId, and Log property.
    /// Subclasses only need to override Name, Order, and InvokeAsync.
    /// </summary>
    public abstract class MiddlewareBase<TContext> : IMiddleware<TContext> where TContext : IPipelineContext
    {
        public abstract string Name { get; }
        public abstract int Order { get; }
        public virtual string Id => Name;
        public virtual string OwnerModId => RimMindOwnerConsts.CoreModId;

        protected readonly ILogSink? Log;

        protected MiddlewareBase(ILogSink? log = null)
        {
            Log = log;
        }

        public abstract Task InvokeAsync(TContext context, MiddlewareDelegate<TContext> next);
    }
}
