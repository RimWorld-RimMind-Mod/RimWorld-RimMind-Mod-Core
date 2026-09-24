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
        private volatile IReadOnlyList<IMiddleware<TContext>> _middlewares = new List<IMiddleware<TContext>>();
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
            }
        }

        public void UseRange(IEnumerable<IMiddleware<TContext>> middlewares)
        {
            lock (_lock)
            {
                var list = new List<IMiddleware<TContext>>(_middlewares);
                list.AddRange(middlewares);
                _middlewares = list.OrderBy(m => m.Order).ToList().AsReadOnly();
            }
        }

        /// <summary>
        /// Sets the extension registry for lazy middleware merging.
        /// Extensions are merged on the first ExecuteAsync call, allowing sub-Mods
        /// to register middlewares after Core initialization.
        /// </summary>
        public void SetExtensionRegistry(IExtensionRegistry<IMiddleware<TContext>> registry)
        {
            _extensionRegistry = registry;
            _extensionsMerged = false;
        }

        public async Task ExecuteAsync(TContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            // Lazy merge: on first execution, merge extension middlewares
            if (!_extensionsMerged && _extensionRegistry != null)
            {
                MergeExtensions();
            }

            var snapshot = _middlewares;
            int index = 0;
            async Task Next(TContext ctx)
            {
                if (ctx.IsShortCircuited) return;
                if (index >= snapshot.Count) return;
                var mw = snapshot[index++];
                await mw.InvokeAsync(ctx, Next).ConfigureAwait(false);
            }
            await Next(context).ConfigureAwait(false);
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
                _extensionsMerged = true;
            }
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
