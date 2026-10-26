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
            if (string.IsNullOrWhiteSpace(modId) || ModCooldowns == null) return 0;

            var registered = ModCooldowns.FindById(modId);
            if (registered != null && registered.CooldownTicks > 0)
                return registered.CooldownTicks;

            foreach (var cd in ModCooldowns.All)
            {
                if (cd == null || cd.CooldownTicks <= 0) continue;
                if (MatchesModId(modId, cd.Id) || MatchesModId(modId, cd.OwnerModId))
                {
                    return cd.CooldownTicks;
                }
            }
            return 0;
        }

        private static bool MatchesModId(string modId, string? candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate)) return false;
            if (string.Equals(modId, candidate, System.StringComparison.OrdinalIgnoreCase))
                return true;

            if (modId.StartsWith(candidate!, System.StringComparison.OrdinalIgnoreCase) &&
                modId.Length > candidate!.Length)
            {
                char sep = modId[candidate.Length];
                if (sep == '.' || sep == '-' || sep == '_' || sep == ':' || sep == '/')
                    return true;
            }
            return false;
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
