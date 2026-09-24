using System;

namespace RimMind.Application.Features.Requests.Queue
{
    public interface ITickableRequestQueue : IRequestQueue
    {
        int CurrentTick { get; set; }
        Action<string, bool>? LogHandler { get; set; }
        void Tick();
        void Reset();
    }
}
