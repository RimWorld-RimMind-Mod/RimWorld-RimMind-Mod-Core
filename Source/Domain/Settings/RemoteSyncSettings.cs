namespace RimMind.Domain.Settings
{
    /// <summary>
    /// User-controlled remote sync settings. Persisted via RimWorld IExposable + Scribe.
    /// </summary>
    public sealed class RemoteSyncSettings
    {
        public bool AutoPull { get; set; } = false;
        public bool AutoPush { get; set; } = false;
        public int PushDebounceSeconds { get; set; } = 30;
        public bool SyncMemory { get; set; } = true;
        public bool SyncSettings { get; set; } = false;
        public bool SyncAgentIdentity { get; set; } = false;
    }
}
