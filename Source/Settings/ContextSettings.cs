using System.Collections.Generic;
using Verse;

namespace RimMind.Core.Settings
{
    public class ContextSettings : IExposable
    {
        public bool IncludeRace          = true;
        public bool IncludeAge           = true;
        public bool IncludeGender        = true;
        public bool IncludeBackstory     = true;
        public bool IncludeIdeology      = true;
        public bool IncludeTraits        = true;
        public bool IncludeSkills        = true;
        public int  MinSkillLevel        = 4;
        public bool IncludeHealth        = true;
        public bool IncludeCapacities    = true;
        public bool IncludeMood          = true;
        public bool IncludeMoodThoughts  = true;
        public bool IncludeCurrentJob    = true;
        public bool IncludeWorkPriorities = true;
        public bool IncludeEquipment     = true;
        public bool IncludeInventory     = true;
        public bool IncludeLocation      = true;
        public bool IncludeRelations     = true;
        public bool IncludeGenes         = true;
        public bool IncludeSurroundings  = true;
        public bool IncludeCombatStatus  = true;

        public bool IncludeGameTime      = true;
        public bool IncludeColonistCount = true;
        public bool IncludeColonistNames = true;
        public bool IncludeWealth        = true;
        public bool IncludeFood          = true;
        public bool IncludeSeason        = true;
        public bool IncludeWeather       = true;
        public bool IncludeThreats       = true;

        public float ContextBudget = 0.6f;
        public float BudgetW1 = 0.4f;
        public float BudgetW2 = 0.6f;

        public void ExposeData()
        {
            Scribe_Values.Look(ref IncludeRace,           "IncludeRace",           true);
            Scribe_Values.Look(ref IncludeAge,            "IncludeAge",            true);
            Scribe_Values.Look(ref IncludeGender,         "IncludeGender",         true);
            Scribe_Values.Look(ref IncludeBackstory,      "IncludeBackstory",      true);
            Scribe_Values.Look(ref IncludeIdeology,       "IncludeIdeology",       true);
            Scribe_Values.Look(ref IncludeTraits,         "IncludeTraits",         true);
            Scribe_Values.Look(ref IncludeSkills,         "IncludeSkills",         true);
            Scribe_Values.Look(ref MinSkillLevel,         "MinSkillLevel",         4);
            Scribe_Values.Look(ref IncludeHealth,         "IncludeHealth",         true);
            Scribe_Values.Look(ref IncludeCapacities,     "IncludeCapacities",     true);
            Scribe_Values.Look(ref IncludeMood,           "IncludeMood",           true);
            Scribe_Values.Look(ref IncludeMoodThoughts,   "IncludeMoodThoughts",   true);
            Scribe_Values.Look(ref IncludeCurrentJob,     "IncludeCurrentJob",     true);
            Scribe_Values.Look(ref IncludeWorkPriorities, "IncludeWorkPriorities", true);
            Scribe_Values.Look(ref IncludeEquipment,      "IncludeEquipment",      true);
            Scribe_Values.Look(ref IncludeInventory,      "IncludeInventory",      true);
            Scribe_Values.Look(ref IncludeLocation,       "IncludeLocation",       true);
            Scribe_Values.Look(ref IncludeRelations,      "IncludeRelations",      true);
            Scribe_Values.Look(ref IncludeGenes,          "IncludeGenes",          true);
            Scribe_Values.Look(ref IncludeSurroundings,   "IncludeSurroundings",   true);
            Scribe_Values.Look(ref IncludeCombatStatus,   "IncludeCombatStatus",   true);
            Scribe_Values.Look(ref IncludeGameTime,       "IncludeGameTime",       true);
            Scribe_Values.Look(ref IncludeColonistCount,  "IncludeColonistCount",  true);
            Scribe_Values.Look(ref IncludeColonistNames,  "IncludeColonistNames",  true);
            Scribe_Values.Look(ref IncludeWealth,         "IncludeWealth",         true);
            Scribe_Values.Look(ref IncludeFood,           "IncludeFood",           true);
            Scribe_Values.Look(ref IncludeSeason,         "IncludeSeason",         true);
            Scribe_Values.Look(ref IncludeWeather,        "IncludeWeather",        true);
            Scribe_Values.Look(ref IncludeThreats,        "IncludeThreats",        true);
            Scribe_Values.Look(ref ContextBudget, "ContextBudget", 0.6f);
            Scribe_Values.Look(ref BudgetW1, "BudgetW1", 0.4f);
            Scribe_Values.Look(ref BudgetW2, "BudgetW2", 0.6f);
        }

        public void ApplyPreset(ContextPreset preset)
        {
            switch (preset)
            {
                case ContextPreset.Minimal:
                    IncludeRace = true;  IncludeAge = false; IncludeGender = false;
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
                    IncludeRace = true;  IncludeAge = true; IncludeGender = true;
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
                    ContextBudget = 0.6f;
                    break;
                case ContextPreset.Full:
                    IncludeRace = true;  IncludeAge = true; IncludeGender = true;
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
    }

    public enum ContextPreset { Minimal, Standard, Full, Custom }
}
