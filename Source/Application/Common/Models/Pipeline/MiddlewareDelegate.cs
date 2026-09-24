using System;
using System.Threading.Tasks;
using RimMind.Application.Common.Interfaces.Pipeline;

namespace RimMind.Application.Common.Models.Pipeline
{
    public delegate Task MiddlewareDelegate<TContext>(TContext context) where TContext : IPipelineContext;
}
