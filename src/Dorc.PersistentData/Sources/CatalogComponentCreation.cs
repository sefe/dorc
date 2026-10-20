using Dorc.ApiModel;

namespace Dorc.PersistentData.Sources;

public enum CatalogComponentCreationStatus
{
    Created,
    Reused,
    Conflict,
    Disabled,
    InvalidParent,
    ProjectNotFound
}

public sealed record CatalogComponentCreation(
    CatalogComponentCreationStatus Status,
    ComponentApiModel? Component = null,
    string? Message = null);
