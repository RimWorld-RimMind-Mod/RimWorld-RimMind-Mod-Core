namespace RimMind.Application.Common.Models.Client
{
    /// <summary>
    /// Represents a configured API endpoint node in the primary + backup failover list.
    /// </summary>
    public sealed class BackupEndpointNode
    {
        public string Name { get; set; } = "Backup Node";
        public string Provider { get; set; } = "openai";
        public string ApiEndpoint { get; set; } = string.Empty;
        public string ModelName { get; set; } = string.Empty;
        public string ApiKey { get; set; } = string.Empty;
        public bool Enabled { get; set; } = true;

        public bool IsConfigured =>
            Enabled &&
            !string.IsNullOrWhiteSpace(ApiEndpoint) &&
            !string.IsNullOrWhiteSpace(ApiKey) &&
            !string.IsNullOrWhiteSpace(ModelName);
    }
}
