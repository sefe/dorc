namespace Dorc.Api.Interfaces
{
    public interface IUserGroupProvider
    {
        IUserGroupReader GetWinAuthUserGroupsReader();
        IUserGroupReader GetOAuthUserGroupsReader();
    }
}