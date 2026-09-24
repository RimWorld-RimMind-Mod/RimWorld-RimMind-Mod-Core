using System;
using System.Collections.Generic;
using System.Threading;
using RimMind.Application.Common.Interfaces.Storage;
using RimMind.Presentation.Runtime.Services;
using UnityEngine;
using Verse;

namespace RimMind.Infrastructure.UI
{
    /// <summary>
    /// UI component for manual NPC data synchronization with remote backend.
    /// </summary>
    public static class NpcSyncActions
    {
        private static readonly NpcSyncStateStore<SyncOperationKey, SyncUiState> States =
            new(MaxRetainedStates);
        private static readonly Dictionary<SyncOperationKey, NpcSyncOperation> ActiveOperations = new();
        private const int MaxRetainedStates = 128;
        private const int OperationTimeoutMs = 120000;
        private static long _visibleGeneration = long.MinValue;

        public static float MeasureHeight(string npcId)
        {
            RuntimeServiceScope runtimeScope = RuntimeServiceHub.Shared.Capture();
            PruneStaleGenerations(runtimeScope.Generation);
            var key = new SyncOperationKey(runtimeScope.Generation, npcId);
            SyncUiState state = GetState(key);
            return string.IsNullOrEmpty(state.LastSyncStatus) ? 30f : 64f;
        }

        public static void DrawNpcSyncActions(Rect rect, string npcId, IRemoteSyncService? syncService)
        {
            if (syncService == null)
            {
                Widgets.Label(rect, "RimMind.RemoteSync.Unavailable".Translate());
                return;
            }

            RuntimeServiceScope runtimeScope = RuntimeServiceHub.Shared.Capture();
            PruneStaleGenerations(runtimeScope.Generation);
            var key = new SyncOperationKey(runtimeScope.Generation, npcId);
            SyncUiState state = GetState(key);

            float buttonHeight = 30f;
            float gap = 4f;
            float curY = rect.y;

            // Status label
            if (!string.IsNullOrEmpty(state.LastSyncStatus))
            {
                var statusRect = new Rect(rect.x, curY, rect.width, buttonHeight);
                Widgets.Label(statusRect, state.LastSyncStatus);
                curY += buttonHeight + gap;
            }

            // Pull button
            var pullRect = new Rect(rect.x, curY, rect.width / 2f - gap / 2f, buttonHeight);
            bool wasEnabled = GUI.enabled;
            GUI.enabled = wasEnabled && !state.IsSyncing;
            bool pullClicked = Widgets.ButtonText(pullRect, "RimMind.RemoteSync.PullNpc".Translate());
            if (pullClicked)
            {
                StartPull(syncService, key, runtimeScope.Token, state);
            }

            // Push button
            var pushRect = new Rect(rect.x + rect.width / 2f + gap / 2f, curY, rect.width / 2f - gap / 2f, buttonHeight);
            bool pushClicked = Widgets.ButtonText(pushRect, "RimMind.RemoteSync.PushNpc".Translate());
            GUI.enabled = wasEnabled;
            if (pushClicked)
            {
                StartPush(syncService, key, runtimeScope.Token, state);
            }
        }

        private static SyncUiState GetState(SyncOperationKey key)
        {
            return States.GetOrAdd(
                key,
                () => new SyncUiState(),
                ActiveOperations.ContainsKey,
                CancelRetainedOperation);
        }

        private static void CancelRetainedOperation(SyncOperationKey key)
        {
            if (!ActiveOperations.TryGetValue(key, out NpcSyncOperation? operation))
                return;
            operation.Cancellation.Cancel();
            CleanupOperation(operation);
        }

        private static void PruneStaleGenerations(long currentGeneration)
        {
            if (_visibleGeneration != currentGeneration)
            {
                _visibleGeneration = currentGeneration;
                foreach (SyncOperationKey key in new List<SyncOperationKey>(ActiveOperations.Keys))
                {
                    if (key.RuntimeGeneration == currentGeneration)
                        continue;
                    NpcSyncOperation operation = ActiveOperations[key];
                    operation.RecordStaleOnce(RuntimeServiceHub.Shared);
                    operation.Cancellation.Cancel();
                    CleanupOperation(operation);
                }

                foreach (SyncOperationKey key in new List<SyncOperationKey>(States.Keys))
                {
                    if (key.RuntimeGeneration != currentGeneration)
                        States.Remove(key);
                }
            }

            if (States.Count <= MaxRetainedStates)
                return;

            foreach (SyncOperationKey key in new List<SyncOperationKey>(States.Keys))
            {
                if (States.Count <= MaxRetainedStates)
                    break;
                if (!ActiveOperations.ContainsKey(key))
                    States.Remove(key);
            }
        }

        private static void StartPull(
            IRemoteSyncService syncService,
            SyncOperationKey key,
            RuntimeGenerationToken token,
            SyncUiState state)
        {
            if (state.IsSyncing)
                return;
            var operation = new NpcSyncOperation(key, token, state);
            ActiveOperations[key] = operation;
            state.IsSyncing = true;
            state.LastSyncStatus = "RimMind.RemoteSync.Syncing".Translate();
            _ = PullNpcAsync(syncService, operation);
        }

        private static void StartPush(
            IRemoteSyncService syncService,
            SyncOperationKey key,
            RuntimeGenerationToken token,
            SyncUiState state)
        {
            if (state.IsSyncing)
                return;
            var operation = new NpcSyncOperation(key, token, state);
            ActiveOperations[key] = operation;
            state.IsSyncing = true;
            state.LastSyncStatus = "RimMind.RemoteSync.Syncing".Translate();
            _ = PushNpcAsync(syncService, operation);
        }

        private static async System.Threading.Tasks.Task PullNpcAsync(
            IRemoteSyncService syncService,
            NpcSyncOperation operation)
        {
            try
            {
                var result = await syncService.ManualPullAsync(operation.Key.NpcId, operation.Cancellation.Token);
                LongEventHandler.ExecuteWhenFinished(() =>
                    TryPublish(
                        operation,
                        result.IsOk
                            ? "RimMind.RemoteSync.PullSuccess".Translate()
                            : $"{"RimMind.RemoteSync.PullFailed".Translate()}: {result.Error.Message}"));
            }
            catch (Exception ex)
            {
                string message = ex.Message;
                LongEventHandler.ExecuteWhenFinished(() =>
                    TryPublish(
                        operation,
                        $"{"RimMind.RemoteSync.PullFailed".Translate()}: {message}"));
            }
            finally
            {
                LongEventHandler.ExecuteWhenFinished(() => CleanupOperation(operation));
            }
        }

        private static async System.Threading.Tasks.Task PushNpcAsync(
            IRemoteSyncService syncService,
            NpcSyncOperation operation)
        {
            try
            {
                var result = await syncService.EnqueuePushAsync(operation.Key.NpcId, "", 0, operation.Cancellation.Token);
                LongEventHandler.ExecuteWhenFinished(() =>
                    TryPublish(
                        operation,
                        result.IsOk
                            ? "RimMind.RemoteSync.PushQueued".Translate()
                            : $"{"RimMind.RemoteSync.PushFailed".Translate()}: {result.Error.Message}"));
            }
            catch (Exception ex)
            {
                string message = ex.Message;
                LongEventHandler.ExecuteWhenFinished(() =>
                    TryPublish(
                        operation,
                        $"{"RimMind.RemoteSync.PushFailed".Translate()}: {message}"));
            }
            finally
            {
                LongEventHandler.ExecuteWhenFinished(() => CleanupOperation(operation));
            }
        }

        private static bool TryPublish(NpcSyncOperation operation, string status)
        {
            if (!ActiveOperations.TryGetValue(operation.Key, out NpcSyncOperation? active)
                || !ReferenceEquals(active, operation))
            {
                return false;
            }

            ActiveOperations.Remove(operation.Key);
            operation.State.IsSyncing = false;
            if (!RuntimeServiceHub.Shared.IsCurrent(operation.RuntimeToken))
            {
                operation.RecordStaleOnce(RuntimeServiceHub.Shared);
                return false;
            }

            operation.State.LastSyncStatus = status;
            return true;
        }

        private static void CancelOperation(NpcSyncOperation operation)
        {
            if (!ActiveOperations.TryGetValue(operation.Key, out NpcSyncOperation? active)
                || !ReferenceEquals(active, operation))
            {
                return;
            }

            if (!RuntimeServiceHub.Shared.IsCurrent(operation.RuntimeToken))
                operation.RecordStaleOnce(RuntimeServiceHub.Shared);
            CleanupOperation(operation);
        }

        private static void CleanupOperation(NpcSyncOperation operation)
        {
            if (ActiveOperations.TryGetValue(operation.Key, out NpcSyncOperation? active)
                && ReferenceEquals(active, operation))
            {
                ActiveOperations.Remove(operation.Key);
                operation.State.IsSyncing = false;
            }

            operation.Dispose();
        }

        private readonly struct SyncOperationKey : IEquatable<SyncOperationKey>
        {
            public SyncOperationKey(long runtimeGeneration, string npcId)
            {
                RuntimeGeneration = runtimeGeneration;
                NpcId = npcId ?? string.Empty;
            }

            public long RuntimeGeneration { get; }
            public string NpcId { get; }

            public bool Equals(SyncOperationKey other)
                => RuntimeGeneration == other.RuntimeGeneration
                    && string.Equals(NpcId, other.NpcId, StringComparison.Ordinal);

            public override bool Equals(object? obj)
                => obj is SyncOperationKey other && Equals(other);

            public override int GetHashCode()
                => unchecked((RuntimeGeneration.GetHashCode() * 397) ^ NpcId.GetHashCode());
        }

        private sealed class SyncUiState
        {
            public string LastSyncStatus { get; set; } = string.Empty;
            public bool IsSyncing { get; set; }
        }

        private sealed class NpcSyncOperation : IDisposable
        {
            private bool _staleRecorded;
            private int _disposed;
            private readonly CancellationTokenRegistration _cancellationRegistration;

            public NpcSyncOperation(
                SyncOperationKey key,
                RuntimeGenerationToken runtimeToken,
                SyncUiState state)
            {
                Key = key;
                RuntimeToken = runtimeToken;
                State = state;
                Cancellation = new CancellationTokenSource();
                _cancellationRegistration = Cancellation.Token.Register(() =>
                    LongEventHandler.ExecuteWhenFinished(() => CancelOperation(this)));
                Cancellation.CancelAfter(OperationTimeoutMs);
            }

            public SyncOperationKey Key { get; }
            public RuntimeGenerationToken RuntimeToken { get; }
            public SyncUiState State { get; }
            public CancellationTokenSource Cancellation { get; }

            public void RecordStaleOnce(RuntimeServiceHub runtimeHub)
            {
                if (_staleRecorded)
                    return;
                _staleRecorded = true;
                runtimeHub.RecordStaleCompletion(LifecycleEventSources.NpcSync);
            }

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) != 0)
                    return;
                _cancellationRegistration.Dispose();
                Cancellation.Dispose();
            }
        }
    }
}
