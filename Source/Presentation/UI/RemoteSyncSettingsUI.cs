using System;
using System.Threading;
using System.Threading.Tasks;
using RimMind.Application.Common.Constants;
using RimMind.Application.Common.Interfaces.Storage;
using RimMind.Domain.Settings;
using RimMind.Presentation.Runtime.Services;
using RimMind.Presentation.Settings;
using RimMind.Presentation.UI.Framework;
using UnityEngine;
using Verse;

namespace RimMind.Presentation.UI
{
    public class RemoteSyncSettingsUI : ISettingsTab, IRuntimeScopedSettingsTab
    {
        public string Id => "remotesync";
        public string OwnerModId => RimMindOwnerConsts.CoreModId;
        public string Label => "RimMind.Settings.Tab.RemoteSync".Translate();

        private readonly RuntimeServiceRef<RemoteSyncSettings> _settings =
            RuntimeServiceRef<RemoteSyncSettings>.Required();
        private readonly RuntimeServiceRef<IRemoteSyncService> _syncService =
            RuntimeServiceRef<IRemoteSyncService>.Required();
        private readonly GenerationUiState _generationState = new GenerationUiState();
        private Vector2 _scrollPos = Vector2.zero;
        private string _statusText = "";
        private Color _statusColor = Color.white;
        private GenerationUiOperation? _activeOperation;

        public RemoteSyncSettingsUI(RemoteSyncSettings settings, IRemoteSyncService syncService)
        {
            _ = settings ?? throw new ArgumentNullException(nameof(settings));
            _ = syncService ?? throw new ArgumentNullException(nameof(syncService));
        }

        public void Draw(Rect inRect)
        {
            RuntimeServiceScope runtimeScope = RuntimeServiceHub.Shared.Capture();
            Draw(inRect, runtimeScope);
        }

        public void Draw(Rect inRect, RuntimeServiceScope runtimeScope)
        {
            RemoteSyncSettings settings = _settings.Resolve(runtimeScope);
            IRemoteSyncService syncService = _syncService.Resolve(runtimeScope);
            if (_generationState.Refresh(runtimeScope.Generation))
            {
                _activeOperation = null;
                _statusText = string.Empty;
                _statusColor = Color.white;
            }

            float contentH = EstimateHeight();
            Rect viewRect = new Rect(0f, 0f, inRect.width - 16f, contentH);
            Widgets.BeginScrollView(inRect, ref _scrollPos, viewRect);

            var listing = new Listing_Standard();
            listing.Begin(viewRect);

            // Copy to local for CheckboxLabeled ref parameters
            bool autoPull = settings.AutoPull;
            bool autoPush = settings.AutoPush;
            bool syncMemory = settings.SyncMemory;
            bool syncSettings = settings.SyncSettings;
            bool syncAgentIdentity = settings.SyncAgentIdentity;

            SettingsUIDrawer.DrawSectionHeader(listing, "RimMind.Settings.RemoteSync.Section.AutoSync".Translate());

            listing.CheckboxLabeled(
                "RimMind.Settings.RemoteSync.AutoPull".Translate(),
                ref autoPull,
                "RimMind.Settings.RemoteSync.AutoPull.Desc".Translate());

            listing.CheckboxLabeled(
                "RimMind.Settings.RemoteSync.AutoPush".Translate(),
                ref autoPush,
                "RimMind.Settings.RemoteSync.AutoPush.Desc".Translate());

            SettingsUIDrawer.DrawSectionHeader(listing, "RimMind.Settings.RemoteSync.Section.Granularity".Translate());

            listing.CheckboxLabeled(
                "RimMind.Settings.RemoteSync.SyncMemory".Translate(),
                ref syncMemory,
                "RimMind.Settings.RemoteSync.SyncMemory.Desc".Translate());

            listing.CheckboxLabeled(
                "RimMind.Settings.RemoteSync.SyncSettings".Translate(),
                ref syncSettings,
                "RimMind.Settings.RemoteSync.SyncSettings.Desc".Translate());

            listing.CheckboxLabeled(
                "RimMind.Settings.RemoteSync.SyncAgentIdentity".Translate(),
                ref syncAgentIdentity,
                "RimMind.Settings.RemoteSync.SyncAgentIdentity.Desc".Translate());

            // Write back from locals to properties
            settings.AutoPull = autoPull;
            settings.AutoPush = autoPush;
            settings.SyncMemory = syncMemory;
            settings.SyncSettings = syncSettings;
            settings.SyncAgentIdentity = syncAgentIdentity;

            SettingsUIDrawer.DrawSectionHeader(listing, "RimMind.Settings.RemoteSync.Section.Manual".Translate());

            bool isConfigured = syncService.IsConfigured;

            if (!isConfigured)
            {
                GUI.color = Color.yellow;
                listing.Label("RimMind.Settings.RemoteSync.NotConfigured".Translate());
                GUI.color = Color.white;
            }

            Rect pullRow = listing.GetRect(30f);
            if (Widgets.ButtonText(pullRow, "RimMind.Settings.RemoteSync.ManualPull".Translate()))
            {
                if (isConfigured)
                {
                    BeginPull(syncService, runtimeScope.Token);
                }
            }

            Rect pushRow = listing.GetRect(30f);
            if (Widgets.ButtonText(pushRow, "RimMind.Settings.RemoteSync.ManualPush".Translate()))
            {
                if (isConfigured)
                {
                    BeginPush(syncService, runtimeScope.Token);
                }
            }

            if (!string.IsNullOrEmpty(_statusText))
            {
                GUI.color = _statusColor;
                listing.Label(_statusText);
                GUI.color = Color.white;
            }

            listing.End();
            Widgets.EndScrollView();
        }

        private void BeginPull(IRemoteSyncService syncService, RuntimeGenerationToken token)
        {
            var operation = BeginOperation(token, "RimMind.Settings.RemoteSync.Pulling".Translate());
            _ = Task.Run(async () =>
            {
                try
                {
                    var result = await syncService.ManualPullAsync("all", CancellationToken.None);
                    LongEventHandler.ExecuteWhenFinished(() => TryPublish(
                        operation,
                        result.IsOk
                            ? "RimMind.Settings.RemoteSync.PullSuccess".Translate()
                            : "RimMind.Settings.RemoteSync.PullFailed".Translate(result.Error?.Message ?? ""),
                        result.IsOk ? Color.green : Color.red));
                }
                catch (Exception ex)
                {
                    LongEventHandler.ExecuteWhenFinished(() => TryPublish(
                        operation,
                        "RimMind.Settings.RemoteSync.PullFailed".Translate(ex.Message),
                        Color.red));
                }
            });
        }

        private void BeginPush(IRemoteSyncService syncService, RuntimeGenerationToken token)
        {
            var operation = BeginOperation(token, "RimMind.Settings.RemoteSync.Pushing".Translate());
            _ = Task.Run(async () =>
            {
                try
                {
                    var result = await syncService.ManualPushAsync("all", "{}", 0, CancellationToken.None);
                    LongEventHandler.ExecuteWhenFinished(() => TryPublish(
                        operation,
                        result.IsOk
                            ? "RimMind.Settings.RemoteSync.PushSuccess".Translate()
                            : "RimMind.Settings.RemoteSync.PushFailed".Translate(result.Error?.Message ?? ""),
                        result.IsOk ? Color.green : Color.red));
                }
                catch (Exception ex)
                {
                    LongEventHandler.ExecuteWhenFinished(() => TryPublish(
                        operation,
                        "RimMind.Settings.RemoteSync.PushFailed".Translate(ex.Message),
                        Color.red));
                }
            });
        }

        private GenerationUiOperation BeginOperation(RuntimeGenerationToken token, string status)
        {
            var operation = new GenerationUiOperation(
                RuntimeServiceHub.Shared,
                token,
                LifecycleEventSources.RemoteSync);
            _activeOperation = operation;
            _statusText = status;
            _statusColor = Color.cyan;
            return operation;
        }

        private bool TryPublish(GenerationUiOperation operation, string status, Color color)
        {
            if (!operation.CanPublish())
            {
                if (ReferenceEquals(_activeOperation, operation))
                    _activeOperation = null;
                return false;
            }

            if (!ReferenceEquals(_activeOperation, operation))
                return false;

            _activeOperation = null;
            _statusText = status;
            _statusColor = color;
            return true;
        }

        private static float EstimateHeight()
        {
            float h = 30f;
            // AutoSync section: header + 2 checkboxes
            h += 24f + 24f + 24f;
            // Granularity section: header + 3 checkboxes
            h += 24f + 24f + 24f + 24f;
            // Manual section: header + warning? + 2 buttons + status
            h += 24f + 30f + 30f + 24f;
            return h + 40f;
        }
    }
}
