using System.Threading.Tasks;

namespace RimMind.Application.Common.Interfaces.Pipeline
{
    public interface IPipeline<TContext> where TContext : IPipelineContext
    {
        Task ExecuteAsync(TContext context);
    }
}
