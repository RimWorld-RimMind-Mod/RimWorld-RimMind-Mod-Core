using System;
using System.Collections.Generic;
using HarmonyLib;
using RimMind.Application.Common.Interfaces.Extension;
using RimMind.Application.Features.Bridges;
using RimMind.Domain.ValueObjects;
using Verse;

namespace RimMind.Presentation.Api
{
    public static partial class RimMindAPI
    {
        /// <summary>
        /// Verse-aware factory facade for Core's soft-dependency Bridge Kit
        /// (<see cref="SoftDependencyDetector"/>, <see cref="ReflectionAccessor"/>,
        /// and <see cref="ExternalModBridgeCoordinator"/>).
        /// </summary>
        public static class Bridges
        {
            private static readonly Func<string, bool> DefaultIsModActive = IsModActiveOnMainThread;
            private static readonly Func<string, Type?> DefaultResolveType = ResolveTypeInAnyAssembly;
            private static readonly Func<int> DefaultGetCurrentTick = GetCurrentTickSafe;
            private static readonly Action<string> DefaultLogInfo = msg => Log.Message(msg);
            private static readonly Action<string> DefaultLogWarn = msg => RimMindErrors.Warn(msg);

            public static SoftDependencyDetector CreateDetector(
                string packageId,
                IReadOnlyList<string>? candidateTypeNames = null,
                int cacheIntervalTicks = SoftDependencyDetector.DefaultCacheIntervalTicks)
            {
                return new SoftDependencyDetector(
                    packageId,
                    candidateTypeNames,
                    DefaultIsModActive,
                    DefaultResolveType,
                    DefaultGetCurrentTick,
                    cacheIntervalTicks);
            }

            public static ReflectionAccessor CreateReflectionAccessor(
                string? logPrefix = null,
                Action<string>? warnCallback = null,
                Func<string, Type?>? resolveType = null)
            {
                return new ReflectionAccessor(
                    resolveType ?? DefaultResolveType,
                    warnCallback ?? DefaultLogWarn,
                    logPrefix);
            }

            public static ExternalModBridgeCoordinator CreateCoordinator(
                Func<bool> isModAvailable,
                IReadOnlyList<IBridgeModule> modules,
                IReadOnlyList<ISkipCheck>? gates = null,
                Action<string>? logInfo = null,
                Action<string>? logWarn = null,
                string? inactiveMessage = null)
            {
                return new ExternalModBridgeCoordinator(
                    isModAvailable,
                    modules,
                    gates,
                    logInfo ?? DefaultLogInfo,
                    logWarn ?? DefaultLogWarn,
                    gate => Extensions<ISkipCheck>().Register(gate),
                    gateId => Extensions<ISkipCheck>().Unregister(gateId),
                    inactiveMessage);
            }

            private static bool IsModActiveOnMainThread(string packageId)
            {
                if (string.IsNullOrWhiteSpace(packageId)) return false;
                try
                {
                    return ModsConfig.IsActive(packageId);
                }
                catch
                {
                    return false;
                }
            }

            private static Type? ResolveTypeInAnyAssembly(string typeName)
            {
                if (string.IsNullOrWhiteSpace(typeName)) return null;
                try
                {
                    if (UnityData.IsInMainThread)
                    {
                        var found = GenTypes.GetTypeInAnyAssembly(typeName);
                        if (found != null) return found;
                    }
                }
                catch
                {
                    // Fall through to AccessTools.TypeByName when UnityData/GenTypes is not initialized.
                }

                try
                {
                    return AccessTools.TypeByName(typeName);
                }
                catch
                {
                    return Type.GetType(typeName, throwOnError: false);
                }
            }

            private static int GetCurrentTickSafe()
            {
                try
                {
                    return Find.TickManager?.TicksGame ?? 0;
                }
                catch
                {
                    return 0;
                }
            }
        }
    }
}
