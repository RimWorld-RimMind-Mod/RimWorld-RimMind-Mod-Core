using System;
using System.Collections.Generic;
using RimMind.Application.Common.Interfaces.Extension;

namespace RimMind.Application.Features.Bridges
{
    /// <summary>
    /// Reusable lifecycle coordinator for external-mod bridge modules and optional skip-check gates.
    /// Guarantees symmetric <see cref="RegisterAll"/> / <see cref="UnregisterAll"/> and per-module
    /// exception isolation so one faulty bridge module cannot block sibling modules.
    /// </summary>
    public sealed class ExternalModBridgeCoordinator
    {
        private readonly Func<bool> _isModAvailable;
        private readonly IReadOnlyList<IBridgeModule> _modules;
        private readonly IReadOnlyList<ISkipCheck> _gates;
        private readonly Action<ISkipCheck>? _registerGate;
        private readonly Func<string, bool>? _unregisterGate;
        private readonly Action<string>? _logInfo;
        private readonly Action<string>? _logWarn;
        private readonly string _inactiveMessage;
        private bool _gatesRegistered;

        public ExternalModBridgeCoordinator(
            Func<bool> isModAvailable,
            IReadOnlyList<IBridgeModule> modules,
            IReadOnlyList<ISkipCheck>? gates = null,
            Action<string>? logInfo = null,
            Action<string>? logWarn = null,
            Action<ISkipCheck>? registerGate = null,
            Func<string, bool>? unregisterGate = null,
            string? inactiveMessage = null)
        {
            _isModAvailable = isModAvailable ?? throw new ArgumentNullException(nameof(isModAvailable));
            _modules = modules ?? throw new ArgumentNullException(nameof(modules));
            _gates = gates ?? Array.Empty<ISkipCheck>();
            _logInfo = logInfo;
            _logWarn = logWarn ?? logInfo;
            _registerGate = registerGate;
            _unregisterGate = unregisterGate;
            _inactiveMessage = string.IsNullOrWhiteSpace(inactiveMessage)
                ? "External dependency not active, bridge modules skipped."
                : inactiveMessage!;
        }

        public IReadOnlyList<IBridgeModule> Modules => _modules;

        public IReadOnlyList<ISkipCheck> Gates => _gates;

        public bool AreAllRegistered
        {
            get
            {
                if (_modules.Count == 0) return false;
                for (int i = 0; i < _modules.Count; i++)
                {
                    var module = _modules[i];
                    if (module == null || !module.IsRegistered)
                        return false;
                }
                return true;
            }
        }

        public void RegisterAll()
        {
            if (!_isModAvailable())
            {
                _logInfo?.Invoke(_inactiveMessage);
                return;
            }

            if (!_gatesRegistered && _gates.Count > 0 && _registerGate != null)
            {
                for (int i = 0; i < _gates.Count; i++)
                {
                    var gate = _gates[i];
                    if (gate == null) continue;
                    try
                    {
                        _registerGate(gate);
                    }
                    catch (Exception ex)
                    {
                        _logWarn?.Invoke($"{gate.Id} gate registration failed: {ex.Message}");
                    }
                }
                _gatesRegistered = true;
            }

            for (int i = 0; i < _modules.Count; i++)
            {
                var module = _modules[i];
                if (module == null) continue;
                try
                {
                    module.Register();
                    _logInfo?.Invoke($"{module.Id} registered={module.IsRegistered}.");
                }
                catch (Exception ex)
                {
                    _logWarn?.Invoke($"{module.Id} registration failed: {ex.Message}");
                }
            }
        }

        public void UnregisterAll()
        {
            if (_gatesRegistered && _gates.Count > 0 && _unregisterGate != null)
            {
                for (int i = 0; i < _gates.Count; i++)
                {
                    var gate = _gates[i];
                    if (gate == null) continue;
                    try
                    {
                        _unregisterGate(gate.Id);
                    }
                    catch (Exception ex)
                    {
                        _logWarn?.Invoke($"{gate.Id} gate cleanup failed: {ex.Message}");
                    }
                }
                _gatesRegistered = false;
            }

            for (int i = 0; i < _modules.Count; i++)
            {
                var module = _modules[i];
                if (module == null) continue;
                try
                {
                    module.Unregister();
                }
                catch (Exception ex)
                {
                    _logWarn?.Invoke($"{module.Id} cleanup failed: {ex.Message}");
                }
            }
        }

        public void Refresh()
        {
            UnregisterAll();
            RegisterAll();
        }
    }
}
