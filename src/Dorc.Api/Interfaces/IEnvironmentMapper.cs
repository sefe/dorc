using Dorc.ApiModel;
using System.Security.Claims;

namespace Dorc.Api.Interfaces;

public interface IEnvironmentMapper
{
    EnvironmentApiModel GetEnvironmentByDatabase(int envId, int databaseId, ClaimsPrincipal user);
}