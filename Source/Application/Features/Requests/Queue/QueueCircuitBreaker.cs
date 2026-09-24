using System.Collections.Generic;
using RimMind.Application.Common.Interfaces.Abstractions;
using RimMind.Application.Common.Interfaces.Extension;
using RimMind.Application.Common.Interfaces.Internal;

namespace RimMind.Application.Features.Requests.Queue
{
    /// <summary>
    /// Encapsulates cooldown tracking and circuit breaker state management for the request queue.
    /// Delegates low-level cooldown storage to <see cref="CooldownTable"/>.
    /// </summary>
    internal sealed class QueueCircuitBreaker
    {
        private readonly CooldownTable _cooldowns;
        private readonly ISettingsProvider _settings;
        public IExtensionRegistry<IModCooldown>? ModCooldowns { get; set; }

        public QueueCircuitBreaker(ISettingsProvider settings, ILogSink? logSink = null, IExtensionRegistry<IModCooldown>? modCooldowns = null)
        {
            _settings = settings;
            _cooldowns = new CooldownTable(logSink);
            ModCooldowns = modCooldowns;
        }

        public CooldownTable Cooldowns => _cooldowns;

        public bool IsOnCooldown(string modId, int currentTick)
            => _cooldowns.IsOnCooldown(modId, currentTick);

        public int GetCooldownTicksLeft(string modId, int currentTick)
            => _cooldowns.GetCooldownTicksLeft(modId, currentTick);

        public int GetModCooldownTicks(string modId)
        {
            if (ModCooldowns != null)
            {
                var registered = ModCooldowns.FindById(modId);
                if (registered != null && registered.CooldownTicks > 0)
                    return registered.CooldownTicks;

                foreach (var cd in ModCooldowns.All)
                {
                    if (cd == null) continue;
                    if (string.Equals(cd.Id, modId, System.StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(cd.OwnerModId, modId, System.StringComparison.OrdinalIgnoreCase) ||
                        modId.IndexOf(cd.Id, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        if (cd.CooldownTicks > 0) return cd.CooldownTicks;
                    }
                }
            }
            return _cooldowns.GetModCooldownTicks(modId);
        }

        public void SetCooldown(string modId, int ticksRemaining)
            => _cooldowns.Set(modId, ticksRemaining);

        public IReadOnlyDictionary<string, int> GetCooldownSnapshot()
            => _cooldowns.GetSnapshot();

        public IReadOnlyDictionary<string, int> GetAllCooldowns()
            => _cooldowns.GetAll();

        public void ClearCooldown(string modId)
            => _cooldowns.Clear(modId);

        public void ClearAllCooldowns()
            => _cooldowns.ClearAll();

        public void TickCooldowns()
            => _cooldowns.Tick();

        public int FailureThreshold => _settings.CircuitBreakerFailureThreshold;
        public int OpenDurationSec => _settings.CircuitBreakerOpenDurationSec;
    }
}
