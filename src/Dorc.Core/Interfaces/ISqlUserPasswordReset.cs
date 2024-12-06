using Dorc.ApiModel;

namespace Dorc.Core.Interfaces
{
    public interface ISqlUserPasswordReset
    {
        ApiBoolResult ResetSqlUserPassword(string targetDbServer, string username);
    }
}