namespace RimMind.Application.Common.Interfaces.Internal
{
    /// <summary>
    /// Pawn-related include settings: personal attributes, skills, health, mood, etc.
    /// </summary>
    public interface IPawnIncludeSettings
    {
        bool IncludeRace { get; set; }
        bool IncludeAge { get; set; }
        bool IncludeGender { get; set; }
        bool IncludeBackstory { get; set; }
        bool IncludeIdeology { get; set; }
        bool IncludeTraits { get; set; }
        bool IncludeSkills { get; set; }
        int MinSkillLevel { get; set; }
        bool IncludeHealth { get; set; }
        bool IncludeCapacities { get; set; }
        bool IncludeMood { get; set; }
        bool IncludeMoodThoughts { get; set; }
        bool IncludeCurrentJob { get; set; }
        bool IncludeWorkPriorities { get; set; }
        bool IncludeEquipment { get; set; }
        bool IncludeInventory { get; set; }
        bool IncludeLocation { get; set; }
        bool IncludeRelations { get; set; }
        bool IncludeGenes { get; set; }
        bool IncludeSurroundings { get; set; }
        bool IncludeCombatStatus { get; set; }
    }

    /// <summary>
    /// Map environment include settings: time, season, weather.
    /// </summary>
    public interface IMapIncludeSettings
    {
        bool IncludeGameTime { get; set; }
        bool IncludeSeason { get; set; }
        bool IncludeWeather { get; set; }
    }

    /// <summary>
    /// Colony status include settings: colonists, wealth, food, threats.
    /// </summary>
    public interface IColonyIncludeSettings
    {
        bool IncludeColonistCount { get; set; }
        bool IncludeColonistNames { get; set; }
        bool IncludeWealth { get; set; }
        bool IncludeFood { get; set; }
        bool IncludeThreats { get; set; }
    }

    /// <summary>
    /// Composite interface combining all include settings sub-interfaces.
    /// Maintains backward compatibility with existing code.
    /// </summary>
    public interface IContextIncludeSettings : IPawnIncludeSettings, IMapIncludeSettings, IColonyIncludeSettings
    {
    }
}
