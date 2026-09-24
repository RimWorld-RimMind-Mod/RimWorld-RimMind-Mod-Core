using System;
using System.Collections.Generic;

namespace RimMind.Infrastructure.UI.AgentFlow
{
    internal enum FlowLabStep
    {
        SelectTarget,
        CreateAgent,
        BuildContext,
        SendRequest,
        ParseDecision,
        MapMechanism,
        DryRun,
        Execute
    }

    internal enum StepStatus
    {
        Pending,
        Active,
        Completed,
        Failed
    }

    internal sealed class AgentFlowStepTracker
    {
        private readonly Dictionary<FlowLabStep, StepStatus> _statuses = new();

        public AgentFlowStepTracker()
        {
            Reset();
        }

        public void Reset()
        {
            foreach (FlowLabStep step in Enum.GetValues(typeof(FlowLabStep)))
                _statuses[step] = StepStatus.Pending;
        }

        public void Set(FlowLabStep step, StepStatus status)
        {
            _statuses[step] = status;
        }

        public StepStatus Get(FlowLabStep step)
        {
            return _statuses.TryGetValue(step, out var status)
                ? status
                : StepStatus.Pending;
        }
    }
}
