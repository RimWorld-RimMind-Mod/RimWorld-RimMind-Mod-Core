using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using RimMind.Application.Common.Interfaces.Abstractions;
using RimMind.Application.Common.Interfaces.Flywheel;
using RimMind.Application.Common.Models;

namespace RimMind.Application.Features.Flywheel
{
    public sealed class FlywheelParameterStore : IFlywheelParameterStore
    {
        private const float ChangeNotificationThreshold = 0.0001f;

        private readonly ConcurrentDictionary<string, float> _parameters
            = new ConcurrentDictionary<string, float>();
        private readonly ConcurrentDictionary<string, float> _defaults
            = new ConcurrentDictionary<string, float>();
        private readonly ILogSink? _log;

        /// <summary>L2: per-key priority overrides set by the flywheel tuning loop.</summary>
        public Dictionary<string, float> KeyPriorityOverrides { get; set; } = new Dictionary<string, float>();

        /// <summary>L2: per-key feedback scores from IRelevanceLearner (populated in L5).</summary>
        public Dictionary<string, float> KeyFeedbackScores { get; set; } = new Dictionary<string, float>();

        public event Action<string, float>? OnParameterChanged;

        public FlywheelParameterStore(ILogSink? log = null)
        {
            _log = log;
            InitializeDefaults();
        }

        public int TotalBudget => _parameters.TryGetValue("ContextBudget", out var val) ? (int)(val * RimMindDefaults.DefaultTotalBudget) : RimMindDefaults.DefaultTotalBudget;

        public float Get(string key)
        {
            return _parameters.TryGetValue(key, out var val) ? val : 0f;
        }

        public void UpdateParameter(string key, float value)
        {
            var clampedValue = ClampParameter(key, value);
            var oldValue = Get(key);
            _parameters[key] = clampedValue;
            if (Math.Abs(oldValue - clampedValue) > ChangeNotificationThreshold)
                OnParameterChanged?.Invoke(key, clampedValue);
        }

        public void ResetToDefault(string key)
        {
            if (_defaults.TryGetValue(key, out var def))
            {
                _parameters[key] = def;
                OnParameterChanged?.Invoke(key, def);
            }
        }

        public void ResetAll()
        {
            foreach (var kvp in _defaults)
            {
                _parameters[kvp.Key] = kvp.Value;
                OnParameterChanged?.Invoke(kvp.Key, kvp.Value);
            }
        }

        public Dictionary<string, float> GetAll()
        {
            return new Dictionary<string, float>(_parameters);
        }

        public Dictionary<string, float> GetDefaults()
        {
            return new Dictionary<string, float>(_defaults);
        }

        private void InitializeDefaults()
        {
            _defaults["ContextBudget"] = 1.0f;
            _defaults["MaxCacheEntries"] = RimMindDefaults.MaxCacheEntries;
            _defaults["W1"] = 0.30f;
            _defaults["W2"] = 0.25f;
            _defaults["W3"] = 0.15f;
            _defaults["W4"] = 0.10f;
            _defaults["W5"] = 0.15f;
            _defaults["W6"] = 0.05f;
            _defaults["RecencyHalflife"] = 30000f;
            _defaults["CooldownWindow"] = 5000f;
            _defaults["Alpha"] = 0.01f;
            _defaults["AlphaSmooth"] = 0.7f;
            _defaults["PromoteThreshold"] = 0.8f;
            _defaults["DemoteThreshold"] = 0.2f;
            foreach (var kvp in _defaults)
                _parameters[kvp.Key] = kvp.Value;
        }

        public (List<string> keys, List<float> values) GetSaveSnapshot()
        {
            var keys = new List<string>();
            var values = new List<float>();
            foreach (var kvp in _parameters)
            {
                keys.Add(kvp.Key);
                values.Add(kvp.Value);
            }
            return (keys, values);
        }

        public void LoadFromSnapshot(List<string> keys, List<float> values)
        {
            _parameters.Clear();
            foreach (var kvp in _defaults)
                _parameters[kvp.Key] = kvp.Value;

            if (keys != null && values != null)
            {
                for (int i = 0; i < System.Math.Min(keys.Count, values.Count); i++)
                    _parameters[keys[i]] = ClampParameter(keys[i], values[i]);
            }
        }

        private static float ClampParameter(string key, float value)
        {
            switch (key)
            {
                case "W1":
                case "W2":
                case "W3":
                case "W4":
                case "W5":
                case "W6":
                case "Alpha":
                case "AlphaSmooth":
                case "PromoteThreshold":
                case "DemoteThreshold":
                    return Math.Max(0f, Math.Min(1f, value));
                case "ContextBudget":
                    return Math.Max(0.3f, Math.Min(2f, value));
                default:
                    return value;
            }
        }

        public (List<string> keys, List<float> values) GetKeyPriorityOverridesSnapshot()
        {
            var keys = new List<string>();
            var values = new List<float>();
            foreach (var kvp in KeyPriorityOverrides)
            {
                keys.Add(kvp.Key);
                values.Add(kvp.Value);
            }
            return (keys, values);
        }

        public void LoadKeyPriorityOverridesSnapshot(List<string> keys, List<float> values)
        {
            KeyPriorityOverrides.Clear();
            if (keys != null && values != null)
            {
                for (int i = 0; i < System.Math.Min(keys.Count, values.Count); i++)
                    KeyPriorityOverrides[keys[i]] = values[i];
            }
        }

        public (List<string> keys, List<float> values) GetKeyFeedbackScoresSnapshot()
        {
            var keys = new List<string>();
            var values = new List<float>();
            foreach (var kvp in KeyFeedbackScores)
            {
                keys.Add(kvp.Key);
                values.Add(kvp.Value);
            }
            return (keys, values);
        }

        public void LoadKeyFeedbackScoresSnapshot(List<string> keys, List<float> values)
        {
            KeyFeedbackScores.Clear();
            if (keys != null && values != null)
            {
                for (int i = 0; i < System.Math.Min(keys.Count, values.Count); i++)
                    KeyFeedbackScores[keys[i]] = values[i];
            }
        }

        public void RecordAction(string npcId, string actionType)
        {
            _log?.Message($"[Flywheel] RecordAction: NpcId={npcId}, ActionType={actionType}");
            // Future: accumulate per-NPC action counts and adjust Alpha/W1/W2 adaptively.
        }
    }
}
