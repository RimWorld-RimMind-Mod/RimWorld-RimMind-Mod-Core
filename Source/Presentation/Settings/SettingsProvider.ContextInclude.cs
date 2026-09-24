using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Domain.Enums;

namespace RimMind.Presentation.Settings
{
    internal sealed partial class SettingsProvider
    {
        bool IPawnIncludeSettings.IncludeRace { get => Context.IncludeRace; set => Context.IncludeRace = value; }
        bool IPawnIncludeSettings.IncludeAge { get => Context.IncludeAge; set => Context.IncludeAge = value; }
        bool IPawnIncludeSettings.IncludeGender { get => Context.IncludeGender; set => Context.IncludeGender = value; }
        bool IPawnIncludeSettings.IncludeBackstory { get => Context.IncludeBackstory; set => Context.IncludeBackstory = value; }
        bool IPawnIncludeSettings.IncludeIdeology { get => Context.IncludeIdeology; set => Context.IncludeIdeology = value; }
        bool IPawnIncludeSettings.IncludeTraits { get => Context.IncludeTraits; set => Context.IncludeTraits = value; }
        bool IPawnIncludeSettings.IncludeSkills { get => Context.IncludeSkills; set => Context.IncludeSkills = value; }
        int IPawnIncludeSettings.MinSkillLevel { get => Context.MinSkillLevel; set => Context.MinSkillLevel = value; }
        bool IPawnIncludeSettings.IncludeHealth { get => Context.IncludeHealth; set => Context.IncludeHealth = value; }
        bool IPawnIncludeSettings.IncludeCapacities { get => Context.IncludeCapacities; set => Context.IncludeCapacities = value; }
        bool IPawnIncludeSettings.IncludeMood { get => Context.IncludeMood; set => Context.IncludeMood = value; }
        bool IPawnIncludeSettings.IncludeMoodThoughts { get => Context.IncludeMoodThoughts; set => Context.IncludeMoodThoughts = value; }
        bool IPawnIncludeSettings.IncludeCurrentJob { get => Context.IncludeCurrentJob; set => Context.IncludeCurrentJob = value; }
        bool IPawnIncludeSettings.IncludeWorkPriorities { get => Context.IncludeWorkPriorities; set => Context.IncludeWorkPriorities = value; }
        bool IPawnIncludeSettings.IncludeEquipment { get => Context.IncludeEquipment; set => Context.IncludeEquipment = value; }
        bool IPawnIncludeSettings.IncludeInventory { get => Context.IncludeInventory; set => Context.IncludeInventory = value; }
        bool IPawnIncludeSettings.IncludeLocation { get => Context.IncludeLocation; set => Context.IncludeLocation = value; }
        bool IPawnIncludeSettings.IncludeRelations { get => Context.IncludeRelations; set => Context.IncludeRelations = value; }
        bool IPawnIncludeSettings.IncludeGenes { get => Context.IncludeGenes; set => Context.IncludeGenes = value; }
        bool IPawnIncludeSettings.IncludeSurroundings { get => Context.IncludeSurroundings; set => Context.IncludeSurroundings = value; }
        bool IPawnIncludeSettings.IncludeCombatStatus { get => Context.IncludeCombatStatus; set => Context.IncludeCombatStatus = value; }
        bool IMapIncludeSettings.IncludeGameTime { get => Context.IncludeGameTime; set => Context.IncludeGameTime = value; }
        bool IColonyIncludeSettings.IncludeColonistCount { get => Context.IncludeColonistCount; set => Context.IncludeColonistCount = value; }
        bool IColonyIncludeSettings.IncludeColonistNames { get => Context.IncludeColonistNames; set => Context.IncludeColonistNames = value; }
        bool IColonyIncludeSettings.IncludeWealth { get => Context.IncludeWealth; set => Context.IncludeWealth = value; }
        bool IColonyIncludeSettings.IncludeFood { get => Context.IncludeFood; set => Context.IncludeFood = value; }
        bool IMapIncludeSettings.IncludeSeason { get => Context.IncludeSeason; set => Context.IncludeSeason = value; }
        bool IMapIncludeSettings.IncludeWeather { get => Context.IncludeWeather; set => Context.IncludeWeather = value; }
        bool IColonyIncludeSettings.IncludeThreats { get => Context.IncludeThreats; set => Context.IncludeThreats = value; }

        float IContextBudgetSettings.ContextBudget { get => Context.ContextBudget; set => Context.ContextBudget = value; }
        int IContextBudgetSettings.ContextBriefLimit => Context.ContextBriefLimit;
        int IContextBudgetSettings.MaxCacheEntries => Context.MaxCacheEntries;

        int IContextEnvironmentSettings.EnvironmentScanRadius => Context.EnvironmentScanRadius;
        int IContextEnvironmentSettings.EnvironmentMaxItems => Context.EnvironmentMaxItems;
        float IContextEnvironmentSettings.ThreatThresholdHigh => Context.ThreatThresholdHigh;
        float IContextEnvironmentSettings.ThreatThresholdMedium => Context.ThreatThresholdMedium;
        float IContextEnvironmentSettings.ThreatThresholdLow => Context.ThreatThresholdLow;
        float IContextEnvironmentSettings.MoodDiffThreshold => Context.MoodDiffThreshold;
        float IContextEnvironmentSettings.TemperatureDiffThreshold => Context.TemperatureDiffThreshold;

        void IContextSettings.ApplyPreset(ContextPreset preset) => Context.ApplyPreset(preset);
        void IContextSettings.ResetToDefault() => Context.ResetToDefault();
    }
}
