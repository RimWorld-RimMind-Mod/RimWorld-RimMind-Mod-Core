using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Application.Common.Models;
using RimMind.Domain.Enums;
using Verse;

namespace RimMind.Presentation.Settings
{
    public class ContextSettings : IExposable, IContextSettings
    {
        public bool IncludeRace = true;
        public bool IncludeAge = true;
        public bool IncludeGender = true;
        public bool IncludeBackstory = true;
        public bool IncludeIdeology = true;
        public bool IncludeTraits = true;
        public bool IncludeSkills = true;
        public int MinSkillLevel = 4;
        public bool IncludeHealth = true;
        public bool IncludeCapacities = true;
        public bool IncludeMood = true;
        public bool IncludeMoodThoughts = true;
        public bool IncludeCurrentJob = true;
        public bool IncludeWorkPriorities = true;
        public bool IncludeEquipment = true;
        public bool IncludeInventory = true;
        public bool IncludeLocation = true;
        public bool IncludeRelations = true;
        public bool IncludeGenes = true;
        public bool IncludeSurroundings = true;
        public bool IncludeCombatStatus = true;

        public bool IncludeGameTime = true;
        public bool IncludeColonistCount = true;
        public bool IncludeColonistNames = true;
        public bool IncludeWealth = true;
        public bool IncludeFood = true;
        public bool IncludeSeason = true;
        public bool IncludeWeather = true;
        public bool IncludeThreats = true;

        public float ContextBudget = RimMindDefaults.DefaultContextBudget;
        private float _legacyBudgetW1 = RimMindDefaults.ReserveBudgetWeight;
        private float _legacyBudgetW2 = RimMindDefaults.ContextBudgetWeight;

        public int maxCacheEntries = RimMindDefaults.MaxCacheEntries;
        public int contextBriefLimit = RimMindDefaults.DefaultBriefLimit;
        public float moodDiffThreshold = 5f;
        public float temperatureDiffThreshold = 5f;
        public int environmentScanRadius = 5;
        public int environmentMaxItems = 8;
        public float threatThresholdHigh = 200000f;
        public float threatThresholdMedium = 100000f;
        public float threatThresholdLow = 50000f;

        float IContextBudgetSettings.ContextBudget
        {
            get => ContextBudget;
            set => ContextBudget = value;
        }
        int IContextBudgetSettings.ContextBriefLimit => contextBriefLimit;
        int IContextEnvironmentSettings.EnvironmentScanRadius => environmentScanRadius;
        int IContextEnvironmentSettings.EnvironmentMaxItems => environmentMaxItems;
        float IContextEnvironmentSettings.ThreatThresholdHigh => threatThresholdHigh;
        float IContextEnvironmentSettings.ThreatThresholdMedium => threatThresholdMedium;
        float IContextEnvironmentSettings.ThreatThresholdLow => threatThresholdLow;
        int IContextBudgetSettings.MaxCacheEntries => maxCacheEntries;
        float IContextEnvironmentSettings.MoodDiffThreshold => moodDiffThreshold;
        float IContextEnvironmentSettings.TemperatureDiffThreshold => temperatureDiffThreshold;

        bool IPawnIncludeSettings.IncludeRace
        {
            get => IncludeRace;
            set => IncludeRace = value;
        }
        bool IPawnIncludeSettings.IncludeAge
        {
            get => IncludeAge;
            set => IncludeAge = value;
        }
        bool IPawnIncludeSettings.IncludeGender
        {
            get => IncludeGender;
            set => IncludeGender = value;
        }
        bool IPawnIncludeSettings.IncludeBackstory
        {
            get => IncludeBackstory;
            set => IncludeBackstory = value;
        }
        bool IPawnIncludeSettings.IncludeIdeology
        {
            get => IncludeIdeology;
            set => IncludeIdeology = value;
        }
        bool IPawnIncludeSettings.IncludeTraits
        {
            get => IncludeTraits;
            set => IncludeTraits = value;
        }
        bool IPawnIncludeSettings.IncludeSkills
        {
            get => IncludeSkills;
            set => IncludeSkills = value;
        }
        int IPawnIncludeSettings.MinSkillLevel
        {
            get => MinSkillLevel;
            set => MinSkillLevel = value;
        }
        bool IPawnIncludeSettings.IncludeHealth
        {
            get => IncludeHealth;
            set => IncludeHealth = value;
        }
        bool IPawnIncludeSettings.IncludeCapacities
        {
            get => IncludeCapacities;
            set => IncludeCapacities = value;
        }
        bool IPawnIncludeSettings.IncludeMood
        {
            get => IncludeMood;
            set => IncludeMood = value;
        }
        bool IPawnIncludeSettings.IncludeMoodThoughts
        {
            get => IncludeMoodThoughts;
            set => IncludeMoodThoughts = value;
        }
        bool IPawnIncludeSettings.IncludeCurrentJob
        {
            get => IncludeCurrentJob;
            set => IncludeCurrentJob = value;
        }
        bool IPawnIncludeSettings.IncludeWorkPriorities
        {
            get => IncludeWorkPriorities;
            set => IncludeWorkPriorities = value;
        }
        bool IPawnIncludeSettings.IncludeEquipment
        {
            get => IncludeEquipment;
            set => IncludeEquipment = value;
        }
        bool IPawnIncludeSettings.IncludeInventory
        {
            get => IncludeInventory;
            set => IncludeInventory = value;
        }
        bool IPawnIncludeSettings.IncludeLocation
        {
            get => IncludeLocation;
            set => IncludeLocation = value;
        }
        bool IPawnIncludeSettings.IncludeRelations
        {
            get => IncludeRelations;
            set => IncludeRelations = value;
        }
        bool IPawnIncludeSettings.IncludeGenes
        {
            get => IncludeGenes;
            set => IncludeGenes = value;
        }
        bool IPawnIncludeSettings.IncludeSurroundings
        {
            get => IncludeSurroundings;
            set => IncludeSurroundings = value;
        }
        bool IPawnIncludeSettings.IncludeCombatStatus
        {
            get => IncludeCombatStatus;
            set => IncludeCombatStatus = value;
        }
        bool IMapIncludeSettings.IncludeGameTime
        {
            get => IncludeGameTime;
            set => IncludeGameTime = value;
        }
        bool IColonyIncludeSettings.IncludeColonistCount
        {
            get => IncludeColonistCount;
            set => IncludeColonistCount = value;
        }
        bool IColonyIncludeSettings.IncludeColonistNames
        {
            get => IncludeColonistNames;
            set => IncludeColonistNames = value;
        }
        bool IColonyIncludeSettings.IncludeWealth
        {
            get => IncludeWealth;
            set => IncludeWealth = value;
        }
        bool IColonyIncludeSettings.IncludeFood
        {
            get => IncludeFood;
            set => IncludeFood = value;
        }
        bool IMapIncludeSettings.IncludeSeason
        {
            get => IncludeSeason;
            set => IncludeSeason = value;
        }
        bool IMapIncludeSettings.IncludeWeather
        {
            get => IncludeWeather;
            set => IncludeWeather = value;
        }
        bool IColonyIncludeSettings.IncludeThreats
        {
            get => IncludeThreats;
            set => IncludeThreats = value;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref IncludeRace, "IncludeRace", true);
            Scribe_Values.Look(ref IncludeAge, "IncludeAge", true);
            Scribe_Values.Look(ref IncludeGender, "IncludeGender", true);
            Scribe_Values.Look(ref IncludeBackstory, "IncludeBackstory", true);
            Scribe_Values.Look(ref IncludeIdeology, "IncludeIdeology", true);
            Scribe_Values.Look(ref IncludeTraits, "IncludeTraits", true);
            Scribe_Values.Look(ref IncludeSkills, "IncludeSkills", true);
            Scribe_Values.Look(ref MinSkillLevel, "MinSkillLevel", 4);
            Scribe_Values.Look(ref IncludeHealth, "IncludeHealth", true);
            Scribe_Values.Look(ref IncludeCapacities, "IncludeCapacities", true);
            Scribe_Values.Look(ref IncludeMood, "IncludeMood", true);
            Scribe_Values.Look(ref IncludeMoodThoughts, "IncludeMoodThoughts", true);
            Scribe_Values.Look(ref IncludeCurrentJob, "IncludeCurrentJob", true);
            Scribe_Values.Look(ref IncludeWorkPriorities, "IncludeWorkPriorities", true);
            Scribe_Values.Look(ref IncludeEquipment, "IncludeEquipment", true);
            Scribe_Values.Look(ref IncludeInventory, "IncludeInventory", true);
            Scribe_Values.Look(ref IncludeLocation, "IncludeLocation", true);
            Scribe_Values.Look(ref IncludeRelations, "IncludeRelations", true);
            Scribe_Values.Look(ref IncludeGenes, "IncludeGenes", true);
            Scribe_Values.Look(ref IncludeSurroundings, "IncludeSurroundings", true);
            Scribe_Values.Look(ref IncludeCombatStatus, "IncludeCombatStatus", true);
            Scribe_Values.Look(ref IncludeGameTime, "IncludeGameTime", true);
            Scribe_Values.Look(ref IncludeColonistCount, "IncludeColonistCount", true);
            Scribe_Values.Look(ref IncludeColonistNames, "IncludeColonistNames", true);
            Scribe_Values.Look(ref IncludeWealth, "IncludeWealth", true);
            Scribe_Values.Look(ref IncludeFood, "IncludeFood", true);
            Scribe_Values.Look(ref IncludeSeason, "IncludeSeason", true);
            Scribe_Values.Look(ref IncludeWeather, "IncludeWeather", true);
            Scribe_Values.Look(ref IncludeThreats, "IncludeThreats", true);
            Scribe_Values.Look(ref ContextBudget, "ContextBudget", RimMindDefaults.DefaultContextBudget);
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                Scribe_Values.Look(ref _legacyBudgetW1, "BudgetW1", RimMindDefaults.ReserveBudgetWeight);
                Scribe_Values.Look(ref _legacyBudgetW2, "BudgetW2", RimMindDefaults.ContextBudgetWeight);
            }
            Scribe_Values.Look(ref maxCacheEntries, "maxCacheEntries", RimMindDefaults.MaxCacheEntries);
            Scribe_Values.Look(ref contextBriefLimit, "contextBriefLimit", RimMindDefaults.DefaultBriefLimit);
            Scribe_Values.Look(ref moodDiffThreshold, "moodDiffThreshold", 5f);
            Scribe_Values.Look(ref temperatureDiffThreshold, "temperatureDiffThreshold", 5f);
            Scribe_Values.Look(ref environmentScanRadius, "environmentScanRadius", 5);
            Scribe_Values.Look(ref environmentMaxItems, "environmentMaxItems", 8);
            Scribe_Values.Look(ref threatThresholdHigh, "threatThresholdHigh", 200000f);
            Scribe_Values.Look(ref threatThresholdMedium, "threatThresholdMedium", 100000f);
            Scribe_Values.Look(ref threatThresholdLow, "threatThresholdLow", 50000f);
        }

        public void ApplyPreset(ContextPreset preset)
        {
            switch (preset)
            {
                case ContextPreset.Minimal:
                    IncludeRace = true; IncludeAge = false; IncludeGender = false;
                    IncludeBackstory = false; IncludeIdeology = false;
                    IncludeTraits = false; IncludeSkills = false; MinSkillLevel = 4;
                    IncludeHealth = true; IncludeCapacities = false; IncludeMood = true; IncludeMoodThoughts = false;
                    IncludeCurrentJob = false; IncludeWorkPriorities = false;
                    IncludeEquipment = false; IncludeInventory = false; IncludeLocation = false;
                    IncludeRelations = false; IncludeGenes = false;
                    IncludeSurroundings = false; IncludeCombatStatus = true;
                    IncludeGameTime = false; IncludeColonistCount = true; IncludeColonistNames = false; IncludeWealth = false;
                    IncludeFood = false; IncludeSeason = false;
                    IncludeWeather = true; IncludeThreats = true;
                    ContextBudget = 0.3f;
                    break;
                case ContextPreset.Standard:
                    IncludeRace = true; IncludeAge = true; IncludeGender = true;
                    IncludeBackstory = true; IncludeIdeology = false;
                    IncludeTraits = true; IncludeSkills = true; MinSkillLevel = 4;
                    IncludeHealth = true; IncludeCapacities = true; IncludeMood = true; IncludeMoodThoughts = false;
                    IncludeCurrentJob = true; IncludeWorkPriorities = true;
                    IncludeEquipment = true; IncludeInventory = false; IncludeLocation = false;
                    IncludeRelations = true; IncludeGenes = true;
                    IncludeSurroundings = false; IncludeCombatStatus = true;
                    IncludeGameTime = true; IncludeColonistCount = true; IncludeColonistNames = true; IncludeWealth = false;
                    IncludeFood = true; IncludeSeason = true;
                    IncludeWeather = true; IncludeThreats = true;
                    ContextBudget = RimMindDefaults.DefaultContextBudget;
                    break;
                case ContextPreset.Full:
                    IncludeRace = true; IncludeAge = true; IncludeGender = true;
                    IncludeBackstory = true; IncludeIdeology = true;
                    IncludeTraits = true; IncludeSkills = true; MinSkillLevel = 1;
                    IncludeHealth = true; IncludeCapacities = true; IncludeMood = true; IncludeMoodThoughts = true;
                    IncludeCurrentJob = true; IncludeWorkPriorities = true;
                    IncludeEquipment = true; IncludeInventory = true; IncludeLocation = true;
                    IncludeRelations = true; IncludeGenes = true;
                    IncludeSurroundings = true; IncludeCombatStatus = true;
                    IncludeGameTime = true; IncludeColonistCount = true; IncludeColonistNames = true; IncludeWealth = true;
                    IncludeFood = true; IncludeSeason = true;
                    IncludeWeather = true; IncludeThreats = true;
                    ContextBudget = 1.0f;
                    break;
                case ContextPreset.Custom:
                    break;
            }
        }

        public void ResetToDefault()
        {
            IncludeRace = true; IncludeAge = true; IncludeGender = true;
            IncludeBackstory = true; IncludeIdeology = true;
            IncludeTraits = true; IncludeSkills = true; MinSkillLevel = 4;
            IncludeHealth = true; IncludeCapacities = true; IncludeMood = true; IncludeMoodThoughts = true;
            IncludeCurrentJob = true; IncludeWorkPriorities = true;
            IncludeEquipment = true; IncludeInventory = true; IncludeLocation = true;
            IncludeRelations = true; IncludeGenes = true;
            IncludeSurroundings = true; IncludeCombatStatus = true;
            IncludeGameTime = true; IncludeColonistCount = true; IncludeColonistNames = true;
            IncludeWealth = true; IncludeFood = true; IncludeSeason = true;
            IncludeWeather = true; IncludeThreats = true;
            ContextBudget = RimMindDefaults.DefaultContextBudget;
        }
    }
}
