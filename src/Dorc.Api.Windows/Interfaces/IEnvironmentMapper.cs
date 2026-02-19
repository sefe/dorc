using Dorc.ApiModel;
using System.Security.Claims;

namespace Dorc.Api.Windows.Interfaces;

public interface IEnvironmentMapper
{
    EnvironmentApiModel GetEnvironmentByDatabase(int envId, int databaseId, ClaimsPrincipal user);
}