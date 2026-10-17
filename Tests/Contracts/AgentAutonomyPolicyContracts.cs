using System;
using RimMind.Domain.Agent.Policies;
using RimMind.Infrastructure.Verse;
using RimMind.Presentation.Api;
using RimMind.Testing;
using Verse;
using Xunit;

namespace RimMind.Tests.Contracts
{
    public sealed class AgentAutonomyPolicyContracts
    {
        [Fact]
        public void AgentAutonomyPolicy_defaults_and_custom_levels_behave_correctly()
        {
            ContractCaseRunner.Run(
                ("default policy grants full autonomy to survival and marking, approval to bills and blueprints", () =>
                {
                    var policy = AgentAutonomyPolicy.CreateDefault();

                    Assert.Equal(AutonomyLevel.FullAutonomous, policy.GetLevel(AutonomyDomain.EmergencySurvival));
                    Assert.Equal(AutonomyLevel.FullAutonomous, policy.GetLevel(AutonomyDomain.ResourceMarking));
                    Assert.Equal(AutonomyLevel.RequireApproval, policy.GetLevel(AutonomyDomain.WorktableBills));
                    Assert.Equal(AutonomyLevel.RequireApproval, policy.GetLevel(AutonomyDomain.BlueprintPlacement));
                    Assert.Equal(AutonomyLevel.RequireApproval, policy.GetLevel(AutonomyDomain.HighRiskOperations));

                    Assert.True(policy.CanExecuteAutonomous(AutonomyDomain.EmergencySurvival));
                    Assert.True(policy.RequiresApproval(AutonomyDomain.BlueprintPlacement));
                    Assert.False(policy.IsDisabled(AutonomyDomain.WorktableBills));
                }),
                ("custom level overrides take effect immediately and clone works independently", () =>
                {
                    var policy = new AgentAutonomyPolicy();
                    policy.SetLevel(AutonomyDomain.WorktableBills, AutonomyLevel.FullAutonomous);
                    policy.SetLevel(AutonomyDomain.BlueprintPlacement, AutonomyLevel.Disabled);

                    Assert.True(policy.CanExecuteAutonomous(AutonomyDomain.WorktableBills));
                    Assert.True(policy.IsDisabled(AutonomyDomain.BlueprintPlacement));

                    var clone = policy.Clone();
                    clone.SetLevel(AutonomyDomain.WorktableBills, AutonomyLevel.Disabled);

                    Assert.True(clone.IsDisabled(AutonomyDomain.WorktableBills));
                    Assert.True(policy.CanExecuteAutonomous(AutonomyDomain.WorktableBills));
                })
            );
        }

        [Fact]
        public void RimMindAPI_Autonomy_evaluates_pawn_and_fallback_cleanly()
        {
            ContractCaseRunner.Run(
                ("null pawn falls back to default policy with require approval", () =>
                {
                    Assert.False(RimMindAPI.Autonomy.CanExecuteAutonomous(null, AutonomyDomain.BlueprintPlacement));
                    Assert.True(RimMindAPI.Autonomy.RequiresApproval(null, AutonomyDomain.BlueprintPlacement));
                    Assert.True(RimMindAPI.Autonomy.CanExecuteAutonomous(null, AutonomyDomain.EmergencySurvival));
                }),
                ("pawn with custom policy is respected via API", () =>
                {
                    var pawn = new Pawn();
                    var comp = new CompPawnAgent();
                    comp.parent = pawn;
                    comp.AutonomyPolicy.SetLevel(AutonomyDomain.BlueprintPlacement, AutonomyLevel.FullAutonomous);
                    comp.AutonomyPolicy.SetLevel(AutonomyDomain.ResourceMarking, AutonomyLevel.Disabled);

                    pawn.AllComps.Add(comp);

                    Assert.True(RimMindAPI.Autonomy.CanExecuteAutonomous(pawn, AutonomyDomain.BlueprintPlacement));
                    Assert.True(RimMindAPI.Autonomy.IsDisabled(pawn, AutonomyDomain.ResourceMarking));
                })
            );
        }
    }
}
