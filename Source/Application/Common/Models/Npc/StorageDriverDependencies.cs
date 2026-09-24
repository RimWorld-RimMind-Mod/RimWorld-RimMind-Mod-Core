using RimMind.Application.Common.Interfaces;
using RimMind.Application.Common.Interfaces.Abstractions;
using RimMind.Application.Common.Interfaces.Context;
using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Application.Common.Interfaces.Npc;

namespace RimMind.Application.Common.Models.Npc
{
    /// <summary>
    /// Parameter object grouping common storage driver dependencies.
    /// Reduces constructor parameter count for StorageDriver implementations.
    /// </summary>
    public sealed class StorageDriverDependencies
    {
        public INpcManager NpcManager { get; }
        public ILogSink LogSink { get; }
        public IContextBuilder ContextBuilder { get; }
        public ISettingsProvider SettingsProvider { get; }
        public IGameContextBuilder GameContextBuilder { get; }
        public IResponseDispatcher ResponseDispatcher { get; }

        public StorageDriverDependencies(
            INpcManager npcManager,
            ILogSink logSink,
            IContextBuilder contextBuilder,
            ISettingsProvider settingsProvider,
            IGameContextBuilder gameContextBuilder,
            IResponseDispatcher responseDispatcher)
        {
            NpcManager = npcManager;
            LogSink = logSink;
            ContextBuilder = contextBuilder;
            SettingsProvider = settingsProvider;
            GameContextBuilder = gameContextBuilder;
            ResponseDispatcher = responseDispatcher;
        }
    }
}
