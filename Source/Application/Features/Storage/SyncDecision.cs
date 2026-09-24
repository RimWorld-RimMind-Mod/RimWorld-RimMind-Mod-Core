namespace RimMind.Application.Features.Storage
{
    /// <summary>
    /// Result of a remote sync comparison.
    /// </summary>
    internal enum SyncDecision
    {
        /// <summary>No sync needed — local and remote are identical.</summary>
        NoChange,
        /// <summary>Remote is newer — should pull.</summary>
        PullRemote,
        /// <summary>Local is newer — should push.</summary>
        PushLocal,
        /// <summary>Remote does not exist — should push.</summary>
        PushNew,
    }
}
