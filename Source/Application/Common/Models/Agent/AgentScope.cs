namespace RimMind.Application.Common.Models.Agent
{
    public sealed record AgentScope
    {
        public AgentScopeKind Kind { get; }
        public string Id { get; }
        public int? MapId { get; }
        public string? OwnerModId { get; }
        public string ScopeType { get; }
        public string CompositeKey => MapId.HasValue
            ? ScopeType + ":" + Id + ":map:" + MapId.Value
            : ScopeType + ":" + Id;

        public AgentScope(AgentScopeKind Kind, string Id, int? MapId = null, string? OwnerModId = null)
            : this(Kind, Id, MapId, OwnerModId, Kind.ToString())
        {
        }

        private AgentScope(AgentScopeKind kind, string id, int? mapId, string? ownerModId, string scopeType)
        {
            Kind = kind;
            Id = Normalize(id);
            MapId = mapId;
            OwnerModId = NormalizeOptional(ownerModId);
            ScopeType = Normalize(scopeType);
        }

        public static AgentScope Pawn(string pawnId, int? mapId = null, string? ownerModId = null)
            => new(AgentScopeKind.Pawn, pawnId, mapId, ownerModId);

        public static AgentScope Storyteller(string storytellerId = "storyteller", string? ownerModId = null)
            => new(AgentScopeKind.Storyteller, storytellerId, null, ownerModId);

        public static AgentScope Map(int mapId, string? ownerModId = null)
            => new(AgentScopeKind.Map, mapId.ToString(), mapId, ownerModId);

        public static AgentScope Map(string mapId, int? numericMapId = null, string? ownerModId = null)
            => new(AgentScopeKind.Map, mapId, numericMapId, ownerModId);

        public static AgentScope Thing(string thingId, int? mapId = null, string? ownerModId = null)
            => new(AgentScopeKind.Thing, thingId, mapId, ownerModId);

        public static AgentScope Global(string globalId = "global", string? ownerModId = null)
            => new(AgentScopeKind.Global, globalId, null, ownerModId);

        public static AgentScope Custom(string scopeType, string scopeId, int? mapId = null, string? ownerModId = null)
            => new(AgentScopeKind.Custom, scopeId, mapId, ownerModId, scopeType);

        private static string Normalize(string? value)
            => string.IsNullOrWhiteSpace(value) ? "unknown" : value!;

        private static string? NormalizeOptional(string? value)
            => string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
