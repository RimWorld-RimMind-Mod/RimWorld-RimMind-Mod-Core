using RimMind.Application.Common.Interfaces.Agent;
using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Application.Common.Interfaces.Mechanisms;
using RimMind.Presentation.Runtime.Services;

namespace RimMind.Presentation.Runtime.Composition
{
    internal static class ToolMechanismComposition
    {
        public static void RegisterAllMechanisms(IGameMechanismRegistry registry)
        {
            registry.Register(new RimMind.Infrastructure.Mechanisms.Pawn.Job.JobMechanism());
            registry.Register(new RimMind.Infrastructure.Mechanisms.Pawn.Draft.DraftMechanism());
            registry.Register(new RimMind.Infrastructure.Mechanisms.Pawn.Work.WorkMechanism());
            registry.Register(new RimMind.Infrastructure.Mechanisms.Pawn.Equipment.EquipmentMechanism());
            registry.Register(new RimMind.Infrastructure.Mechanisms.Pawn.Interaction.InteractionMechanism());
            registry.Register(new RimMind.Infrastructure.Mechanisms.Pawn.Recruit.RecruitMechanism());
            registry.Register(new RimMind.Infrastructure.Mechanisms.Pawn.Thought.ThoughtMechanism());
            registry.Register(new RimMind.Infrastructure.Mechanisms.Pawn.Inspiration.InspirationMechanism());
            registry.Register(new RimMind.Infrastructure.Mechanisms.Pawn.MentalState.MentalStateMechanism());
            registry.Register(new RimMind.Infrastructure.Mechanisms.Pawn.Health.HealthMechanism());
            registry.Register(new RimMind.Infrastructure.Mechanisms.Pawn.Relations.RelationsMechanism());
            registry.Register(new RimMind.Infrastructure.Mechanisms.Pawn.Skill.SkillMechanism());
            registry.Register(new RimMind.Infrastructure.Mechanisms.Pawn.Need.NeedMechanism());

            registry.Register(new RimMind.Infrastructure.Mechanisms.Map.Wealth.WealthMechanism());

            registry.Register(new RimMind.Infrastructure.Mechanisms.World.Faction.FactionMechanism());
            registry.Register(new RimMind.Infrastructure.Mechanisms.World.Storyteller.StorytellerMechanism());
            registry.Register(new RimMind.Infrastructure.Mechanisms.World.ChoiceLetter.ChoiceLetterMechanism());
        }

        public static IActionExecutor ComposeActionExecutor(
            RuntimeServiceBuilder services,
            IGameMechanismRegistry mechanismRegistry)
        {
            var actionExecutor = new RimMind.Infrastructure.Agent.MechanismActionExecutor(mechanismRegistry);
            services.Bind<IActionExecutor>(actionExecutor);
            return actionExecutor;
        }
    }
}
