using System.Collections.Generic;

namespace RimMind.Application.Common.Interfaces.Context
{
    public class PawnExtractedData
    {
        public string? MoodString;
        public float MoodPercent;
        public bool HasMap;
        public float Temperature;
        public string Name = "";
        public string Gender = "";
        public string GenderLabel = "";
        public int AgeBiological;
        public string Age = "";
        public string Race = "";
        public string RaceLabel = "";
        public string Title = "";
        public string Faction = "";
        public string IdeologyName = "";
        public string ChildhoodTitle = "";
        public string AdulthoodTitle = "";
        public Dictionary<string, int> Skills = new Dictionary<string, int>();
        public List<string> Traits = new List<string>();
        public string TraitLabels = "";
        public List<RelationEntry> Relations = new List<RelationEntry>();
        public string HealthSummary = "";
        public string EquippedWeapon = "";
        public string WeaponLabel = "";
        public bool Drafted;
        public string EnemyTargetLabel = "";
        public float? EnemyTargetHpPercent;
        public string RoomLabel = "";
        public string WeatherLabel = "";
        public string TimeString = "";
        public string NearbyPawnNames = "";
        public string SeasonLabel = "";
        public int ColonistCount;
        public float ColonyWealth;
        public int ThreatCount;
        public List<HediffEntry> Hediffs = new List<HediffEntry>();
        public bool InMentalState;
        public string MentalStateInspectLine = "";
        public string CurrentJobReport = "";
        public string CurrentJobDefLabel = "";
        public string IdeologyMemes = "";
        public List<string> NotableGenes = new List<string>();
        public bool Downed;
        public List<MoodThoughtEntry> MoodThoughts = new List<MoodThoughtEntry>();
        public List<CapacityEntry> Capacities = new List<CapacityEntry>();
        public List<WorkPriorityEntry> WorkPriorities = new List<WorkPriorityEntry>();
        public List<string> ApparelLabels = new List<string>();
        public Dictionary<string, int> InventoryItems = new Dictionary<string, int>();
        public bool InCombat;
    }

    public class RelationEntry
    {
        public string RelationLabel = "";
        public string OtherName = "";
    }

    public class HediffEntry
    {
        public string HediffLabel = "";
        public bool Visible = true;
        public bool IsBad;
        public float Severity;
        public string? PartLabel;
    }

    public class MoodThoughtEntry
    {
        public string Label = "";
        public float Offset;
    }

    public class CapacityEntry
    {
        public string Label = "";
        public float Level;
    }

    public class WorkPriorityEntry
    {
        public string Label = "";
        public int Priority;
    }
}
