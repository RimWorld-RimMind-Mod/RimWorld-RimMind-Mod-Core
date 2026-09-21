using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RimMind.Domain.ValueObjects;
using RimMind.Domain.Events;
using RimMind.Domain.Enums;
using RimMind.Application.Common.Interfaces;
using RimMind.Application.Common.Interfaces.Client;
using RimMind.Application.Common.Interfaces.Context;
using RimMind.Application.Common.Interfaces.Extension;
using RimMind.Application.Common.Interfaces.Flywheel;
using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Application.Common.Interfaces.Abstractions;
using RimMind.Application.Common.Models.Context;

using IParameterTunerContract = RimMind.Application.Common.Interfaces.Extension.IParameterTuner;

namespace RimMind.Application.Features.Context
{
    internal static class AsyncEnumerable
    {
        public static IAsyncEnumerable<T> Empty<T>() => new EmptyAsyncEnumerable<T>();

        private sealed class EmptyAsyncEnumerable<T> : IAsyncEnumerable<T>
        {
            public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default) => new EmptyAsyncEnumerator<T>();
        }

        private sealed class EmptyAsyncEnumerator<T> : IAsyncEnumerator<T>
        {
            public T Current => default!;
            public ValueTask<bool> MoveNextAsync() => new ValueTask<bool>(false);
            public ValueTask DisposeAsync() => default;
        }
    }
}

namespace RimMind.Tests
{
    internal sealed class VerseTickProvider : RimMind.Application.Common.Interfaces.Abstractions.ITickProvider
    {
        public int TicksGame => 0;
    }
}

namespace RimMind.Infrastructure.UI
{
    public sealed class Window_RimMindSettings : global::Verse.Window { }
    public sealed class Window_ContextPayloadInspector : global::Verse.Window { }

    public static class RequestOverlay
    {
        public static IReadOnlyList<object> Pending { get; } = Array.Empty<object>();
    }
}

namespace RimMind.Presentation.Runtime
{
    internal static class RimMindRuntime
    {
        public static int InitializeCallCount { get; private set; }

        public static void Initialize()
        {
            InitializeCallCount++;
        }

        public static void ResetForTests()
        {
            InitializeCallCount = 0;
        }
    }
}

namespace RimMind.Application.Common.Interfaces.Internal
{
    // Test-only compatibility seam for legacy tests retained on disk until the
    // compact contract-project cutover. Production has no service locator.
    internal static class RimMindServiceLocator
    {
        private static readonly Dictionary<Type, object> Services = new Dictionary<Type, object>();

        public static void Register<T>(T instance) where T : class
            => Services[typeof(T)] = instance;

        public static T? Get<T>() where T : class
            => Services.TryGetValue(typeof(T), out var value) ? (T)value : null;

        public static T? TryGet<T>() where T : class => Get<T>();

        public static bool IsRegistered<T>() => Services.ContainsKey(typeof(T));

        public static void Reset() => Services.Clear();
    }
}
