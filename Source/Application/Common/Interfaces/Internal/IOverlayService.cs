using System.Collections.Generic;
using RimMind.Application.Common.Models.UI;

namespace RimMind.Application.Common.Interfaces.Internal
{
    public interface IOverlayService
    {
        void RegisterPendingRequest(RequestEntry entry);
        IReadOnlyList<RequestEntry> GetPendingRequests();
        bool TryResolve(RequestEntry entry, string choice);
        bool TryDismiss(RequestEntry entry);
        void Clear();
        void Tick();
    }
}
