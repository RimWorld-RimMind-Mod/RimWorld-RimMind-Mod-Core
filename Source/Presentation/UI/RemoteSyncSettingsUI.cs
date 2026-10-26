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
        public string Label => UiText.Get("RimMind.Settings.Tab.RemoteSync", "Remote Sync");

        private readonly RuntimeServiceRef<RemoteSyncSettings> _settings =
            RuntimeServiceRef<RemoteSyncSettings>.Required();
        private readonly RuntimeServiceRef<IRemoteSyncService> _syncService =
            RuntimeServiceRef<IRemoteSyncService>.Required();
        private readonly GenerationUiState _generationState = new GenerationUiState();
        private Vector2 _scrollPos = Vector2.zero;
        private float _lastMeasuredHeight;
        private string _statusText = "";
        private Color _statusColor = Color.white;
        private GenerationUiOperation? _activeOperation;

        internal bool HasActiveOperation => _activeOperation != null;

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

            float contentH = Mathf.Max(EstimateHeight(), _lastMeasuredHeight);
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

            SettingsUIDrawer.DrawSectionHeader(listing, UiText.Get("RimMind.Settings.RemoteSync.Section.AutoSync"));

            listing.CheckboxLabeled(
                UiText.Get("RimMind.Settings.RemoteSync.AutoPull"),
                ref autoPull,
                UiText.Get("RimMind.Settings.RemoteSync.AutoPull.Desc"));

            listing.CheckboxLabeled(
                UiText.Get("RimMind.Settings.RemoteSync.AutoPush"),
                ref autoPush,
                UiText.Get("RimMind.Settings.RemoteSync.AutoPush.Desc"));

            SettingsUIDrawer.DrawSectionHeader(listing, UiText.Get("RimMind.Settings.RemoteSync.Section.Granularity"));

            listing.CheckboxLabeled(
                UiText.Get("RimMind.Settings.RemoteSync.SyncMemory"),
                ref syncMemory,
                UiText.Get("RimMind.Settings.RemoteSync.SyncMemory.Desc"));

            listing.CheckboxLabeled(
                UiText.Get("RimMind.Settings.RemoteSync.SyncSettings"),
                ref syncSettings,
                UiText.Get("RimMind.Settings.RemoteSync.SyncSettings.Desc"));

            listing.CheckboxLabeled(
                UiText.Get("RimMind.Settings.RemoteSync.SyncAgentIdentity"),
                ref syncAgentIdentity,
                UiText.Get("RimMind.Settings.RemoteSync.SyncAgentIdentity.Desc"));

            // Write back from locals to properties
            settings.AutoPull = autoPull;
            settings.AutoPush = autoPush;
            settings.SyncMemory = syncMemory;
            settings.SyncSettings = syncSettings;
            settings.SyncAgentIdentity = syncAgentIdentity;

            SettingsUIDrawer.DrawSectionHeader(listing, UiText.Get("RimMind.Settings.RemoteSync.Section.Manual"));

            bool isConfigured = syncService.IsConfigured;

            if (!isConfigured)
            {
                GUI.color = Color.yellow;
                listing.Label(UiText.Get("RimMind.Settings.RemoteSync.NotConfigured"));
                GUI.color = Color.white;
            }

            bool canRunManualSync = isConfigured && _activeOperation == null;
            bool prevEnabled = GUI.enabled;
            GUI.enabled = prevEnabled && canRunManualSync;

            Rect pullRow = listing.GetRect(30f);
            if (Widgets.ButtonText(pullRow, UiText.Get("RimMind.Settings.RemoteSync.ManualPull")) && canRunManualSync)
            {
                BeginPull(syncService, runtimeScope.Token);
            }

            Rect pushRow = listing.GetRect(30f);
            if (Widgets.ButtonText(pushRow, UiText.Get("RimMind.Settings.RemoteSync.ManualPush")) && canRunManualSync)
            {
                BeginPush(syncService, runtimeScope.Token);
            }

            GUI.enabled = prevEnabled;

            if (!string.IsNullOrEmpty(_statusText))
            {
                GUI.color = _statusColor;
                listing.Label(_statusText);
                GUI.color = Color.white;
            }

            _lastMeasuredHeight = listing.CurHeight + 24f;
            listing.End();
            Widgets.EndScrollView();
        }

        private void BeginPull(IRemoteSyncService syncService, RuntimeGenerationToken token)
        {
            if (_activeOperation != null) return;
            var operation = BeginOperation(token, UiText.Get("RimMind.Settings.RemoteSync.Pulling"));
            _ = Task.Run(async () =>
            {
                try
                {
                    var result = await syncService.ManualPullAsync("all", CancellationToken.None);
                    RimMind.Presentation.Api.RimMindAPI.MainThread.Post(() => TryPublish(
                        operation,
                        result.IsOk
                            ? UiText.Get("RimMind.Settings.RemoteSync.PullSuccess")
                            : UiText.Format("RimMind.Settings.RemoteSync.PullFailed", result.Error?.Message ?? ""),
                        result.IsOk ? Color.green : Color.red));
                }
                catch (Exception ex)
                {
                    RimMind.Presentation.Api.RimMindAPI.MainThread.Post(() => TryPublish(
                        operation,
                        UiText.Format("RimMind.Settings.RemoteSync.PullFailed", ex.Message),
                        Color.red));
                }
            });
        }

        private void BeginPush(IRemoteSyncService syncService, RuntimeGenerationToken token)
        {
            if (_activeOperation != null) return;
            var operation = BeginOperation(token, UiText.Get("RimMind.Settings.RemoteSync.Pushing"));
            _ = Task.Run(async () =>
            {
                try
                {
                    var result = await syncService.ManualPushAsync("all", "{}", 0, CancellationToken.None);
                    RimMind.Presentation.Api.RimMindAPI.MainThread.Post(() => TryPublish(
                        operation,
                        result.IsOk
                            ? UiText.Get("RimMind.Settings.RemoteSync.PushSuccess")
                            : UiText.Format("RimMind.Settings.RemoteSync.PushFailed", result.Error?.Message ?? ""),
                        result.IsOk ? Color.green : Color.red));
                }
                catch (Exception ex)
                {
                    RimMind.Presentation.Api.RimMindAPI.MainThread.Post(() => TryPublish(
                        operation,
                        UiText.Format("RimMind.Settings.RemoteSync.PushFailed", ex.Message),
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
