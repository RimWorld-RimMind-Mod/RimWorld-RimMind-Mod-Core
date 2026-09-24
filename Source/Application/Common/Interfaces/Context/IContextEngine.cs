using System;

namespace RimMind.Application.Common.Interfaces.Context
{
    /// <summary>
    /// Composite interface aggregating all context engine concerns.
    /// Inherit from a sub-interface when only a subset of functionality is needed:
    ///   <see cref="IContextBuilder"/>      — snapshot building and scheduler access
    ///   <see cref="IContextCache"/>        — cache queries and reset
    ///   <see cref="IContextInvalidation"/> — invalidation notifications
    /// </summary>
    public interface IContextEngine : IContextBuilder, IContextCache, IContextInvalidation, IDisposable
    {
    }
}
