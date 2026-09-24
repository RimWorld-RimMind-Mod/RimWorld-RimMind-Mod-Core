using System.Threading.Tasks;
using RimMind.Application.Common.Interfaces.Extension;
using RimMind.Application.Common.Models.Pipeline;

namespace RimMind.Application.Common.Interfaces.Pipeline
{
    public interface IMiddleware<TContext> : IExtension where TContext : IPipelineContext
    {
        string Name { get; }
        int Order { get; }
        Task InvokeAsync(TContext context, MiddlewareDelegate<TContext> next);
    }
}
