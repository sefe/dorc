namespace Dorc.ApiModel
{
    public class ApiApiModel
    {
        public int Id { get; set; }

        public int EnvironmentId { get; set; }

        public string EnvironmentName { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string Endpoint { get; set; } = string.Empty;

        public string EndpointResolved { get; set; } = string.Empty;

        public ApiEndpointResolutionStatus ResolutionStatus { get; set; }

        public string? UnresolvedTokens { get; set; }

        public string? Description { get; set; }

        public string Type { get; set; } = string.Empty;

        public string AuthType { get; set; } = string.Empty;

        public string? HealthCheckPath { get; set; }

        public int? OwnerProjectId { get; set; }

        public string? OwnerProjectName { get; set; }

        public string? Tags { get; set; }

        public bool UserEditable { get; set; }
    }

    public enum ApiEndpointResolutionStatus
    {
        NoTokens = 0,
        Resolved = 1,
        PartiallyResolved = 2
    }
}
