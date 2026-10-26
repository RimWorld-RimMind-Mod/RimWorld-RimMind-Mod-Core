using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using RimMind.Application.Common.Interfaces.Extension;
using RimMind.Application.Common.Interfaces.Pipeline;
using RimMind.Application.Common.Models.Pipeline;

namespace RimMind.Application.Common.Behaviours
{
    public sealed class MutablePipeline<TContext> : IPipeline<TContext>
        where TContext : IPipelineContext
    {
        private static readonly MiddlewareDelegate<TContext> TerminalStep = _ => Task.CompletedTask;

        private volatile IReadOnlyList<IMiddleware<TContext>> _middlewares = new List<IMiddleware<TContext>>();
        private volatile MiddlewareDelegate<TContext>? _compiledPipeline;
        private readonly object _lock = new object();

        // Lazy construction support: extension registry is merged on first ExecuteAsync
        private IExtensionRegistry<IMiddleware<TContext>>? _extensionRegistry;
        private volatile bool _extensionsMerged;

        public void Use(IMiddleware<TContext> middleware)
        {
            lock (_lock)
            {
                var list = new List<IMiddleware<TContext>>(_middlewares) { middleware };
                _middlewares = list.OrderBy(m => m.Order).ToList().AsReadOnly();
                _compiledPipeline = CompilePipeline(_middlewares);
            }
        }

        public void UseRange(IEnumerable<IMiddleware<TContext>> middlewares)
        {
            lock (_lock)
            {
                var list = new List<IMiddleware<TContext>>(_middlewares);
                list.AddRange(middlewares);
                _middlewares = list.OrderBy(m => m.Order).ToList().AsReadOnly();
                _compiledPipeline = CompilePipeline(_middlewares);
            }
        }

        /// <summary>
        /// Sets the extension registry for lazy middleware merging.
        /// Extensions are merged on the first ExecuteAsync call, allowing sub-Mods
        /// to register middlewares after Core initialization.
        /// </summary>
        public void SetExtensionRegistry(IExtensionRegistry<IMiddleware<TContext>> registry)
        {
            lock (_lock)
            {
                _extensionRegistry = registry;
                _extensionsMerged = false;
                _compiledPipeline = null;
            }
        }

        public Task ExecuteAsync(TContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            // Lazy merge: on first execution, merge extension middlewares
            if (!_extensionsMerged && _extensionRegistry != null)
            {
                MergeExtensions();
            }

            var compiled = _compiledPipeline;
            if (compiled == null)
            {
                lock (_lock)
                {
                    compiled = _compiledPipeline ??= CompilePipeline(_middlewares);
                }
            }

            return compiled(context);
        }

        private void MergeExtensions()
        {
            lock (_lock)
            {
                if (_extensionsMerged) return;
                var extra = _extensionRegistry?.All ?? Enumerable.Empty<IMiddleware<TContext>>();
                if (extra.Any())
                {
                    var list = new List<IMiddleware<TContext>>(_middlewares);
                    list.AddRange(extra);
                    _middlewares = list.OrderBy(m => m.Order).ToList().AsReadOnly();
                }
                _compiledPipeline = CompilePipeline(_middlewares);
                _extensionsMerged = true;
            }
        }

        private static MiddlewareDelegate<TContext> CompilePipeline(IReadOnlyList<IMiddleware<TContext>> snapshot)
        {
            MiddlewareDelegate<TContext> next = TerminalStep;
            for (int i = snapshot.Count - 1; i >= 0; i--)
            {
                var mw = snapshot[i];
                var currentNext = next;
                next = ctx =>
                {
                    if (ctx.IsShortCircuited) return Task.CompletedTask;
                    return mw.InvokeAsync(ctx, currentNext);
                };
            }
            return next;
        }
    }

    public static class PipelineFactory
    {
        /// <summary>
        /// Builds a pipeline with default middlewares. Extension middlewares are
        /// lazily merged on first ExecuteAsync if a registry is provided.
        /// </summary>
        public static IPipeline<TContext> Build<TContext>(
            IReadOnlyList<IMiddleware<TContext>> defaults,
            IExtensionRegistry<IMiddleware<TContext>>? extensions = null)
            where TContext : IPipelineContext
        {
            var pipeline = new MutablePipeline<TContext>();
            pipeline.UseRange(defaults);
            if (extensions != null)
            {
                pipeline.SetExtensionRegistry(extensions);
            }
            return pipeline;
        }
    }
}
