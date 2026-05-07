using System.Text.Json.Serialization;

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

        public string UnresolvedTokens { get; set; }

        public string Description { get; set; }

        public string Type { get; set; } = string.Empty;

        public string AuthType { get; set; } = string.Empty;

        public string HealthCheckPath { get; set; }

        public int? OwnerProjectId { get; set; }

        public string OwnerProjectName { get; set; }

        public string Tags { get; set; }

        public bool UserEditable { get; set; }
    }

    // Serialise as the enum name string, not the integer. The global Program.cs JSON
    // options register JsonStringEnumConverter, but the TS client (generated from the
    // OpenAPI spec) treats this enum as a string enum ('NoTokens' / 'Resolved' /
    // 'PartiallyResolved'). The explicit type-level converter pins the wire format
    // here so the FE↔BE contract stays correct even if a future code path bypasses
    // the global pipeline.
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ApiEndpointResolutionStatus
    {
        NoTokens = 0,
        Resolved = 1,
        PartiallyResolved = 2
    }
}
