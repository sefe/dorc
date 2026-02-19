using Dorc.Api.Windows.Interfaces;

namespace Dorc.Api.Windows.Interfaces
{
    /// <summary>
    /// Provides user group readers for different authentication schemes
    /// </summary>
    public interface IUserGroupProvider
    {
        IUserGroupReader GetWinAuthUserGroupsReader();
        IUserGroupReader GetOAuthUserGroupsReader();
    }
}