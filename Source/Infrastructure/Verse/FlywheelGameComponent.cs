using System;
using RimMind.Application.Common.Interfaces.Flywheel;
using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Application.Common.Models;
using RimMind.Application.Common.Models.Flywheel;
using RimMind.Domain.ValueObjects;
using RimMind.Presentation.Runtime.Services;

using Verse;

namespace RimMind.Infrastructure.Verse
{
    public class FlywheelGameComponent : GameComponent
    {
        private int _lastAnalysisTick;
        private IContextCalibrationSettings? _calibrationSettings;
        private ITelemetryCollector? _telemetryCollector;
        private IFlywheelRuleEngine? _ruleEngine;
        private readonly RuntimeBinding _binding = new RuntimeBinding();

        private void EnsureCached()
        {
            _binding.Refresh(scope =>
            {
                _calibrationSettings = scope.GetOptional<IContextCalibrationSettings>();
                _telemetryCollector = scope.GetOptional<ITelemetryCollector>();
                _ruleEngine = scope.GetOptional<IFlywheelRuleEngine>();
                return null;
            });
        }

        public void Dispose()
        {
            _binding.Dispose();
        }

        private int AnalysisIntervalTicks
        {
            get
            {
                EnsureCached();
                return _calibrationSettings?.ContextCalibrateInterval ?? RimMindDefaults.FlywheelCalibrateInterval;
            }
        }

        public FlywheelGameComponent() : base() { }
        public FlywheelGameComponent(Game game) : base() { }

        public override void StartedNewGame()
        {
            base.StartedNewGame();
            _lastAnalysisTick = 0;
        }

        public override void LoadedGame()
        {
            base.LoadedGame();
            _lastAnalysisTick = 0;
        }

        public override void ExposeData()
        {
            base.ExposeData();
        }

        public override void GameComponentTick()
        {
            base.GameComponentTick();
            int ticks = Find.TickManager?.TicksGame ?? 0;
            if (_lastAnalysisTick == 0)
                _lastAnalysisTick = ticks;
            if (ticks - _lastAnalysisTick >= AnalysisIntervalTicks)
            {
                _lastAnalysisTick = ticks;
                try
                {
                    RunPeriodicAnalysis();
                }
                catch (Exception ex) { RimMindErrors.Warn($"[RimMind] Flywheel analysis failed: {ex.Message}"); }
            }
        }

        private void RunPeriodicAnalysis()
        {
            EnsureCached();
            var records = _telemetryCollector?.GetRecentRecords(RimMindDefaults.TelemetryRecordLimit);
            if (records == null || records.Count == 0) return;
            _ruleEngine?.Analyze(records);
        }
    }
}
